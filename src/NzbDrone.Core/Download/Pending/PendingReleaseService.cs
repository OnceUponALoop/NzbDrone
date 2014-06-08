using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Download.Pending
{
    public interface IPendingReleaseService
    {
        void Add(DownloadDecision decision);
        void RemoveGrabbed(List<DownloadDecision> grabbed);
        IEnumerable<ReleaseInfo> GetPending();
        List<Queue.Queue> GetPendingQueue();
        void ForceGrab(Int32 id);
        void Remove(Int32 id);
    }

    public class PendingReleaseService : IPendingReleaseService, IHandle<SeriesDeletedEvent>
    {
        private readonly IPendingReleaseRepository _repository;
        private readonly ISeriesService _seriesService;
        private readonly IParsingService _parsingService;
        private readonly IDownloadService _downloadService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IPrioritizeDownloadDecision _prioritizeDownloadDecision;
        private readonly Logger _logger;

        public PendingReleaseService(IPendingReleaseRepository repository,
                                    ISeriesService seriesService,
                                    IParsingService parsingService,
                                    IDownloadService downloadService,
                                    IEventAggregator eventAggregator,
                                    IPrioritizeDownloadDecision prioritizeDownloadDecision,
                                    Logger logger)
        {
            _repository = repository;
            _seriesService = seriesService;
            _parsingService = parsingService;
            _downloadService = downloadService;
            _eventAggregator = eventAggregator;
            _prioritizeDownloadDecision = prioritizeDownloadDecision;
            _logger = logger;
        }

        public void Add(DownloadDecision decision)
        {
            var alreadyPending = GetPendingReleases();

            var profile = decision.RemoteEpisode.Series.Profile.Value;
            var episodeIds = decision.RemoteEpisode.Episodes.Select(e => e.Id);

            var existingReport = alreadyPending.FirstOrDefault(r => r.SeriesId == decision.RemoteEpisode.Series.Id &&
                                                                r.RemoteEpisode.Episodes.Select(e => e.Id).Intersect(episodeIds)
                                                                 .Any());

            if (existingReport != null)
            {
                var compare = new QualityModelComparer(profile).Compare(existingReport.ParsedEpisodeInfo.Quality,
                                                                    decision.RemoteEpisode.ParsedEpisodeInfo.Quality);

                if (compare >= 0)
                {
                    _logger.Debug("Existing pending release meets or exceeds quality");
                    return;
                }

                _logger.Debug("Removing previously pending release, with lower quality");
                Delete(existingReport);
            }

            _logger.Debug("Delaying grab of release");
            Insert(decision, profile);
        }

        public void RemoveGrabbed(List<DownloadDecision> grabbed)
        {
            var alreadyPending = GetPendingReleases();

            foreach (var decision in grabbed)
            {
                var episodeIds = decision.RemoteEpisode.Episodes.Select(e => e.Id);

                var existingReport = alreadyPending.FirstOrDefault(r => r.SeriesId == decision.RemoteEpisode.Series.Id &&
                                                                        r.RemoteEpisode.Episodes.Select(e => e.Id)
                                                                         .Intersect(episodeIds)
                                                                         .Any());

                if (existingReport != null)
                {
                    _logger.Debug("Removing previously pending release, as it was grabbed.");
                    Delete(existingReport);
                }
            }
        }

        public IEnumerable<ReleaseInfo> GetPending()
        {
            return _repository.All().Select(p => p.Release);
        }

        public List<Queue.Queue> GetPendingQueue()
        {
            var queued = new List<Queue.Queue>();

            foreach (var pendingRelease in GetPendingReleases())
            {
                foreach (var episode in pendingRelease.RemoteEpisode.Episodes)
                {
                    var queue = new Queue.Queue();
                    queue.Id = pendingRelease.Id; //Not sure if this is going to be unique enough, queue is a little awkward with IDs
                    queue.Series = pendingRelease.RemoteEpisode.Series;
                    queue.Episode = episode;
                    queue.Quality = pendingRelease.RemoteEpisode.ParsedEpisodeInfo.Quality;
                    queue.Title = pendingRelease.Title;
                    queue.Size = pendingRelease.RemoteEpisode.Release.Size;
                    queue.Sizeleft = pendingRelease.RemoteEpisode.Release.Size;
                    queue.Timeleft = pendingRelease.RetryTime.Subtract(DateTime.UtcNow);
                    queue.Status = "Pending";
                    queue.RemoteEpisode = pendingRelease.RemoteEpisode;
                    queued.Add(queue);
                }
            }

            return queued;
        }

        public void ForceGrab(Int32 id)
        {
            var release = _repository.Get(id);
            var remoteEpisode = GetRemoteEpisode(release);

            _downloadService.DownloadReport(remoteEpisode);
        }

        public void Remove(Int32 id)
        {
            _repository.Delete(id);
        }

        private List<DownloadDecision> GetQualifiedReports(List<DownloadDecision> decisions)
        {
            var prioritizedDecisions = _prioritizeDownloadDecision.PrioritizeDecisions(decisions);

            return prioritizedDecisions.Where(c => c.TemporarilyRejected && c.RemoteEpisode.Episodes.Any()).ToList();
        }

        private List<PendingRelease> GetPendingReleases()
        {
            var result = new List<PendingRelease>();

            foreach (var release in _repository.All())
            {
                var remoteEpisode = GetRemoteEpisode(release);

                if (remoteEpisode == null) continue;

                release.RemoteEpisode = remoteEpisode;

                result.Add(release);
            }

            return result;
        }

        private RemoteEpisode GetRemoteEpisode(PendingRelease release)
        {
            var series = _seriesService.GetSeries(release.SeriesId);

            //Just in case the series was removed, but wasn't cleaned up yet (housekeeper will clean it up)
            if (series == null) return null;

            var episodes = _parsingService.GetEpisodes(release.ParsedEpisodeInfo, series, true);

            return new RemoteEpisode
            {
                Series = series,
                Episodes = episodes,
                ParsedEpisodeInfo = release.ParsedEpisodeInfo,
                Release = release.Release
            };
        }

        private void Insert(DownloadDecision decision, Profile profile)
        {
            var retryTime = decision.RemoteEpisode.Release.PublishDate.AddHours(profile.GrabDelay);

            _repository.Insert(new PendingRelease
            {
                SeriesId = decision.RemoteEpisode.Series.Id,
                ParsedEpisodeInfo = decision.RemoteEpisode.ParsedEpisodeInfo,
                Release = decision.RemoteEpisode.Release,
                Title = decision.RemoteEpisode.Release.Title,
                Added = DateTime.UtcNow,
                RetryTime = retryTime
            });

            _eventAggregator.PublishEvent(new PendingReleasesUpdatedEvent());
        }

        private void Delete(PendingRelease pendingRelease)
        {
            _repository.Delete(pendingRelease);
            _eventAggregator.PublishEvent(new PendingReleasesUpdatedEvent());
        }

        public void Handle(SeriesDeletedEvent message)
        {
            _repository.DeleteBySeriesId(message.Series.Id);
        }
    }
}
