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

		var fileName = path ?? Path.Combine(_scriptsDirectory, $"{SanitizeFileName(script.Name)}.orbitfsm.json");
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

		foreach (var file in Directory.EnumerateFiles(_scriptsDirectory, "*.orbitfsm.json", SearchOption.TopDirectoryOnly))
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
		SetParameterValue(fish, "offset", "InteractNPC_route = 4928");
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
			Author = "Orbit",
			StartNodeId = start.Id,
			Nodes = new System.Collections.ObjectModel.ObservableCollection<NodeModel>(nodes),
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
		string? conditionKey = null, bool expected = true, bool fallback = false)
	{
		var transition = new TransitionModel
		{
			FromNodeId = fromId,
			ToNodeId = toId,
			Label = label,
			IsFallback = fallback
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
