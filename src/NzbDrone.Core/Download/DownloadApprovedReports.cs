﻿using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Download
{
    public interface IDownloadApprovedReports
    {
        List<DownloadDecision> DownloadApproved(List<DownloadDecision> decisions);
    }

    public class DownloadApprovedReports : IDownloadApprovedReports
    {
        private readonly IDownloadService _downloadService;
        private readonly IPrioritizeDownloadDecision _prioritizeDownloadDecision;
        private readonly Logger _logger;

        public DownloadApprovedReports(IDownloadService downloadService, IPrioritizeDownloadDecision prioritizeDownloadDecision, Logger logger)
        {
            _downloadService = downloadService;
            _prioritizeDownloadDecision = prioritizeDownloadDecision;
            _logger = logger;
        }

        public List<DownloadDecision> DownloadApproved(List<DownloadDecision> decisions)
        {
            var qualifiedReports = GetQualifiedReports(decisions);
            var prioritizedDecisions = _prioritizeDownloadDecision.PrioritizeDecisions(qualifiedReports);
            var downloadedReports = new List<DownloadDecision>();

            foreach (var report in prioritizedDecisions)
            {
                var remoteEpisode = report.RemoteEpisode;

                try
                {
                    if (downloadedReports.SelectMany(r => r.RemoteEpisode.Episodes)
                                         .Select(e => e.Id)
                                         .ToList()
                                         .Intersect(remoteEpisode.Episodes.Select(e => e.Id))
                                         .Any())
                    {
                        continue;
                    }

                    _downloadService.DownloadReport(remoteEpisode);
                    downloadedReports.Add(report);
                }
                catch (Exception e)
                {
                    _logger.WarnException("Couldn't add report to download queue. " + remoteEpisode, e);
                }
            }

            return downloadedReports;
        }

        public List<DownloadDecision> GetQualifiedReports(IEnumerable<DownloadDecision> decisions)
        {
            return decisions.Where(c => c.Approved && c.RemoteEpisode.Episodes.Any()).ToList();
        }
    }
}
