﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Download.Clients.Sabnzbd
{
    public class Sabnzbd : DownloadClientBase<SabnzbdSettings>
    {
        private readonly IHttpProvider _httpProvider;
        private readonly ISabnzbdProxy _proxy;

        public Sabnzbd(ISabnzbdProxy proxy,
                       IHttpProvider httpProvider,
                       IConfigService configService,
                       IDiskProvider diskProvider,
                       IParsingService parsingService,
                       Logger logger)
            : base(configService, diskProvider, parsingService, logger)
        {
            _proxy = proxy;
            _httpProvider = httpProvider;
        }

        public override DownloadProtocol Protocol
        {
            get
            {
                return DownloadProtocol.Usenet;
            }
        }

        public override string Download(RemoteEpisode remoteEpisode)
        {
            var url = remoteEpisode.Release.DownloadUrl;
            var title = remoteEpisode.Release.Title;
            var category = Settings.TvCategory;
            var priority = remoteEpisode.IsRecentEpisode() ? Settings.RecentTvPriority : Settings.OlderTvPriority;

            using (var nzb = _httpProvider.DownloadStream(url))
            {
                _logger.Info("Adding report [{0}] to the queue.", title);
                var response = _proxy.DownloadNzb(nzb, title, category, priority, Settings);

                if (response != null && response.Ids.Any())
                {
                    return response.Ids.First();
                }

                return null;
            }
        }

        private IEnumerable<DownloadClientItem> GetQueue()
        {
            SabnzbdQueue sabQueue;

            try
            {
                sabQueue = _proxy.GetQueue(0, 0, Settings);
            }
            catch (DownloadClientException ex)
            {
                _logger.ErrorException(ex.Message, ex);
                return Enumerable.Empty<DownloadClientItem>();
            }

            var queueItems = new List<DownloadClientItem>();

            foreach (var sabQueueItem in sabQueue.Items)
            {
                var queueItem = new DownloadClientItem();
                queueItem.DownloadClient = Definition.Name;
                queueItem.DownloadClientId = sabQueueItem.Id;
                queueItem.Category = sabQueueItem.Category;
                queueItem.Title = sabQueueItem.Title;
                queueItem.TotalSize = (long)(sabQueueItem.Size * 1024 * 1024);
                queueItem.RemainingSize = (long)(sabQueueItem.Sizeleft * 1024 * 1024);
                queueItem.RemainingTime = sabQueueItem.Timeleft;

                if (sabQueue.Paused || sabQueueItem.Status == SabnzbdDownloadStatus.Paused)
                {
                    queueItem.Status = DownloadItemStatus.Paused;

                    queueItem.RemainingTime = null;
                }
                else if (sabQueueItem.Status == SabnzbdDownloadStatus.Queued || sabQueueItem.Status == SabnzbdDownloadStatus.Grabbing)
                {
                    queueItem.Status = DownloadItemStatus.Queued;
                }
                else
                {
                    queueItem.Status = DownloadItemStatus.Downloading;
                }

                if (queueItem.Title.StartsWith("ENCRYPTED /"))
                {
                    queueItem.Title = queueItem.Title.Substring(11);
                    queueItem.IsEncrypted = true;
                }

                queueItems.Add(queueItem);
            }

            return queueItems;
        }

        private IEnumerable<DownloadClientItem> GetHistory()
        {
            SabnzbdHistory sabHistory;

            try
            {
                sabHistory = _proxy.GetHistory(0, _configService.DownloadClientHistoryLimit, Settings);
            }
            catch (DownloadClientException ex)
            {
                _logger.ErrorException(ex.Message, ex);
                return Enumerable.Empty<DownloadClientItem>();
            }

            var historyItems = new List<DownloadClientItem>();

            foreach (var sabHistoryItem in sabHistory.Items)
            {
                var historyItem = new DownloadClientItem
                {
                    DownloadClient = Definition.Name,
                    DownloadClientId = sabHistoryItem.Id,
                    Category = sabHistoryItem.Category,
                    Title = sabHistoryItem.Title,

                    TotalSize = sabHistoryItem.Size,
                    RemainingSize = 0,
                    DownloadTime = TimeSpan.FromSeconds(sabHistoryItem.DownloadTime),
                    RemainingTime = TimeSpan.Zero,

                    Message = sabHistoryItem.FailMessage
                };

                if (sabHistoryItem.Status == SabnzbdDownloadStatus.Failed)
                {
                    historyItem.Status = DownloadItemStatus.Failed;
                }
                else if (sabHistoryItem.Status == SabnzbdDownloadStatus.Completed)
                {
                    historyItem.Status = DownloadItemStatus.Completed;
                }
                else // Verifying/Moving etc
                {
                    historyItem.Status = DownloadItemStatus.Downloading;
                }

                if (!sabHistoryItem.Storage.IsNullOrWhiteSpace())
                {
                    historyItem.OutputPath = sabHistoryItem.Storage;

                    var parent = sabHistoryItem.Storage.GetParentPath();
                    while (parent != null)
                    {
                        if (Path.GetFileName(parent) == sabHistoryItem.Title)
                        {
                            historyItem.OutputPath = parent;
                        }
                        parent = parent.GetParentPath();
                    }
                }

                historyItems.Add(historyItem);
            }

            return historyItems;
        }

        public override IEnumerable<DownloadClientItem> GetItems()
        {
            SabnzbdConfig config = null;
            SabnzbdCategory category = null;
            try
            {
                if (!Settings.TvCategoryLocalPath.IsNullOrWhiteSpace())
                {
                    config = _proxy.GetConfig(Settings);
                    category = GetCategories(config).FirstOrDefault(v => v.Name == Settings.TvCategory);
                }
            }
            catch (DownloadClientException ex)
            {
                _logger.ErrorException(ex.Message, ex);
                yield break;
            }

            foreach (var downloadClientItem in GetQueue().Concat(GetHistory()))
            {
                if (downloadClientItem.Category == Settings.TvCategory)
                {
                    if (category != null)
                    {
                        RemapStorage(downloadClientItem, category.FullPath, Settings.TvCategoryLocalPath);
                    }

                    downloadClientItem.RemoteEpisode = GetRemoteEpisode(downloadClientItem.Title);
                    if (downloadClientItem.RemoteEpisode == null) continue;

                    yield return downloadClientItem;
                }
            }
        }

        public override void RemoveItem(String id)
        {
            if (GetQueue().Any(v => v.DownloadClientId == id))
            {
                _proxy.RemoveFrom("queue", id, Settings);
            }
            else
            {
                _proxy.RemoveFrom("history", id, Settings);
            }
        }

        public override String RetryDownload(String id)
        {
            // Sabnzbd changed the nzo_id for retried downloads without reporting it back to us. We need to try to determine the new ID.

            var history = GetHistory().Where(v => v.DownloadClientId == id).ToList();

            _proxy.RetryDownload(id, Settings);

            if (history.Count() != 1)
            {
                return id;
            }

            var queue = GetQueue().Where(v => v.Category == history.First().Category && v.Title == history.First().Title).ToList();

            if (queue.Count() != 1)
            {
                return id;
            }

            return queue.First().DownloadClientId;
        }

        protected IEnumerable<SabnzbdCategory> GetCategories(SabnzbdConfig config)
        {
            var completeDir = config.Misc.complete_dir.TrimEnd('\\', '/');

            if (!completeDir.StartsWith("/") && !completeDir.StartsWith("\\") && !completeDir.Contains(':'))
            {
                var queue = _proxy.GetQueue(0, 1, Settings);

                if (queue.DefaultRootFolder.StartsWith("/"))
                {
                    completeDir = queue.DefaultRootFolder + "/" + completeDir;
                }
                else
                {
                    completeDir = queue.DefaultRootFolder + "\\" + completeDir;
                }
            }

            foreach (var category in config.Categories)
            {
                var relativeDir = category.Dir.TrimEnd('*');

                if (relativeDir.IsNullOrWhiteSpace())
                {
                    category.FullPath = completeDir;
                }
                else if (completeDir.StartsWith("/"))
                { // Process remote Linux paths irrespective of our own OS.
                    if (relativeDir.StartsWith("/"))
                    {
                        category.FullPath = relativeDir;
                    }
                    else
                    {
                        category.FullPath = completeDir + "/" + relativeDir;
                    }
                }
                else
                { // Process remote Windows paths irrespective of our own OS.
                    if (relativeDir.StartsWith("\\") || relativeDir.Contains(':'))
                    {
                        category.FullPath = relativeDir;
                    }
                    else
                    {
                        category.FullPath = completeDir + "\\" + relativeDir;
                    }
                }

                yield return category;
            }
        }

        public override DownloadClientStatus GetStatus()
        {
            var config = _proxy.GetConfig(Settings);
            var categories = GetCategories(config).ToArray();

            var category = categories.FirstOrDefault(v => v.Name == Settings.TvCategory);

            if (category == null)
            {
                category = categories.FirstOrDefault(v => v.Name == "*");
            }

            var status = new DownloadClientStatus
            {
                IsLocalhost = Settings.Host == "127.0.0.1" || Settings.Host == "localhost"
            };

            if (category != null)
            {
                if (Settings.TvCategoryLocalPath.IsNullOrWhiteSpace())
                {
                    status.OutputRootFolders = new List<String> { category.FullPath };
                }
                else
                {
                    status.OutputRootFolders = new List<String> { Settings.TvCategoryLocalPath };
                }
            }

            return status;
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            failures.AddIfNotNull(TestConnection());
            failures.AddIfNotNull(TestAuthentication());
            failures.AddIfNotNull(TestCategory());

            if (!Settings.TvCategoryLocalPath.IsNullOrWhiteSpace())
            {
                failures.AddIfNotNull(TestFolder(Settings.TvCategoryLocalPath, "TvCategoryLocalPath"));
            }
        }

        private ValidationFailure TestConnection()
        {
            try
            {
                _proxy.GetVersion(Settings);
            }
            catch (Exception ex)
            {
                _logger.ErrorException(ex.Message, ex);
                return new ValidationFailure("Host", "Unable to connect to SABnzbd");
            }

            return null;
        }

        private ValidationFailure TestAuthentication()
        {
            try
            {
                _proxy.GetConfig(Settings);
            }
            catch (Exception ex)
            {
                if (ex.Message.ContainsIgnoreCase("API Key Incorrect"))
                {
                    return new ValidationFailure("APIKey", "API Key Incorrect");
                }
                if (ex.Message.ContainsIgnoreCase("API Key Required"))
                {
                    return new ValidationFailure("APIKey", "API Key Required");
                }
                throw;
            }

            return null;
        }

        private ValidationFailure TestCategory()
        {
            var config = this._proxy.GetConfig(Settings);
            var category = GetCategories(config).FirstOrDefault((SabnzbdCategory v) => v.Name == Settings.TvCategory);

            if (category != null)
            {
                if (category.Dir.EndsWith("*"))
                {
                    return new NzbDroneValidationFailure("TvCategory", "Enable Job folders")
                    {
                        InfoLink = String.Format("http://{0}:{1}/sabnzbd/config/categories/", Settings.Host, Settings.Port),
                        DetailedDescription = "NzbDrone prefers each download to have a separate folder. With * appended to the Folder/Path Sabnzbd will not create these job folders. Go to Sabnzbd to fix it."
                    };
                }
            }
            else
            {
                if (!Settings.TvCategory.IsNullOrWhiteSpace())
                {
                    return new NzbDroneValidationFailure("TvCategory", "Category does not exist")
                    {
                        InfoLink = String.Format("http://{0}:{1}/sabnzbd/config/categories/", Settings.Host, Settings.Port),
                        DetailedDescription = "The Category your entered doesn't exist in Sabnzbd. Go to Sabnzbd to create it."
                    };
                }
            }

            if (config.Misc.enable_tv_sorting)
            {
                if (!config.Misc.tv_categories.Any<string>() ||
                    config.Misc.tv_categories.Contains(Settings.TvCategory) ||
                    (Settings.TvCategory.IsNullOrWhiteSpace() && config.Misc.tv_categories.Contains("Default")))
                {
                    return new NzbDroneValidationFailure("TvCategory", "Disable TV Sorting")
                    {
                        InfoLink = String.Format("http://{0}:{1}/sabnzbd/config/sorting/", Settings.Host, Settings.Port),
                        DetailedDescription = "You must disable Sabnzbd TV Sorting for the category NzbDrone uses to prevent import issues. Go to Sabnzbd to fix it."
                    };
                }
            }

            return null;
        }
    }
}