using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MESharp.API;
using MESharp.Commands;

namespace MESharp.ViewModels
{
    public sealed class DungeonMapOption
    {
        public string Title { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;

        public WikiDungeonMapDefinition ToDefinition() => new(Title, DisplayName, Category);
        public override string ToString() => DisplayName;
    }

    public sealed class DungeonPreviewDisplay
    {
        public string Kind { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public string Confidence { get; init; } = string.Empty;
    }

    public sealed class DungeonImportViewModel : BaseViewModel, IActivatableViewModel
    {
        private DungeonMapOption? _selectedDungeon;
        private string _customMapTitle = string.Empty;
        private string _status = "Select a starter dungeon map, then fetch a preview from the RuneScape Wiki API.";
        private string _summary = "No preview loaded.";
        private string _log = string.Empty;
        private bool _isBusy;
        private bool _replaceExactIds = true;
        private bool _replaceMatchingAreas = true;
        private WikiDungeonGraphFragment? _currentFragment;

        public ObservableCollection<DungeonMapOption> DungeonOptions { get; } = new();
        public ObservableCollection<DungeonPreviewDisplay> PreviewItems { get; } = new();

        public DungeonMapOption? SelectedDungeon
        {
            get => _selectedDungeon;
            set
            {
                if (SetProperty(ref _selectedDungeon, value))
                    InvalidateCommands();
            }
        }

        public string CustomMapTitle
        {
            get => _customMapTitle;
            set
            {
                if (SetProperty(ref _customMapTitle, value))
                    InvalidateCommands();
            }
        }

        public string Status { get => _status; private set => SetProperty(ref _status, value); }
        public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
        public string Log { get => _log; private set => SetProperty(ref _log, value); }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                    InvalidateCommands();
            }
        }

        public bool ReplaceExactIds
        {
            get => _replaceExactIds;
            set => SetProperty(ref _replaceExactIds, value);
        }

        public bool ReplaceMatchingAreas
        {
            get => _replaceMatchingAreas;
            set => SetProperty(ref _replaceMatchingAreas, value);
        }

        public ICommand FetchPreviewCommand { get; }
        public ICommand ImportPreviewCommand { get; }
        public ICommand ClearPreviewCommand { get; }

        public DungeonImportViewModel()
        {
            foreach (var def in RuneScapeWikiDungeonImporter.InitialSlayerDungeons)
            {
                DungeonOptions.Add(new DungeonMapOption
                {
                    Title = def.Title,
                    DisplayName = def.DisplayName,
                    Category = def.Category
                });
            }

            SelectedDungeon = DungeonOptions.FirstOrDefault();
            FetchPreviewCommand = new RelayCommand(async _ => await FetchPreviewAsync().ConfigureAwait(true), _ => CanFetch());
            ImportPreviewCommand = new RelayCommand(_ => ImportPreview(), _ => !IsBusy && _currentFragment != null);
            ClearPreviewCommand = new RelayCommand(_ => ClearPreview(), _ => !IsBusy && _currentFragment != null);
        }

        public void OnActivated()
        {
            if (DungeonOptions.Count == 0)
                Status = "No starter dungeon maps are configured.";
        }

        public void OnDeactivated() { }

        private bool CanFetch()
            => !IsBusy && (SelectedDungeon != null || !string.IsNullOrWhiteSpace(CustomMapTitle));

        private WikiDungeonMapDefinition BuildDefinition()
        {
            var custom = CustomMapTitle.Trim();
            if (!string.IsNullOrWhiteSpace(custom))
            {
                var title = custom.StartsWith("Map:", StringComparison.OrdinalIgnoreCase) ? custom : "Map:" + custom;
                return new WikiDungeonMapDefinition(title, title[4..], "custom");
            }

            return SelectedDungeon?.ToDefinition() ??
                   new WikiDungeonMapDefinition("Map:Fremennik Slayer Dungeon", "Fremennik Slayer Dungeon");
        }

        private async Task FetchPreviewAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            PreviewItems.Clear();
            _currentFragment = null;

            var definition = BuildDefinition();
            Status = $"Fetching {definition.Title} from the RuneScape Wiki API...";
            Append(Status);

            try
            {
                var fragment = await RuneScapeWikiDungeonImporter.FetchAndBuildFragmentAsync(definition)
                    .ConfigureAwait(true);
                _currentFragment = fragment;
                Summary = fragment.Summary;
                foreach (var item in fragment.PreviewItems)
                {
                    PreviewItems.Add(new DungeonPreviewDisplay
                    {
                        Kind = item.Kind,
                        Title = item.Title,
                        Detail = item.Detail,
                        Confidence = item.Confidence
                    });
                }

                Status = $"Preview ready: {fragment.Summary}";
                Append(Status);
            }
            catch (Exception ex)
            {
                Status = $"Preview failed: {ex.Message}";
                Summary = "No preview loaded.";
                Append(Status);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ImportPreview()
        {
            if (_currentFragment == null) return;
            try
            {
                var result = RuneScapeWikiDungeonImporter.ImportFragment(
                    _currentFragment,
                    replaceExactIds: ReplaceExactIds,
                    replaceMatchingAreas: ReplaceMatchingAreas);
                Status = result.Succeeded
                    ? $"{result.Message} Matched existing areas: {result.MatchedExistingAreas}."
                    : $"Import failed: {result.Message}";
                Append(Status);
                if (result.Succeeded)
                    WebwalkGraph.ReloadGraph();
            }
            catch (Exception ex)
            {
                Status = $"Import failed: {ex.Message}";
                Append(Status);
            }
        }

        private void ClearPreview()
        {
            _currentFragment = null;
            PreviewItems.Clear();
            Summary = "No preview loaded.";
            Status = "Preview cleared.";
            Append(Status);
        }

        private void Append(string line)
        {
            var entry = $"[{DateTime.Now:HH:mm:ss}] {line}";
            Log = string.IsNullOrWhiteSpace(Log) ? entry : entry + Environment.NewLine + Log;
            const int maxChars = 6000;
            if (Log.Length > maxChars)
                Log = Log[..maxChars];
        }

        private static void InvalidateCommands()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null) dispatcher.BeginInvoke(CommandManager.InvalidateRequerySuggested);
            else CommandManager.InvalidateRequerySuggested();
        }
    }
}
