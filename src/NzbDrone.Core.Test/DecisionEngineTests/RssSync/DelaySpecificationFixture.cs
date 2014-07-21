﻿using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine.Specifications.RssSync;
using NzbDrone.Core.History;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.DecisionEngineTests.RssSync
{
    [TestFixture]
    public class DelaySpecificationFixture : CoreTest<DelaySpecification>
    {
        private Profile _profile;
        private RemoteEpisode _remoteEpisode;

        [SetUp]
        public void Setup()
        {
            _profile = Builder<Profile>.CreateNew()
                                       .Build();

            var series = Builder<Series>.CreateNew()
                                        .With(s => s.Profile = _profile)
                                        .Build();

            _remoteEpisode = Builder<RemoteEpisode>.CreateNew()
                                                   .With(r => r.Series = series)
                                                   .Build();

            _profile.Items = new List<ProfileQualityItem>();
            _profile.Items.Add(new ProfileQualityItem { Allowed = true, Quality = Quality.HDTV720p });
            _profile.Items.Add(new ProfileQualityItem { Allowed = true, Quality = Quality.WEBDL720p });
            _profile.Items.Add(new ProfileQualityItem { Allowed = true, Quality = Quality.Bluray720p });

            _profile.Cutoff = Quality.WEBDL720p;

            _remoteEpisode.ParsedEpisodeInfo = new ParsedEpisodeInfo();
            _remoteEpisode.Release = new ReleaseInfo();

            _remoteEpisode.Episodes =Builder<Episode>.CreateListOfSize(1).Build().ToList();
        }

        [Test]
        public void should_be_true_when_search()
        {
            Subject.IsSatisfiedBy(new RemoteEpisode(), new SingleEpisodeSearchCriteria()).Should().BeTrue();
        }

        [Test]
        public void should_be_true_when_profile_does_not_have_a_delay()
        {
            _profile.GrabDelay = 0;

            Subject.IsSatisfiedBy(_remoteEpisode, null).Should().BeTrue();
        }

        [Test]
        public void should_be_true_when_quality_is_last_allowed_in_profile()
        {
            _remoteEpisode.ParsedEpisodeInfo.Quality = new QualityModel(Quality.Bluray720p);

            Subject.IsSatisfiedBy(_remoteEpisode, null).Should().BeTrue();
        }

        [Test]
        public void should_be_true_when_release_is_older_than_delay()
        {
            _remoteEpisode.ParsedEpisodeInfo.Quality = new QualityModel(Quality.HDTV720p);
            _remoteEpisode.Release.PublishDate = DateTime.UtcNow.AddHours(-10);
            
            _profile.GrabDelay = 1;

            Subject.IsSatisfiedBy(_remoteEpisode, null).Should().BeTrue();
        }

        [Test]
        public void should_be_false_when_release_is_first_in_profile_and_mode_is_first()
        {
            _profile.GrabDelayMode = GrabDelayMode.First;
            _remoteEpisode.ParsedEpisodeInfo.Quality = new QualityModel(Quality.HDTV720p);
            _remoteEpisode.Release.PublishDate = DateTime.UtcNow;

            _profile.GrabDelay = 12;

            Subject.IsSatisfiedBy(_remoteEpisode, null).Should().BeFalse();
        }

        [Test]
        public void should_be_false_when_release_is_younger_than_delay()
        {
            _remoteEpisode.ParsedEpisodeInfo.Quality = new QualityModel(Quality.SDTV);
            _remoteEpisode.Release.PublishDate = DateTime.UtcNow;

            _profile.GrabDelay = 12;

            Subject.IsSatisfiedBy(_remoteEpisode, null).Should().BeFalse();
        }

        [Test]
        public void should_be_true_when_release_is_proper_for_existing_episode()
        {
            _remoteEpisode.ParsedEpisodeInfo.Quality = new QualityModel(Quality.HDTV720p, true);
            _remoteEpisode.Release.PublishDate = DateTime.UtcNow;

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.GetBestQualityInHistory(It.IsAny<Profile>(), It.IsAny<Int32>()))
                  .Returns(new QualityModel(Quality.HDTV720p));

            _profile.GrabDelay = 12;

            Subject.IsSatisfiedBy(_remoteEpisode, null).Should().BeTrue();
        }

        [Test]
        public void should_be_false_when_release_is_proper_for_existing_episode_of_different_quality()
        {
            _remoteEpisode.ParsedEpisodeInfo.Quality = new QualityModel(Quality.HDTV720p, true);
            _remoteEpisode.Release.PublishDate = DateTime.UtcNow;

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.GetBestQualityInHistory(It.IsAny<Profile>(), It.IsAny<Int32>()))
                  .Returns(new QualityModel(Quality.SDTV));

            _profile.GrabDelay = 12;

            Subject.IsSatisfiedBy(_remoteEpisode, null).Should().BeFalse();
        }

        [Test]
        public void should_be_false_when_release_is_proper_and_no_existing_episode()
        {
            
        }

        [Test]
        public void should_be_false_when_release_is_first_in_profile_and_proper_and_type_is_first()
        {
            _profile.GrabDelayMode = GrabDelayMode.First;
            _remoteEpisode.ParsedEpisodeInfo.Quality = new QualityModel(Quality.HDTV720p, true);
            _remoteEpisode.Release.PublishDate = DateTime.UtcNow;

            _profile.GrabDelay = 12;

            Subject.IsSatisfiedBy(_remoteEpisode, null).Should().BeFalse();
        }

        [Test]
        public void should_be_true_when_release_meets_cutoff_and_mode_is_cutoff()
        {
            _profile.GrabDelayMode = GrabDelayMode.Cutoff;
            _remoteEpisode.ParsedEpisodeInfo.Quality = new QualityModel(Quality.WEBDL720p);
            _remoteEpisode.Release.PublishDate = DateTime.UtcNow;

            _profile.GrabDelay = 12;

            Subject.IsSatisfiedBy(_remoteEpisode, null).Should().BeTrue();
        }

        [Test]
        public void should_be_true_when_release_exceeds_cutoff_and_mode_is_cutoff()
        {
            _profile.GrabDelayMode = GrabDelayMode.Cutoff;
            _remoteEpisode.ParsedEpisodeInfo.Quality = new QualityModel(Quality.Bluray720p);
            _remoteEpisode.Release.PublishDate = DateTime.UtcNow;

            _profile.GrabDelay = 12;

            Subject.IsSatisfiedBy(_remoteEpisode, null).Should().BeTrue();
        }

        [Test]
        public void should_be_false_when_release_is_below_cutoff_and_mode_is_cutoff()
        {
            _profile.GrabDelayMode = GrabDelayMode.Cutoff;
            _remoteEpisode.ParsedEpisodeInfo.Quality = new QualityModel(Quality.HDTV720p);
            _remoteEpisode.Release.PublishDate = DateTime.UtcNow;

            _profile.GrabDelay = 12;

            Subject.IsSatisfiedBy(_remoteEpisode, null).Should().BeFalse();
        }

        [Test]
        public void should_be_true_when_release_is_not_first_and_mode_is_first()
        {
            _profile.GrabDelayMode = GrabDelayMode.First;

            _remoteEpisode.ParsedEpisodeInfo.Quality = new QualityModel(Quality.WEBDL720p);
            _remoteEpisode.Release.PublishDate = DateTime.UtcNow;
            _profile.GrabDelay = 12;

            Subject.IsSatisfiedBy(_remoteEpisode, null).Should().BeTrue();
        }
    }
}
