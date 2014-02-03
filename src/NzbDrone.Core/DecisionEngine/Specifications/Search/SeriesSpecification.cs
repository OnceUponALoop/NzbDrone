using NLog;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications.Search
{
    public class SeriesSpecification : IDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public SeriesSpecification(Logger logger)
        {
            _logger = logger;
        }

        public string RejectionReason
        {
            get
            {
                return "Wrong series";
            }
        }

        public bool IsSatisfiedBy(RemoteEpisode remoteEpisode, SearchCriteriaBase searchCriteria)
        {
            if (searchCriteria == null)
            {
                return true;
            }

            _logger.Trace("Checking if series matches searched series");

            if (remoteEpisode.Series.Id != searchCriteria.Series.Id)
            {
                _logger.Trace("Series {0} does not match {1}", remoteEpisode.Series, searchCriteria.Series);
                return false;
            }

            return true;
        }
    }
}