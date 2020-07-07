﻿using M2BobPatcher.Downloaders;
using M2BobPatcher.ExceptionHandler;
using M2BobPatcher.FileSystem;
using M2BobPatcher.Resources.Configs;
using M2BobPatcher.Resources.TextResources;
using M2BobPatcher.UI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace M2BobPatcher.Engine {
    class PatcherEngine : IPatcherEngine {

        private static IFileSystemExplorer Explorer;
        private static IExceptionHandler ExceptionHandler;
        private static ConcurrentDictionary<string, FileMetadata> LocalMetadata;
        private static IDownloader Downloader;
        private static Dictionary<string, FileMetadata> ServerMetadata;
        private static UIComponents UI;
        private static string PatchDirectory;
        private static int LogicalProcessorsCount;
        private static double PipelineLength;

        public PatcherEngine(UIComponents ui) {
            UI = ui;
            Explorer = new FileSystemExplorer();
            LogicalProcessorsCount = Environment.ProcessorCount;
            ExceptionHandler = new Handler(this);
            Downloader = new HttpClientDownloader(UI.RegisterProgress);
        }

        void IPatcherEngine.Patch() {
            Tuple<Action, string>[] pipeline = {
                new Tuple<Action, string>(GenerateServerMetadata,
                PatcherEngineResources.PARSING_SERVER_METADATA),
                new Tuple<Action, string>(DownloadMissingContent,
                PatcherEngineResources.DOWNLOADING_MISSING_CONTENT),
                new Tuple<Action, string>(GenerateLocalMetadata,
                PatcherEngineResources.GENERATING_LOCAL_METADATA),
                new Tuple<Action, string>(DownloadOutdatedContent,
                PatcherEngineResources.DOWNLOADING_OUTDATED_CONTENT)
            };
            PipelineLength = pipeline.Length;
            Stopwatch sw = new Stopwatch();
            sw.Start();
            for (int i = 0; i < PipelineLength; i++) {
                UI.Log(string.Format(PatcherEngineResources.STEP, i + 1, PipelineLength) + pipeline[i].Item2, true);
                pipeline[i].Item1.Invoke();
                UI.RegisterProgress(Convert.ToInt32((i + 1) / PipelineLength * 100), false);
            }
            sw.Stop();
            Finish(sw.Elapsed);
        }

        private void DownloadOutdatedContent() {
            List<string> outdatedContent = CalculateOutdatedContent();
            int currentProgress = UI.GetProgressBarProgress();
            for (int i = 0; i < outdatedContent.Count; i++) {
                try {
                    Explorer.FetchFile(outdatedContent[i], PatchDirectory + outdatedContent[i], UI.Log, false, Downloader);
                }
                catch (Exception ex) {
                    ExceptionHandler.Handle(ex);
                }
                UI.RegisterProgress(Convert.ToInt32(currentProgress + (i + 1) / (float)outdatedContent.Count * (1 / PipelineLength * 100)), false);
            }
        }

        private List<string> CalculateMissingContent() {
            List<string> missingFiles = new List<string>();
            foreach (string file in ServerMetadata.Keys)
                if (!Explorer.FileExists(file))
                    missingFiles.Add(file);
            return missingFiles;
        }

        private List<string> CalculateOutdatedContent() {
            List<string> outdatedFiles = new List<string>();
            foreach (KeyValuePair<string, FileMetadata> entry in ServerMetadata)
                if (!entry.Value.Hash.Equals(LocalMetadata[entry.Key].Hash))
                    outdatedFiles.Add(entry.Key);
            return outdatedFiles;
        }

        private void DownloadMissingContent() {
            List<string> missingContent = CalculateMissingContent();
            int currentProgress = UI.GetProgressBarProgress();
            for (int i = 0; i < missingContent.Count; i++) {
                try {
                    Explorer.FetchFile(missingContent[i], PatchDirectory + missingContent[i], UI.Log, false, Downloader);
                }
                catch (Exception ex) {
                    ExceptionHandler.Handle(ex);
                }
                UI.RegisterProgress(Convert.ToInt32(currentProgress + (i + 1) / (float)missingContent.Count * (1 / PipelineLength * 100)), false);
            }
        }

        private string DownloadServerMetadataFile() {
            byte[] data = null;
            try {
                data = Downloader.DownloadData(EngineConfigs.M2BOB_PATCH_METADATA);
            }
            catch (Exception ex) {
                ExceptionHandler.Handle(ex);
            }
            return System.Text.Encoding.Default.GetString(data);
        }

        private void GenerateLocalMetadata() {
            try {
                LocalMetadata = Explorer.GenerateLocalMetadata(ServerMetadata.Keys.ToArray(), LogicalProcessorsCount / 2);
            } catch (Exception ex) {
                ExceptionHandler.Handle(ex);
            }
        }

        private void GenerateServerMetadata() {
            string serverMetadata = DownloadServerMetadataFile();
            string[] metadataByLine = serverMetadata.Trim().Split(new[] { "\n" }, StringSplitOptions.None);
            PatchDirectory = metadataByLine[0];
            int numberOfRemoteFiles = (metadataByLine.Length - 1) / 2;
            ServerMetadata = new Dictionary<string, FileMetadata>(numberOfRemoteFiles);
            for (int i = 1; i < metadataByLine.Length; i += 2)
                ServerMetadata[metadataByLine[i]] = new FileMetadata(metadataByLine[i], metadataByLine[i + 1]);
        }

        private void Finish(TimeSpan elapsed) {
            UI.Log(PatcherEngineResources.FINISHED, true);
            UI.Log(string.Format(PatcherEngineResources.ALL_FILES_ANALYZED, elapsed.ToString("hh\\:mm\\:ss")), false);
            UI.Toggle(true);
        }
    }
}