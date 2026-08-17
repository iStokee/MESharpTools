using System.Collections.ObjectModel;
using System.Windows.Input;
using MESharp.Commands;
using MESharp.Slayer;
using MESharp.ViewModels;

namespace MESharp;

public sealed class SlayerKnowledgeViewModel : BaseViewModel
{
    private readonly SlayerKnowledgeRepository _repository = new();
    private SlayerKnowledgePack _pack = new();
    private SlayerMasterDefinition? _selectedMaster;
    private MonsterLocation? _selectedLocation;
    private string _status = "Ready.";

    public SlayerKnowledgeViewModel()
    {
        RefreshCommand = new RelayCommand(_ => Refresh());
        PublishCommand = new RelayCommand(_ => Publish());
        Refresh();
    }

    public ObservableCollection<MonsterDefinition> Monsters { get; } = new();
    public ObservableCollection<SlayerAssignmentDefinition> Assignments { get; } = new();
    public ObservableCollection<SlayerMasterDefinition> Masters { get; } = new();
    public ObservableCollection<SlayerMasterAssignmentRule> SelectedMasterRules { get; } = new();
    public ObservableCollection<MonsterLocation> Locations { get; } = new();
    public ObservableCollection<KnowledgeProvenance> SelectedLocationEvidence { get; } = new();
    public ObservableCollection<SlayerMechanicDefinition> Mechanics { get; } = new();
    public ObservableCollection<AssignmentIdObservation> AssignmentObservations { get; } = new();
    public IReadOnlyList<KnowledgeVerificationState> VerificationOptions { get; } = Enum.GetValues<KnowledgeVerificationState>();
    public ICommand RefreshCommand { get; }
    public ICommand PublishCommand { get; }
    public string KnowledgePath => _repository.Paths.KnowledgePath;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public int MonsterCount => Monsters.Count;
    public int AssignmentCount => Assignments.Count;
    public int MasterCount => Masters.Count;
    public int LocationCount => Locations.Count;
    public int RunnableLocationCount => Locations.Count(l => l.IsRunnable);

    public SlayerMasterDefinition? SelectedMaster
    {
        get => _selectedMaster;
        set
        {
            if (!SetProperty(ref _selectedMaster, value)) return;
            SelectedMasterRules.Clear();
            foreach (var rule in value?.AssignmentRules ?? new List<SlayerMasterAssignmentRule>()) SelectedMasterRules.Add(rule);
        }
    }

    public MonsterLocation? SelectedLocation
    {
        get => _selectedLocation;
        set
        {
            if (!SetProperty(ref _selectedLocation, value)) return;
            Replace(SelectedLocationEvidence, value?.Evidence ?? new List<KnowledgeProvenance>());
        }
    }

    private void Refresh()
    {
        _pack = _repository.LoadKnowledge();
        Replace(Monsters, _pack.Monsters.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase));
        Replace(Assignments, _pack.Assignments.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase));
        Replace(Masters, _pack.Masters.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase));
        Replace(Locations, _pack.Locations.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase));
        Replace(Mechanics, _pack.Mechanics.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase));
        Replace(AssignmentObservations, _repository.LoadObservations().AssignmentBindings.OrderByDescending(o => o.ObservedAtUtc));
        SelectedMaster = Masters.FirstOrDefault();
        SelectedLocation = Locations.FirstOrDefault();
        Status = $"Loaded {_pack.PackVersion}: {Assignments.Count} assignments, {Locations.Count} locations.";
        RaiseCounts();
    }

    private void Publish()
    {
        _pack.Monsters = Monsters.ToList();
        _pack.Assignments = Assignments.ToList();
        _pack.Masters = Masters.ToList();
        _pack.Locations = Locations.ToList();
        _pack.Mechanics = Mechanics.ToList();
        _pack.PublishedAtUtc = DateTime.UtcNow;
        _pack.PackVersion = DateTime.UtcNow.ToString("yyyyMMdd.HHmmss");
        _repository.Publish(SlayerKnowledgeNormalizer.Normalize(_pack));
        Status = $"Published {_pack.PackVersion} to {KnowledgePath}.";
        RaiseCounts();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    private void RaiseCounts()
    {
        foreach (var name in new[] { nameof(MonsterCount), nameof(AssignmentCount), nameof(MasterCount), nameof(LocationCount), nameof(RunnableLocationCount) })
            RaisePropertyChanged(name);
    }
}
