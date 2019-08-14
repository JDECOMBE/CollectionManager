﻿using App.Interfaces;
using App.Misc;
using CollectionManager.DataTypes;
using CollectionManager.Modules.CollectionsManager;
using CollectionManager.Modules.FileIO;
using CollectionManagerExtensionsDll.Enums;
using CollectionManagerExtensionsDll.Modules.CollectionListGenerator;
using CollectionManagerExtensionsDll.Modules.CollectionListGenerator.ListTypes;
using CollectionManagerExtensionsDll.Utils;
using Common;
using GuiComponents.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using CollectionManagerExtensionsDll.Modules.BeatmapExporter;

namespace App
{
    public class BeatmapListingActionsHandler
    {
        private readonly ICollectionEditor _collectionEditor;
        private readonly IUserDialogs _userDialogs;
        private readonly ILoginFormView _loginForm;
        private readonly OsuFileIo _osuFileIo;
        private readonly ListGenerator _listGenerator = new ListGenerator();
        private readonly Dictionary<BeatmapListingAction, Action<object>> _beatmapOperationHandlers;
        private readonly UserListGenerator UserUrlListGenerator = new UserListGenerator() { collectionBodyFormat = "{MapLink}" + UserListGenerator.NewLine };
        public BeatmapListingActionsHandler(ICollectionEditor collectionEditor, IUserDialogs userDialogs, ILoginFormView loginForm, OsuFileIo osuFileIo)
        {
            _collectionEditor = collectionEditor;
            _userDialogs = userDialogs;
            _loginForm = loginForm;
            _osuFileIo = osuFileIo;

            _beatmapOperationHandlers = new Dictionary<BeatmapListingAction, Action<object>>
            {
                {BeatmapListingAction.CopyBeatmapsAsText, CopyBeatmapsAsText },
                {BeatmapListingAction.CopyBeatmapsAsUrls, CopyBeatmapsAsUrls },
                {BeatmapListingAction.DeleteBeatmapsFromCollection, DeleteBeatmapsFromCollection },
                {BeatmapListingAction.DownloadBeatmapsManaged, DownloadBeatmapsManaged },
                {BeatmapListingAction.DownloadBeatmaps, DownloadBeatmaps },
                {BeatmapListingAction.OpenBeatmapPages, OpenBeatmapPages },
                {BeatmapListingAction.OpenBeatmapFolder, OpenBeatmapFolder },
                {BeatmapListingAction.PullWholeMapSet, PullWholeMapsets },
                {BeatmapListingAction.ExportBeatmaps, ExportBeatmaps }
            };
        }

        public void Bind(IBeatmapListingModel beatmapListingModel)
        {
            beatmapListingModel.BeatmapOperation += BeatmapListingModel_BeatmapOperation;
        }

        public void UnBind(IBeatmapListingModel beatmapListingModel)
        {
            beatmapListingModel.BeatmapOperation -= BeatmapListingModel_BeatmapOperation;
        }

        private void BeatmapListingModel_BeatmapOperation(object sender, BeatmapListingAction args)
        {
            _beatmapOperationHandlers[args](sender);
        }

        private async void ExportBeatmaps(object sender)
        {
            var model = (IBeatmapListingModel)sender;

            var destinationDirectory = _userDialogs.SelectDirectory("Select directory for exported beatmaps", true);

            if (string.IsNullOrEmpty(destinationDirectory))
                return;

            var exporter = new BeatmapExporter();
            var exportForm = GuiComponentsProvider.Instance.GetClassImplementing<IBeatmapExportForm>();
            var cancelationTokenSource = new CancellationTokenSource();
            exportForm.Show(cancelationTokenSource);
            try
            {
                await exporter.ExportBeatmaps(model.SelectedBeatmaps, destinationDirectory,
                    (metadataStatus, preparedFiles) =>
                    {
                        exportForm.TotalFiles = preparedFiles;
                        exportForm.MetadataStatus = metadataStatus;
                    },
                    (copyStatus, processedFiles) =>
                    {
                        exportForm.ProcessedFiles = processedFiles;
                        exportForm.CopyStatus = copyStatus;
                    }, cancelationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                _userDialogs.OkMessageBox("Export aborted", "Warning", MessageBoxType.Warning);
            }
        }

        private void PullWholeMapsets(object sender)
        {
            var model = (IBeatmapListingModel)sender;
            if (model.SelectedBeatmaps?.Count > 0)
            {
                var setBeatmaps = new Beatmaps();

                foreach (var selectedBeatmap in model.SelectedBeatmaps)
                {
                    IEnumerable<Beatmap> set;
                    if (selectedBeatmap.MapSetId <= 20)
                        set = Initalizer.LoadedBeatmaps.Where(b => b.Dir == selectedBeatmap.Dir);
                    else
                        set = Initalizer.LoadedBeatmaps.Where(b => b.MapSetId == selectedBeatmap.MapSetId);
                    setBeatmaps.AddRange(set);

                }
                Initalizer.CollectionEditor.EditCollection(
                    CollectionEditArgs.AddBeatmaps(model.CurrentCollection.Name, setBeatmaps)
                );
            }

        }
        private void DownloadBeatmapsManaged(object sender)
        {
            var model = (IBeatmapListingModel)sender;
            var manager = OsuDownloadManager.Instance;

            if (manager.AskUserForSaveDirectoryAndLogin(_userDialogs, _loginForm))
                OsuDownloadManager.Instance.DownloadBeatmaps(model.SelectedBeatmaps);
        }
        private void OpenBeatmapFolder(object sender)
        {
            var model = (IBeatmapListingModel)sender;
            if (model.SelectedBeatmap != null)
            {
                var location = ((BeatmapExtension)model.SelectedBeatmap).FullOsuFileLocation();
                Process.Start("explorer.exe", $"/select, \"{location}\"");
            }
        }
        private void DeleteBeatmapsFromCollection(object sender)
        {
            var model = (IBeatmapListingModel)sender;
            _collectionEditor.EditCollection(CollectionEditArgs.RemoveBeatmaps(model.CurrentCollection.Name, model.SelectedBeatmaps));
        }

        private void CopyBeatmapsAsUrls(object sender)
        {
            var dummyCollection = ((IBeatmapListingModel)sender).AddSelectedBeatmapsToCollection(new Collection(_osuFileIo.LoadedMaps));
            Helpers.SetClipboardText(_listGenerator.GetAllMapsList(new Collections() { dummyCollection }, UserUrlListGenerator));
        }

        private void CopyBeatmapsAsText(object sender)
        {
            var dummyCollection = ((IBeatmapListingModel)sender).AddSelectedBeatmapsToCollection(new Collection(_osuFileIo.LoadedMaps));
            Helpers.SetClipboardText(_listGenerator.GetAllMapsList(new Collections() { dummyCollection }, CollectionListSaveType.BeatmapList));
        }

        private void DownloadBeatmaps(object sender)
        {
            var model = (IBeatmapListingModel)sender;

            var mapIds = model.SelectedBeatmaps.GetUniqueMapSetIds();
            MassOpen(mapIds, @"https://osu.ppy.sh/d/{0}");
        }

        private void OpenBeatmapPages(object sender)
        {
            var model = (IBeatmapListingModel)sender;

            var mapIds = model.SelectedBeatmaps.GetUniqueMapSetIds();
            MassOpen(mapIds, @"https://osu.ppy.sh/s/{0}");
        }


        private void MassOpen(HashSet<int> dataSet, string urlFormat)
        {
            bool shouldContinue = true;
            if (dataSet.Count > 100)
            {
                shouldContinue = _userDialogs.YesNoMessageBox("You are going to open " + dataSet.Count +
                                             " map links at the same time in your default browser", "Are you sure?", MessageBoxType.Question);
            }
            if (shouldContinue)
            {
                foreach (var d in dataSet)
                {
                    OpenLink(string.Format(urlFormat, d));
                }
            }
        }
        private void OpenLink(string url)
        {
            Process.Start(url);

        }
    }
}