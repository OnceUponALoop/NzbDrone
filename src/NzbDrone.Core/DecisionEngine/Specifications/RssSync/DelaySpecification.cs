using System.Linq;
using NLog;
using NzbDrone.Core.History;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.DecisionEngine.Specifications.RssSync
{
    public class DelaySpecification : IDecisionEngineSpecification
    {
        private readonly IHistoryService _historyService;
        private readonly Logger _logger;

        public DelaySpecification(IHistoryService historyService, Logger logger)
        {
            _historyService = historyService;
            _logger = logger;
        }

        public string RejectionReason
        {
            get
            {
                return "Waiting for better quality release";
            }
        }

        public RejectionType Type { get { return RejectionType.Temporary; } }

        public virtual bool IsSatisfiedBy(RemoteEpisode subject, SearchCriteriaBase searchCriteria)
        {
            //How do we want to handle drone being off and the automatic search being triggered?
            //TODO: Add a flag to the search to state it is a "scheduled" search

            if (searchCriteria != null)
            {
                _logger.Debug("Ignore delay for searches");
                return true;
            }

            var profile = subject.Series.Profile.Value;

            if (profile.GrabDelay == 0)
            {
                _logger.Debug("Profile does not delay before download");
                return true;
            }

            var comparer = new QualityModelComparer(profile);

            if (subject.ParsedEpisodeInfo.Quality.Proper)
            {
                foreach (var episode in subject.Episodes)
                {
                    var bestInHistory = _historyService.GetBestQualityInHistory(profile, episode.Id);

                    if (bestInHistory != null && comparer.Compare(subject.ParsedEpisodeInfo.Quality, bestInHistory) > 0)
                    {
                        var properCompare = subject.ParsedEpisodeInfo.Quality.Proper.CompareTo(bestInHistory.Proper);

                        if (subject.ParsedEpisodeInfo.Quality.Quality == bestInHistory.Quality && properCompare > 0)
                        {
                            _logger.Debug("New quality is a proper for existing quality, skipping delay");
                            return true;
                        }
                    }
                }
            }

            //If quality meets or exceeds the best allowed quality in the profile accept it immediately
            var bestQualityInProfile = new QualityModel(profile.Items.Last(q => q.Allowed).Quality);
            var bestCompare = comparer.Compare(subject.ParsedEpisodeInfo.Quality, bestQualityInProfile);

            if (bestCompare >= 0)
            {
                _logger.Debug("Quality is highest in profile, will not delay");
                return true;
            }

            if (profile.GrabDelayMode == GrabDelayMode.Cutoff)
            {
                var cutoff = new QualityModel(profile.Cutoff);
                var cutoffCompare = comparer.Compare(subject.ParsedEpisodeInfo.Quality, cutoff);

                if (cutoffCompare >= 0)
                {
                    _logger.Debug("Quality meets or exceeds the cutoff, will not delay");
                    return true;
                }
            }

            if (profile.GrabDelayMode == GrabDelayMode.First)
            {
                var worstQualityInProfile = new QualityModel(profile.Items.First(q => q.Allowed).Quality);
                var worstCompare = comparer.Compare(subject.ParsedEpisodeInfo.Quality, worstQualityInProfile);

                //Don't skip the delay if its a proper for the lowest quality in the profile and nothing has been grabbed
                if (worstCompare > 0 && subject.ParsedEpisodeInfo.Quality.Proper.CompareTo(worstQualityInProfile.Proper) <= 0)
                {
                    _logger.Debug("Quality is not lowest in profile, will not delay");
                    return true;
                }
            }

            if (subject.Release.AgeHours < profile.GrabDelay)
            {
                

                _logger.Debug("Age ({0}) is less than delay {1}, delaying", subject.Release.AgeHours, profile.GrabDelay);
                return false;
            }

            return true;
        }
    }
}
