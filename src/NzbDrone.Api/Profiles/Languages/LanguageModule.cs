using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Parser;

namespace NzbDrone.Api.Profiles.Languages
{
    public class LanguageModule : NzbDroneRestModule<LanguageResource>
    {
        public LanguageModule()
        {
            GetResourceAll = GetAll;
            GetResourceById = GetById;
        }

        private LanguageResource GetById(int id)
        {
            var language = (Language)id;

            return new LanguageResource
            {
                Id = (int)language,
                Name = language.ToString()
            };
        }

        private List<LanguageResource> GetAll()
        {
            var languages = new List<LanguageResource>();

            foreach (Language language in Enum.GetValues(typeof(Language)))
            {
                languages.Add(new LanguageResource
                              {
                                  Id = (int) language,
                                  Name = language.ToString()
                              });
            }

            return languages.OrderBy(l => l.Name).ToList();
        }
    }
}