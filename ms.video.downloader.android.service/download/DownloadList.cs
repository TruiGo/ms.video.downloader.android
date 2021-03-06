﻿using System;
using System.Threading;

namespace ms.video.downloader.android.service.download
{
    public delegate void ListDownloadStatusEventHandler(Feed list, Feed feed, DownloadState downloadState, double percentage);
    public delegate void ListDownloadAvailableEventHandler(Feed list, YoutubeEntry feed, VideoInfo videoInfo, MediaType mediaType);

    public class DownloadList : Feed
    {
        public MediaType MediaType { get; set; }
        private bool _ignoreDownloaded;
        private readonly int _poolSize;

        public ListDownloadStatusEventHandler OnListDownloadStatusChange;
        public ListDownloadAvailableEventHandler OnListDownloadAvailable;

        public DownloadList(MediaType mediaType, ListDownloadStatusEventHandler onDownloadStatusChange = null, ListDownloadAvailableEventHandler onListDownloadAvailable = null, int poolSize = 3)
        {
            MediaType = mediaType;
            OnListDownloadStatusChange = onDownloadStatusChange;
            OnListDownloadAvailable = onListDownloadAvailable;
            _ignoreDownloaded = false;
            _poolSize = poolSize;
        }

        public void Download(bool ignoreDownloaded)
        {
            if (ExecutionStatus == ExecutionStatus.Deleted) {
                Delete();
                return;
            }

            var count = Entries.Count;
            if (count == 0) return;
            var firstEntry = Entries[0] as YoutubeEntry;
            if (firstEntry != null)
                if (count == 1)
                    Title = firstEntry.Title;
                else {
                    Title = firstEntry.ChannelName;
                    if (string.IsNullOrEmpty(Title)) Title = firstEntry.Title;
                }
            UpdateStatus(DownloadState.AllStart, null, 0.0);
            _ignoreDownloaded = ignoreDownloaded;
            foreach (YoutubeEntry item in Entries) {
                item.OnEntryDownloadStatusChange += OnDownloadStatusChanged;
                item.OnEntryDownloadAvailable += (feed, info, type) => {
                    if (OnListDownloadAvailable != null) OnListDownloadAvailable(this, feed, info, type);
                };
            }
            DownloadFirst();

        }

        private void OnDownloadStatusChanged(Feed feed, DownloadState downloadState, double percentage)
        {
            var finishedCount = 0;
            var downloadCount = 0;
            var average = 0.0;
            if (downloadState == DownloadState.Deleted) {
                var entry = feed as YoutubeEntry;
                if (entry != null) {
                    entry.OnEntryDownloadStatusChange = null;
                    Entries.Remove(entry);
                }
                return;
            }
            foreach (var en in Entries) {
                if (en.DownloadState == DownloadState.Ready || en.DownloadState == DownloadState.Error)
                    finishedCount++;
                if (
                    !(en.DownloadState == DownloadState.Ready || en.DownloadState == DownloadState.Error ||
                      en.DownloadState == DownloadState.Initialized))
                    downloadCount++;
                average += en.Percentage;
            }
            average = average/Entries.Count;

            if (OnListDownloadStatusChange != null) {
                DownloadState = downloadState;
                if (downloadState == DownloadState.DownloadProgressChanged) {
                    Percentage = average;
                }
                if (downloadCount == 0 && finishedCount == Entries.Count)
                    DownloadState = DownloadState.AllFinished;
                if (Entries.Count == 1 && downloadState == DownloadState.TitleChanged) {
                    Title = Entries[0].Title;
                }
                OnListDownloadStatusChange(this, feed, DownloadState, Percentage);
            }
            if (downloadCount != _poolSize)
                DownloadFirst();
        }

        private void UpdateStatus(DownloadState state, YoutubeEntry entry, double percentage)
        {
            DownloadState = state;
            Percentage = percentage;
            if (OnListDownloadStatusChange != null) OnListDownloadStatusChange(this, entry, DownloadState, Percentage);
        }

        private void DownloadFirst()
        {
            for (var i = 0; i < Entries.Count; i++) {
                var entry = Entries[i] as YoutubeEntry;
                if (entry == null || entry.DownloadState != DownloadState.Initialized || entry.DownloadState == DownloadState.Deleted) continue;
                ThreadPool.QueueUserWorkItem(o => entry.DownloadAsync(MediaType, _ignoreDownloaded) );
                break;
            }
        }

        public override void Delete()
        {
            base.Delete();
            foreach (YoutubeEntry youtubeEntry in Entries) 
                youtubeEntry.OnEntryDownloadStatusChange = null;
            Entries.Clear();
            UpdateStatus(DownloadState.Deleted, null, 0.0);
        }
    }
}