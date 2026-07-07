using MESharp.API;
using MESharp.Commands;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace MESharp.ViewModels
{
    /// <summary>One resolved cache action row shown in the tester (name + the exact dispatch values).</summary>
    public sealed class ResolvedActionRow
    {
        public int Index { get; init; }
        public string Name { get; init; } = string.Empty;
        public string ActionParamHex { get; init; } = string.Empty;
        public int Route { get; init; }
    }

    /// <summary>
    /// Live verification surface for the Layer-1 named verbs (ACTION_CONFIRMATION_CONTRACT.md). Pick an
    /// NPC/object id, see what its cache actions resolve to, fire a named verb (Attack/Talk/Trade/Open/Bank/
    /// Climb/Enter), and watch the *documented postcondition observables* (interacting/combat, Bank.IsOpen,
    /// tile/plane) flip on a refresh timer — the human confirms the verb's postcondition actually lands.
    /// </summary>
    public sealed class NamedVerbTesterViewModel : INotifyPropertyChanged, IActivatableViewModel, IDisposable
    {
        private readonly DispatcherTimer _timer;

        public NamedVerbTesterViewModel()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _timer.Tick += (_, _) => RefreshObservables();
            RefreshActionsCommand = new RelayCommand(_ => RefreshActions());
            FireCommand = new RelayCommand(p => Fire(p as string));
        }

        // --- target selection ---
        private bool _isNpc = true;
        public bool IsNpc
        {
            get => _isNpc;
            set { if (Set(ref _isNpc, value)) { OnPropertyChanged(nameof(IsObject)); RefreshActions(); } }
        }
        public bool IsObject { get => !_isNpc; set => IsNpc = !value; }

        private string _targetId = string.Empty;
        public string TargetId { get => _targetId; set { if (Set(ref _targetId, value)) RefreshActions(); } }

        public ObservableCollection<ResolvedActionRow> Actions { get; } = new();

        private string _resolveStatus = "Enter an NPC/object id.";
        public string ResolveStatus { get => _resolveStatus; set => Set(ref _resolveStatus, value); }

        private string _lastResult = "—";
        public string LastResult { get => _lastResult; set => Set(ref _lastResult, value); }

        // --- live postcondition observables (timer-refreshed) ---
        private string _obsInteract = "—";
        public string ObsInteract { get => _obsInteract; set => Set(ref _obsInteract, value); }
        private string _obsBank = "—";
        public string ObsBank { get => _obsBank; set => Set(ref _obsBank, value); }
        private string _obsTile = "—";
        public string ObsTile { get => _obsTile; set => Set(ref _obsTile, value); }

        public ICommand RefreshActionsCommand { get; }
        public ICommand FireCommand { get; }

        public void OnActivated() { _timer.Start(); RefreshObservables(); }
        public void OnDeactivated() => _timer.Stop();
        public void Dispose() { _timer.Stop(); }

        private int ParsedId => int.TryParse(TargetId, out var v) ? v : -1;

        private void RefreshActions()
        {
            Actions.Clear();
            var id = ParsedId;
            if (id <= 0) { ResolveStatus = "Enter a numeric id."; return; }
            try
            {
                var list = IsNpc ? Npcs.GetActions(id) : Objects.GetActions(id);
                if (list == null || list.Count == 0)
                {
                    ResolveStatus = $"No cache actions for {(IsNpc ? "NPC" : "Object")} {id} (is the cache loaded?).";
                    return;
                }
                foreach (var a in list)
                    Actions.Add(new ResolvedActionRow { Index = a.Index, Name = a.Name, ActionParamHex = a.ActionParamHex, Route = a.Route });
                ResolveStatus = $"{Actions.Count} cache action(s) for {(IsNpc ? "NPC" : "Object")} {id}.";
            }
            catch (Exception ex) { ResolveStatus = "Error reading cache: " + ex.Message; }
        }

        private void Fire(string? verb)
        {
            var id = ParsedId;
            if (id <= 0) { LastResult = "Enter a numeric id first."; return; }
            try
            {
                bool ok = verb switch
                {
                    "Attack" => Npcs.Attack(id),
                    "Talk"   => Npcs.Talk(id),
                    "Trade"  => Npcs.Trade(id),
                    "Open"   => Objects.Open(id),
                    "Bank"   => Objects.Bank(id),
                    "Climb"  => Objects.Climb(id),
                    "Enter"  => Objects.Enter(id),
                    _ => false
                };
                LastResult = $"{DateTime.Now:HH:mm:ss}  {verb}({id}) → dispatched={ok}" +
                             (ok ? "  · watch the observables below" : "  · action not resolved / not dispatched");
            }
            catch (Exception ex) { LastResult = $"{verb} threw: {ex.Message}"; }
        }

        private void RefreshObservables()
        {
            ObsInteract = Read(() =>
                $"InCombat={LocalPlayer.IsInCombat()}  ·  Interacting={LocalPlayer.IsInteracting()}  ·  " +
                $"With=\"{LocalPlayer.GetInteractingWith()}\" (id {LocalPlayer.GetInteractingWithId()})");
            ObsBank = Read(() => $"Bank.IsOpen={Bank.IsOpen}");
            ObsTile = Read(() =>
            {
                var (x, y, z) = LocalPlayer.GetTilePosition();
                return $"Tile=({x}, {y}, plane {z})  ·  Moving={LocalPlayer.IsMoving()}";
            });
        }

        private static string Read(Func<string> f)
        {
            try { return f(); } catch (Exception ex) { return "n/a (" + ex.Message + ")"; }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }
}
