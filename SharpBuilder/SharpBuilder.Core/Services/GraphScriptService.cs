using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Core.Services;

/// <summary>
/// Handles persistence and seeding for SharpBuilder automation graphs.
/// Scripts are stored as JSON files so they can be shared easily.
/// </summary>
public class GraphScriptService
{
	private readonly string _scriptsDirectory;
	private readonly NodeCatalogService _catalogService;

	public GraphScriptService()
		: this(new NodeCatalogService())
	{
	}

	public GraphScriptService(NodeCatalogService catalogService)
	{
		_catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
		var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		_scriptsDirectory = Path.Combine(userProfile, "MemoryError", "CSharp_scripts", "SharpBuilder");
	}

	public string ScriptsDirectory => _scriptsDirectory;

	/// <summary>
	/// Saves a script to disk (using the provided path or a sanitized name in the default folder).
	/// </summary>
	public async Task<string> SaveAsync(GraphModel script, string? path = null, CancellationToken cancellationToken = default)
	{
		if (script == null) throw new ArgumentNullException(nameof(script));

		Directory.CreateDirectory(_scriptsDirectory);

		var fileName = path ?? Path.Combine(_scriptsDirectory, $"{SanitizeFileName(script.Name)}.builder.json");
		script.UpdatedAt = DateTime.UtcNow;

		var json = JsonConvert.SerializeObject(script, Formatting.Indented);

		// Write-then-move so a crash mid-save can't leave a truncated graph behind.
		var tempFile = fileName + ".tmp";
		await File.WriteAllTextAsync(tempFile, json, cancellationToken);
		File.Move(tempFile, fileName, overwrite: true);

		return fileName;
	}

	/// <summary>
	/// Loads a script from disk, returning null if parsing fails.
	/// </summary>
	public async Task<GraphModel?> LoadAsync(string path, CancellationToken cancellationToken = default)
	{
		var (model, _) = await TryLoadAsync(path, cancellationToken);
		return model;
	}

	/// <summary>
	/// Loads a script from disk. On failure the model is null and Error describes why,
	/// so callers can tell the user more than "could not load".
	/// </summary>
	public async Task<(GraphModel? Model, string? Error)> TryLoadAsync(string path, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(path))
			return (null, "No file path was provided.");
		if (!File.Exists(path))
			return (null, $"File not found: {path}");

		try
		{
			var json = await File.ReadAllTextAsync(path, cancellationToken);
			var model = JsonConvert.DeserializeObject<GraphModel>(json);
			if (model == null)
				return (null, "File parsed as empty JSON (null).");

			model.Nodes ??= new ObservableCollection<NodeModel>();

			foreach (var node in model.Nodes)
			{
				node.Transitions ??= new ObservableCollection<TransitionModel>();
				node.Parameters ??= new ObservableCollection<NodeParameterValue>();
			}

			if (model.SchemaVersion <= 1)
			{
				MigrateLegacyParameters(model);
			}

			foreach (var node in model.Nodes)
			{
				EnsureNodeDefinition(node);
				RefreshDriftedOffsets(node);
			}

			model.SchemaVersion = 2;

			return (model, null);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			return (null, ex.Message);
		}
	}

	/// <summary>
	/// Enumerates saved scripts from the default folder.
	/// </summary>
	public async Task<IReadOnlyList<GraphModel>> LoadAllAsync(CancellationToken cancellationToken = default)
	{
		var results = new List<GraphModel>();

		if (!Directory.Exists(_scriptsDirectory))
			return results;

		foreach (var file in Directory.EnumerateFiles(_scriptsDirectory, "*.builder.json", SearchOption.TopDirectoryOnly))
		{
			var model = await LoadAsync(file, cancellationToken);
			if (model != null)
			{
				results.Add(model);
			}
		}

		return results;
	}

	public GraphModel CreateNew(string name = "New Machine")
	{
		var startDefinition = _catalogService.GetDefaultDefinitionForType(NodeType.Start);
		var node = new NodeModel
		{
			Title = startDefinition.Title,
			Type = NodeType.Start,
			Description = startDefinition.ShortDescription,
			DefinitionId = startDefinition.Id,
			DefinitionTitle = startDefinition.Title,
			DwellMilliseconds = 200
		};
		EnsureNodeParameters(node, startDefinition);

		return new GraphModel
		{
			Name = name,
			Description = "Blank machine",
			Author = Environment.UserName,
			StartNodeId = node.Id,
			SchemaVersion = 2,
			Nodes = new System.Collections.ObjectModel.ObservableCollection<NodeModel>
			{
				node
			}
		};
	}

	/// <summary>
	/// Builds a shareable "power fishing" machine with the states described in the feature request.
	/// </summary>
	public GraphModel CreatePowerFishingTemplate()
	{
		// Live game check: publishes the "inventoryFull" signal the edges below read.
		var startDefinition = _catalogService.GetDefinition("conditions.inventoryFull")!;
		var start = new NodeModel
		{
			Title = "Check inventory",
			Description = "Live check of Inventory.IsFull; publishes the 'inventoryFull' signal.",
			Type = NodeType.Condition,
			DefinitionId = startDefinition.Id,
			DefinitionTitle = startDefinition.Title,
			X = 120,
			Y = 80,
			ActionText = "Inventory.IsFull",
			DwellMilliseconds = 150
		};
		EnsureNodeParameters(start, startDefinition);
		SetParameterBool(start, "expected", true);

		// Real drop action: quantity=All keeps dropping until no listed item remains,
		// so a single node clears the bag. Edit the item list for your catch.
		var dropDefinition = _catalogService.GetDefinition("inventory.drop")!;
		var dropInventory = new NodeModel
		{
			Title = "Drop the catch",
			Description = "Drops every matching item (quantity = All) before moving on. Edit the item list for your catch.",
			Type = NodeType.Action,
			DefinitionId = dropDefinition.Id,
			DefinitionTitle = dropDefinition.Title,
			X = 120,
			Y = 260,
			ActionText = "Inventory.Drop × All",
			DwellMilliseconds = 400
		};
		EnsureNodeParameters(dropInventory, dropDefinition);
		SetParameterValue(dropInventory, "items", "Raw shrimps, Raw anchovies");
		SetParameterValue(dropInventory, "quantity", "All");

		// Fishing spots are NPCs in RS3, so the scan must use the NPC query node.
		var findSpotDefinition = _catalogService.GetDefinition("npcs.find")!;
		var lookForSpot = new NodeModel
		{
			Title = "Look for spot",
			Description = "Scan nearby area for a fishing spot.",
			Type = NodeType.Condition,
			DefinitionId = findSpotDefinition.Id,
			DefinitionTitle = findSpotDefinition.Title,
			X = 420,
			Y = 80,
			ActionText = "Scan environment",
			DwellMilliseconds = 250
		};
		EnsureNodeParameters(lookForSpot, findSpotDefinition);
		SetParameterValue(lookForSpot, "name", "Fishing spot");
		SetParameterValue(lookForSpot, "maxDistance", "12");
		SetParameterValue(lookForSpot, "signal", "hasNearbySpot");
		SetParameterBool(lookForSpot, "expected", true);

		var walkDefinition = _catalogService.GetDefinition("traversal.walk")!;
		var moveToSpot = new NodeModel
		{
			Title = "Move to spot",
			Description = "User-provided pathing to the latest known spot.",
			Type = NodeType.Action,
			DefinitionId = walkDefinition.Id,
			DefinitionTitle = walkDefinition.Title,
			X = 420,
			Y = 260,
			ActionText = "Walk toward configured fishing tile",
			DwellMilliseconds = 450
		};
		EnsureNodeParameters(moveToSpot, walkDefinition);
		SetParameterValue(moveToSpot, "target", "3220,3150");

		var interactDefinition = _catalogService.GetDefinition("npcs.interact")!;
		var fish = new NodeModel
		{
			Title = "Fish",
			Description = "Interact with the spot once, then let the wait/check loop handle the fishing cycle.",
			Type = NodeType.Action,
			DefinitionId = interactDefinition.Id,
			DefinitionTitle = interactDefinition.Title,
			X = 700,
			Y = 80,
			ActionText = "Cast line / interact",
			DwellMilliseconds = 600
		};
		EnsureNodeParameters(fish, interactDefinition);
		SetParameterValue(fish, "target", "Fishing spot");
		SetParameterValue(fish, "actionIndex", "60");
		SetParameterValue(fish, "offset", $"InteractNPC_route = {MESharp.API.Npcs.InteractNPC_route}");
		SetParameterValue(fish, "maxDistance", "50");

		var waitDefinition = _catalogService.GetDefinition("traversal.waitRange")!;
		var waitWhileFishing = new NodeModel
		{
			Title = "Wait while fishing",
			Description = "If fishing is already in progress and the inventory is not full, do nothing for a random few seconds instead of clicking the spot every cycle.",
			Type = NodeType.Action,
			DefinitionId = waitDefinition.Id,
			DefinitionTitle = waitDefinition.Title,
			X = 700,
			Y = 240,
			ActionText = "Do nothing; wait 6000-8000ms before checking inventory again",
			DwellMilliseconds = 150
		};
		EnsureNodeParameters(waitWhileFishing, waitDefinition);
		SetParameterValue(waitWhileFishing, "minDelayMs", "6000");
		SetParameterValue(waitWhileFishing, "maxDelayMs", "8000");

		var nodes = new[]
		{
			start,
			dropInventory,
			lookForSpot,
			moveToSpot,
			fish,
			waitWhileFishing
		};

		var transitions = new[]
		{
			new TransitionModel
			{
				FromNodeId = start.Id,
				ToNodeId = dropInventory.Id,
				Label = "Inventory full",
				ConditionKey = "inventoryFull",
				ExpectedValue = true
			},
			new TransitionModel
			{
				FromNodeId = start.Id,
				ToNodeId = lookForSpot.Id,
				Label = "Space available",
				IsFallback = true
			},
			new TransitionModel
			{
				FromNodeId = dropInventory.Id,
				ToNodeId = lookForSpot.Id,
				Label = "Bag cleared",
				IsFallback = true
			},
			new TransitionModel
			{
				FromNodeId = lookForSpot.Id,
				ToNodeId = fish.Id,
				Label = "Spot nearby",
				ConditionKey = "hasNearbySpot",
				ExpectedValue = true
			},
			new TransitionModel
			{
				FromNodeId = lookForSpot.Id,
				ToNodeId = moveToSpot.Id,
				Label = "No spot found",
				IsFallback = true
			},
			new TransitionModel
			{
				FromNodeId = moveToSpot.Id,
				ToNodeId = lookForSpot.Id,
				Label = "Re-scan",
				IsFallback = true
			},
			new TransitionModel
			{
				FromNodeId = fish.Id,
				ToNodeId = waitWhileFishing.Id,
				Label = "Fishing started",
				IsFallback = true
			},
			// Always route back through the live inventory check so the loop reacts to a
			// filling bag; a self-loop on the interact node would spam-click the spot.
			new TransitionModel
			{
				FromNodeId = waitWhileFishing.Id,
				ToNodeId = start.Id,
				Label = "Re-check bag after waiting",
				IsFallback = true
			}
		};

		foreach (var transition in transitions)
		{
			var fromNode = nodes.First(n => n.Id == transition.FromNodeId);
			fromNode.Transitions.Add(transition);
		}

		return new GraphModel
		{
			Name = "Power fishing (template)",
			Description = "Minimal graph to power fish: clear bag, find a spot, move, and fish in a loop.",
			Author = "SharpBuilder",
			StartNodeId = start.Id,
			Nodes = new System.Collections.ObjectModel.ObservableCollection<NodeModel>(nodes),
			SchemaVersion = 2,
			UpdatedAt = DateTime.UtcNow
		};
	}

	/// <summary>
	/// Area-agnostic bank + portable-crafter loop for green dragonhide shields:
	/// bank preset, verify supplies, open Make-X from a nearby portable or leather keybind, craft,
	/// high-alch by keybind while waiting on animation, then repeat.
	/// </summary>
	public GraphModel CreateGreenDhideShieldCraftAlchTemplate()
	{
		var shieldCheck = MakeNode(_catalogService.GetDefinition("inventory.count")!,
			"Have shields?", NodeType.Condition, 120, 80, dwell: 100,
			actionText: "Resume by alching any green dragonhide shields already in inventory");
		SetParameterValue(shieldCheck, "id", "25794");
		SetParameterValue(shieldCheck, "name", "Green dragonhide shield");
		SetParameterValue(shieldCheck, "min", "1");

		var openBank = MakeNode(_catalogService.GetDefinition("bank.open")!,
			"Open nearby bank", NodeType.Action, 120, 260, dwell: 250,
			actionText: "Bank.Open() against nearest bank NPC/object");

		var deposit = MakeNode(_catalogService.GetDefinition("bank.depositAll")!,
			"Clear inventory", NodeType.Action, 120, 410, dwell: 250,
			actionText: "Deposit all before loading preset");

		var preset = MakeNode(_catalogService.GetDefinition("bank.loadPreset")!,
			"Load preset", NodeType.Action, 120, 560, dwell: 250,
			actionText: "Press bank preset keybind once bank is open");
		SetParameterValue(preset, "method", "Keybind");
		SetParameterValue(preset, "keybind", "1");
		SetParameterValue(preset, "preset", "1");
		SetParameterValue(preset, "waitMs", "1200");

		var closeBank = MakeNode(_catalogService.GetDefinition("bank.close")!,
			"Close bank", NodeType.Action, 120, 710, dwell: 500,
			actionText: "Close before crafting");

		var leatherCheck = MakeNode(_catalogService.GetDefinition("inventory.count")!,
			"Have leather?", NodeType.Condition, 440, 80, dwell: 100,
			actionText: "Resume if 26 green dragon leather are already in inventory");
		SetParameterValue(leatherCheck, "id", "1745");
		SetParameterValue(leatherCheck, "name", "Green dragon leather");
		SetParameterValue(leatherCheck, "min", "26");

		var runeCheck = MakeNode(_catalogService.GetDefinition("inventory.count")!,
			"Have natures?", NodeType.Condition, 440, 230, dwell: 100,
			actionText: "Resume if 13 nature runes are already in inventory");
		SetParameterValue(runeCheck, "id", "561");
		SetParameterValue(runeCheck, "name", "Nature rune");
		SetParameterValue(runeCheck, "min", "13");

		var presetLeatherCheck = MakeNode(_catalogService.GetDefinition("inventory.count")!,
			"Preset leather?", NodeType.Condition, 440, 500, dwell: 100,
			actionText: "Verify the loaded preset supplied 26 green dragon leather");
		SetParameterValue(presetLeatherCheck, "id", "1745");
		SetParameterValue(presetLeatherCheck, "name", "Green dragon leather");
		SetParameterValue(presetLeatherCheck, "min", "26");

		var presetRuneCheck = MakeNode(_catalogService.GetDefinition("inventory.count")!,
			"Preset natures?", NodeType.Condition, 440, 650, dwell: 100,
			actionText: "Verify the loaded preset supplied 13 nature runes");
		SetParameterValue(presetRuneCheck, "id", "561");
		SetParameterValue(presetRuneCheck, "name", "Nature rune");
		SetParameterValue(presetRuneCheck, "min", "13");

		var stopMissing = MakeNode(_catalogService.GetDefinition(NodeCatalogDefaults.TerminalId)!,
			"Stop: missing supplies", NodeType.Terminal, 440, 820, dwell: 100,
			actionText: "Preset did not provide required materials/runes");

		var findPortable = MakeNode(_catalogService.GetDefinition("objects.find")!,
			"Find portable crafter", NodeType.Condition, 760, 80, dwell: 150,
			actionText: "Scan local area for a portable crafter");
		SetParameterValue(findPortable, "name", "Portable crafter");
		SetParameterValue(findPortable, "maxDistance", "12");
		SetParameterValue(findPortable, "signal", "hasPortableCrafter");
		SetParameterBool(findPortable, "expected", true);

		var clickPortable = MakeNode(_catalogService.GetDefinition("objects.interact")!,
			"Use portable crafter", NodeType.Action, 760, 250, dwell: 650,
			actionText: "Click nearby portable crafter to open production");
		SetParameterValue(clickPortable, "name", "Portable crafter");
		SetParameterValue(clickPortable, "actionIndex", "58");
		SetParameterValue(clickPortable, "maxDistance", "12");
		SetParameterBool(clickPortable, "valid", false);
		SetParameterValue(clickPortable, "option", "Craft");

		var leatherKeybind = MakeNode(_catalogService.GetDefinition("keyboard.send")!,
			"Dragon leather keybind", NodeType.Action, 760, 430, dwell: 650,
			actionText: "Fallback if no portable is nearby");
		SetParameterValue(leatherKeybind, "keys", "F6");
		SetParameterValue(leatherKeybind, "delayMs", "650");

		var makeShield = MakeNode(_catalogService.GetDefinition("makex.makeItem")!,
			"Make shields", NodeType.Action, 1080, 170, dwell: 1200,
			actionText: "Craft green dragonhide shields (already-selected product, preset amount)");
		SetParameterValue(makeShield, "slot", "");
		SetParameterValue(makeShield, "category", "");
		SetParameterBool(makeShield, "waitComplete", true);

		var alch = MakeNode(_catalogService.GetDefinition("inventory.alchAll")!,
			"Alch shields", NodeType.Action, 1080, 360, dwell: 250,
			actionText: "Press high-alch keybind and wait through animation until shields are gone");
		SetParameterValue(alch, "items", "Green dragonhide shield, 25794");
		SetParameterValue(alch, "keybind", "E");
		SetParameterValue(alch, "targetMode", "KeybindThenItem");
		SetParameterValue(alch, "quantity", "All");
		SetParameterBool(alch, "requireAlchable", true);
		SetParameterValue(alch, "targetDelayMs", "1000");
		SetParameterValue(alch, "recastMode", "ItemDisappears");
		SetParameterValue(alch, "disappearTimeoutMs", "3500");
		SetParameterValue(alch, "postTargetDelayMs", "2500");
		SetParameterValue(alch, "startTimeoutMs", "3000");
		SetParameterValue(alch, "finishTimeoutMs", "5000");
		SetParameterValue(alch, "betweenCastsMs", "250");
		SetParameterValue(alch, "inventoryRoot", "0");
		SetParameterValue(alch, "itemAction", "110");
		SetParameterValue(alch, "itemOffset", $"GeneralInterface_route1 = {MESharp.API.Objects.Offsets.GeneralInterfaceRoute1}");

		var nodes = new[]
		{
			shieldCheck,
			openBank,
			deposit,
			preset,
			closeBank,
			leatherCheck,
			runeCheck,
			presetLeatherCheck,
			presetRuneCheck,
			stopMissing,
			findPortable,
			clickPortable,
			leatherKeybind,
			makeShield,
			alch
		};

		Wire(nodes, shieldCheck.Id, alch.Id, "Shields ready", trigger: TransitionTrigger.OnSuccess);
		Wire(nodes, shieldCheck.Id, leatherCheck.Id, "No shields; inspect supplies", trigger: TransitionTrigger.OnFail);
		Wire(nodes, leatherCheck.Id, runeCheck.Id, "Leather ready", trigger: TransitionTrigger.OnSuccess);
		Wire(nodes, leatherCheck.Id, openBank.Id, "Need supplies", trigger: TransitionTrigger.OnFail);
		Wire(nodes, runeCheck.Id, findPortable.Id, "Runes ready", trigger: TransitionTrigger.OnSuccess);
		Wire(nodes, runeCheck.Id, openBank.Id, "Need natures", trigger: TransitionTrigger.OnFail);
		Wire(nodes, openBank.Id, deposit.Id, "Bank open", fallback: true);
		Wire(nodes, deposit.Id, preset.Id, "Inventory clear", fallback: true);
		Wire(nodes, preset.Id, closeBank.Id, "Preset loaded", trigger: TransitionTrigger.OnSuccess);
		Wire(nodes, preset.Id, stopMissing.Id, "Preset failed", trigger: TransitionTrigger.OnFail);
		Wire(nodes, closeBank.Id, presetLeatherCheck.Id, "Check preset supplies", fallback: true);
		Wire(nodes, presetLeatherCheck.Id, presetRuneCheck.Id, "Leather ready", trigger: TransitionTrigger.OnSuccess);
		Wire(nodes, presetLeatherCheck.Id, stopMissing.Id, "No leather", trigger: TransitionTrigger.OnFail);
		Wire(nodes, presetRuneCheck.Id, findPortable.Id, "Runes ready", trigger: TransitionTrigger.OnSuccess);
		Wire(nodes, presetRuneCheck.Id, stopMissing.Id, "No natures", trigger: TransitionTrigger.OnFail);
		Wire(nodes, findPortable.Id, clickPortable.Id, "Portable found", conditionKey: "hasPortableCrafter", expected: true);
		Wire(nodes, findPortable.Id, leatherKeybind.Id, "No portable; use keybind", fallback: true);
		Wire(nodes, clickPortable.Id, makeShield.Id, "Make-X opened", trigger: TransitionTrigger.OnSuccess);
		Wire(nodes, clickPortable.Id, leatherKeybind.Id, "Portable click failed", trigger: TransitionTrigger.OnFail);
		Wire(nodes, leatherKeybind.Id, makeShield.Id, "Make-X opened", fallback: true);
		Wire(nodes, makeShield.Id, alch.Id, "Crafted", trigger: TransitionTrigger.OnSuccess);
		Wire(nodes, makeShield.Id, stopMissing.Id, "Craft failed", trigger: TransitionTrigger.OnFail);
		Wire(nodes, alch.Id, openBank.Id, "Repeat", trigger: TransitionTrigger.OnSuccess);
		Wire(nodes, alch.Id, stopMissing.Id, "Alch failed/stalled", trigger: TransitionTrigger.OnFail);

		return new GraphModel
		{
			Name = "Green dhide shield craft-alch",
			Description = "Area-agnostic bank/portable crafter loop for crafting and high-alching green dragonhide shields.",
			Author = "SharpBuilder",
			StartNodeId = shieldCheck.Id,
			Nodes = new ObservableCollection<NodeModel>(nodes),
			SchemaVersion = 2,
			UpdatedAt = DateTime.UtcNow
		};
	}

	/// <summary>
	/// Builds the pair of graphs used by the "Load demo" action in the multi-canvas workspace.
	/// The use case: one character (a single game session) driven by two independent routines at
	/// once — a primary skilling loop plus a background anti-idle heartbeat. Both graphs use only
	/// timing/flow nodes (no game API) so they animate live in Studio without an injected client,
	/// which makes them a self-contained demonstration of running and switching between canvases.
	/// </summary>
	public IReadOnlyList<GraphModel> CreateMultiCanvasDemo()
	{
		return new[]
		{
			CreateWoodcuttingDemo(),
			CreateAntiIdleDemo()
		};
	}

	/// <summary>
	/// Primary routine: chop → check bag → (loop / clear) — the foreground activity for the session.
	/// Offline the "invFull" signal never flips, so it loops on the chopping branch and animates.
	/// </summary>
	public GraphModel CreateWoodcuttingDemo()
	{
		var start = MakeNode(_catalogService.GetDefaultDefinitionForType(NodeType.Start),
			"Start", NodeType.Start, 120, 60, dwell: 150);

		var chop = MakeNode(_catalogService.GetDefinition("traversal.waitRange")!,
			"Chop tree", NodeType.Action, 120, 200, dwell: 150,
			actionText: "Swing the axe for a random beat");
		SetParameterValue(chop, "minDelayMs", "700");
		SetParameterValue(chop, "maxDelayMs", "1400");

		var full = MakeNode(_catalogService.GetDefinition(NodeCatalogDefaults.BooleanConditionId)!,
			"Inventory full?", NodeType.Condition, 120, 360, dwell: 120,
			actionText: "Reads the 'invFull' signal");
		SetParameterValue(full, "signal", "invFull");
		SetParameterBool(full, "expected", true);

		var drop = MakeNode(_catalogService.GetDefinition(NodeCatalogDefaults.GenericActionId)!,
			"Clear logs", NodeType.Action, 420, 360, dwell: 500,
			actionText: "Drop / bank the logs");

		var reset = MakeNode(_catalogService.GetDefinition("actions.setSignal")!,
			"Mark bag empty", NodeType.Action, 420, 200, dwell: 150,
			actionText: "invFull = false");
		SetParameterValue(reset, "signal", "invFull");
		SetParameterBool(reset, "value", false);

		var nodes = new[] { start, chop, full, drop, reset };
		Wire(nodes, start.Id, chop.Id, "Begin", fallback: true);
		Wire(nodes, chop.Id, full.Id, "Chopped", fallback: true);
		Wire(nodes, full.Id, drop.Id, "Bag full", conditionKey: "invFull", expected: true);
		Wire(nodes, full.Id, chop.Id, "Keep chopping", fallback: true);
		Wire(nodes, drop.Id, reset.Id, "Cleared", fallback: true);
		Wire(nodes, reset.Id, chop.Id, "Back to it", fallback: true);

		return new GraphModel
		{
			Name = "Woodcutting loop (demo)",
			Description = "Foreground routine: chop in a loop and clear the bag when full. Runs offline as a visual demo.",
			Author = "SharpBuilder",
			StartNodeId = start.Id,
			Nodes = new ObservableCollection<NodeModel>(nodes),
			SchemaVersion = 2,
			UpdatedAt = DateTime.UtcNow
		};
	}

	/// <summary>
	/// Background routine: a slow heartbeat that nudges the camera so the same session never idles
	/// out while the foreground graph works. Demonstrates a second canvas running concurrently.
	/// </summary>
	public GraphModel CreateAntiIdleDemo()
	{
		var start = MakeNode(_catalogService.GetDefaultDefinitionForType(NodeType.Start),
			"Start", NodeType.Start, 120, 60, dwell: 150);

		var wait = MakeNode(_catalogService.GetDefinition("traversal.waitRange")!,
			"Idle wait", NodeType.Action, 120, 200, dwell: 150,
			actionText: "Wait a few seconds between nudges");
		SetParameterValue(wait, "minDelayMs", "2000");
		SetParameterValue(wait, "maxDelayMs", "4000");

		var nudge = MakeNode(_catalogService.GetDefinition(NodeCatalogDefaults.GenericActionId)!,
			"Nudge camera", NodeType.Action, 120, 360, dwell: 400,
			actionText: "Rotate camera to defeat the logout timer");

		var nodes = new[] { start, wait, nudge };
		Wire(nodes, start.Id, wait.Id, "Begin", fallback: true);
		Wire(nodes, wait.Id, nudge.Id, "Tick", fallback: true);
		Wire(nodes, nudge.Id, wait.Id, "Loop", fallback: true);

		return new GraphModel
		{
			Name = "Anti-idle heartbeat (demo)",
			Description = "Background routine for the same session: nudges the camera on a slow loop so it never times out.",
			Author = "SharpBuilder",
			StartNodeId = start.Id,
			Nodes = new ObservableCollection<NodeModel>(nodes),
			SchemaVersion = 2,
			UpdatedAt = DateTime.UtcNow
		};
	}

	private NodeModel MakeNode(NodeDefinition definition, string title, NodeType type, double x, double y,
		int dwell, string? actionText = null)
	{
		var node = new NodeModel
		{
			Title = title,
			Type = type,
			Description = definition.ShortDescription,
			DefinitionId = definition.Id,
			DefinitionTitle = definition.Title,
			X = x,
			Y = y,
			DwellMilliseconds = dwell,
			ActionText = actionText ?? string.Empty
		};
		EnsureNodeParameters(node, definition);
		return node;
	}

	private static void Wire(IReadOnlyList<NodeModel> nodes, Guid fromId, Guid toId, string label,
		string? conditionKey = null, bool expected = true, bool fallback = false, TransitionTrigger trigger = TransitionTrigger.Any)
	{
		var transition = new TransitionModel
		{
			FromNodeId = fromId,
			ToNodeId = toId,
			Label = label,
			IsFallback = fallback,
			Trigger = trigger
		};
		if (!string.IsNullOrWhiteSpace(conditionKey))
		{
			transition.ConditionKey = conditionKey;
			transition.ExpectedValue = expected;
		}

		nodes.First(n => n.Id == fromId).Transitions.Add(transition);
	}

	private static string SanitizeFileName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			return "untitled";

		var cleaned = Regex.Replace(name, @"[^\w\-.]+", "_");
		return string.IsNullOrWhiteSpace(cleaned) ? "untitled" : cleaned;
	}

	private void EnsureNodeDefinition(NodeModel node)
	{
		var definition = _catalogService.GetDefinition(node.DefinitionId) ?? _catalogService.GetDefaultDefinitionForType(node.Type);
		node.DefinitionId = definition.Id;
		node.DefinitionTitle = definition.Title;
		EnsureNodeParameters(node, definition);
	}

	private static void EnsureNodeParameters(NodeModel node, NodeDefinition definition)
	{
		if (definition.Parameters == null || definition.Parameters.Count == 0)
			return;

		foreach (var parameter in definition.Parameters)
		{
			if (node.Parameters.FirstOrDefault(p => string.Equals(p.Key, parameter.Key, StringComparison.OrdinalIgnoreCase)) != null)
				continue;

			node.Parameters.Add(new NodeParameterValue
			{
				Key = parameter.Key,
				Type = parameter.Type,
				AllowMultiple = parameter.AllowMultiple,
				RawValue = parameter.DefaultValue ?? string.Empty
			});
		}
	}

	/// <summary>
	/// Rewrites saved "Name = value" offset params whose number no longer matches the current
	/// ActionOffsets table (route offsets move on game updates). Runtime resolution already
	/// prefers the name; this keeps the persisted text and editor dropdowns in sync too.
	/// </summary>
	private static void RefreshDriftedOffsets(NodeModel node)
	{
		foreach (var parameter in node.Parameters)
		{
			var drift = OffsetNameResolver.DetectDrift(parameter.RawValue);
			if (drift != null)
				parameter.RawValue = $"{drift.Value.Name} = {drift.Value.CurrentValue}";
		}
	}

	private static void SetParameterValue(NodeModel node, string key, string value)
	{
		var param = node.Parameters.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
		if (param == null) return;

		param.RawValue = value;
	}

	private static void SetParameterBool(NodeModel node, string key, bool value)
	{
		var param = node.Parameters.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
		if (param == null) return;

		param.BoolValue = value;
	}

	private void MigrateLegacyParameters(GraphModel model)
	{
		foreach (var node in model.Nodes)
		{
			if (node.Parameters.Count > 0)
				continue;

			var definition = _catalogService.GetDefaultDefinitionForType(node.Type);
			node.DefinitionId = definition.Id;
			node.DefinitionTitle = definition.Title;
			EnsureNodeParameters(node, definition);

			if (node.Type == NodeType.Action && node.Parameters.Count > 0)
			{
				node.Parameters[0].RawValue = node.ActionText ?? node.Description;
			}
			else if (node.Type == NodeType.Condition)
			{
				SetParameterValue(node, "signal", node.Transitions.FirstOrDefault(t => t.HasCondition)?.ConditionKey ?? string.Empty);
				SetParameterBool(node, "expected", true);
			}
		}
	}
}
