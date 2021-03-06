using System;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using System.Collections.Generic;


namespace NzbDrone.Core.DataAugmentation.Scene
{
    public interface ISceneMappingRepository : IBasicRepository<SceneMapping>
    {
        List<SceneMapping> FindByTvdbid(int tvdbId);
        void Clear(string type);
    }

    public class SceneMappingRepository : BasicRepository<SceneMapping>, ISceneMappingRepository
    {
        public SceneMappingRepository(IDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<SceneMapping> FindByTvdbid(int tvdbId)
        {
            return Query.Where(x => x.TvdbId == tvdbId);
        }

        public void Clear(string type)
        {
            Delete(s => s.Type == type);
        }
    }
}