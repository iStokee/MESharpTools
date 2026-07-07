using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MESharp.Recording
{
    public sealed class TraceSignalProfile
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsBuiltIn { get; set; }
        public TraceRecorderOptions Options { get; set; } = new();

        public string DisplayName => IsBuiltIn ? $"{Name} (built-in)" : Name;

        public TraceSignalProfile Clone()
        {
            return new TraceSignalProfile
            {
                Id = Id,
                Name = Name,
                Description = Description,
                IsBuiltIn = IsBuiltIn,
                Options = Options.Clone()
            };
        }
    }

    public static class TraceSignalProfileStore
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        public static IReadOnlyList<TraceSignalProfile> LoadAll()
        {
            var profiles = BuiltIns().Select(p => p.Clone()).ToList();
            foreach (var profile in LoadCustom())
            {
                var existing = profiles.FindIndex(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0)
                {
                    profiles[existing] = profile;
                }
                else
                {
                    profiles.Add(profile);
                }
            }

            return profiles;
        }

        public static TraceSignalProfile SaveCustom(TraceSignalProfile source, string? name = null)
        {
            var profile = source.Clone();
            profile.Name = string.IsNullOrWhiteSpace(name) ? profile.Name : name.Trim();
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                profile.Name = "Custom Trace Profile";
            }

            profile.Id = Slug(profile.Name);
            profile.IsBuiltIn = false;
            profile.Options.ProfileId = profile.Id;
            profile.Options.ProfileName = profile.Name;

            Directory.CreateDirectory(ProfileRoot());
            File.WriteAllText(Path.Combine(ProfileRoot(), profile.Id + ".json"), JsonSerializer.Serialize(profile, Json));
            return profile;
        }

        public static string ProfileRoot()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "MESharp", "trace_profiles");
        }

        private static IEnumerable<TraceSignalProfile> LoadCustom()
        {
            var root = ProfileRoot();
            if (!Directory.Exists(root))
            {
                yield break;
            }

            foreach (var path in Directory.EnumerateFiles(root, "*.json"))
            {
                TraceSignalProfile? profile = null;
                try
                {
                    profile = JsonSerializer.Deserialize<TraceSignalProfile>(File.ReadAllText(path), Json);
                }
                catch
                {
                    profile = null;
                }

                if (profile == null || string.IsNullOrWhiteSpace(profile.Name))
                {
                    continue;
                }

                profile.Id = string.IsNullOrWhiteSpace(profile.Id)
                    ? Path.GetFileNameWithoutExtension(path)
                    : profile.Id;
                profile.IsBuiltIn = false;
                profile.Options.ProfileId = profile.Id;
                profile.Options.ProfileName = profile.Name;
                yield return profile;
            }
        }

        private static IReadOnlyList<TraceSignalProfile> BuiltIns()
        {
            return new[]
            {
                BuiltIn("minimal", "Minimal", "Small, stable trace: clicks, player state, inventory, nearby actionable objects/NPCs, screenshots.",
                    new TraceRecorderOptions
                    {
                        ProfileId = "minimal",
                        ProfileName = "Minimal",
                        ScreenshotMode = TraceScreenshotMode.EventOnly,
                        IncludeNativeClicks = true,
                        IncludeManagedDoActions = false,
                        IncludeInventory = true,
                        IncludeObjects = true,
                        IncludeNpcs = true,
                        IncludeChat = false,
                        IncludeNonActionableObjects = false,
                        IncludeClickContextFrames = false,
                        IncludePlayerExtras = false,
                        IncludeEquipment = false,
                        IncludeInterfaces = false,
                        IncludeDgSignals = false,
                        IncludeInterfaceComponents = false,
                    }),
                BuiltIn("dg", "DG Puzzle Debug", "Daemonheim-focused trace: richer room objects, chat, DG classifications, managed action attempts, and click context.",
                    new TraceRecorderOptions
                    {
                        ProfileId = "dg",
                        ProfileName = "DG Puzzle Debug",
                        Radius = 24,
                        KeyframeEveryHeartbeats = 10,
                        ScreenshotMode = TraceScreenshotMode.EventPlusBurst,
                        ScreenshotBurstSeconds = 20,
                        IncludeNativeClicks = true,
                        IncludeManagedDoActions = true,
                        IncludeInventory = true,
                        IncludeObjects = true,
                        IncludeNpcs = true,
                        IncludeChat = true,
                        IncludeNonActionableObjects = true,
                        IncludeClickContextFrames = true,
                        IncludePlayerExtras = true,
                        IncludeEquipment = false,
                        IncludeInterfaces = true,
                        IncludeDgSignals = true,
                        IncludeInterfaceComponents = false,
                        MaxObjectsPerFrame = 240,
                    }),
                BuiltIn("quest", "Quest Authoring", "SharpQuester-friendly trace: chat, interfaces, inventory/equipment, managed actions, and click context.",
                    new TraceRecorderOptions
                    {
                        ProfileId = "quest",
                        ProfileName = "Quest Authoring",
                        Radius = 20,
                        ScreenshotMode = TraceScreenshotMode.EventPlusBurst,
                        ScreenshotBurstSeconds = 12,
                        IncludeNativeClicks = true,
                        IncludeManagedDoActions = true,
                        IncludeInventory = true,
                        IncludeObjects = true,
                        IncludeNpcs = true,
                        IncludeChat = true,
                        IncludeNonActionableObjects = false,
                        IncludeClickContextFrames = true,
                        IncludePlayerExtras = true,
                        IncludeEquipment = true,
                        IncludeInterfaces = true,
                        IncludeDgSignals = false,
                        IncludeInterfaceComponents = true,
                    }),
                BuiltIn("ui-api", "UI/API Calibration", "Interface-heavy capture for calibrating component ids, menu actions, and DoAction calls.",
                    new TraceRecorderOptions
                    {
                        ProfileId = "ui-api",
                        ProfileName = "UI/API Calibration",
                        Radius = 12,
                        ScreenshotMode = TraceScreenshotMode.EventPlusKeyframes,
                        IncludeNativeClicks = true,
                        IncludeManagedDoActions = true,
                        IncludeInventory = true,
                        IncludeObjects = true,
                        IncludeNpcs = false,
                        IncludeChat = true,
                        IncludeClickContextFrames = true,
                        IncludePlayerExtras = false,
                        IncludeEquipment = false,
                        IncludeInterfaces = true,
                        IncludeInterfaceComponents = true,
                        ScreenshotOnKeyframe = true,
                    }),
                BuiltIn("combat", "Combat Debug", "Combat-oriented trace: NPCs, HP/targeting/prayer/adrenaline, equipment, chat, and click context.",
                    new TraceRecorderOptions
                    {
                        ProfileId = "combat",
                        ProfileName = "Combat Debug",
                        Radius = 24,
                        ScreenshotMode = TraceScreenshotMode.EventPlusBurst,
                        ScreenshotBurstSeconds = 10,
                        IncludeNativeClicks = true,
                        IncludeManagedDoActions = true,
                        IncludeInventory = true,
                        IncludeObjects = true,
                        IncludeNpcs = true,
                        IncludeChat = true,
                        IncludeClickContextFrames = true,
                        IncludePlayerExtras = true,
                        IncludeEquipment = true,
                        IncludeInterfaces = false,
                        IncludeDgSignals = false,
                    }),
                BuiltIn("full", "Full Diagnostic", "Heaviest built-in profile. Use when narrow profiles do not explain a failure.",
                    new TraceRecorderOptions
                    {
                        ProfileId = "full",
                        ProfileName = "Full Diagnostic",
                        Radius = 28,
                        KeyframeEveryHeartbeats = 8,
                        ScreenshotMode = TraceScreenshotMode.Periodic1s,
                        ScreenshotOnKeyframe = true,
                        IncludeNativeClicks = true,
                        IncludeManagedDoActions = true,
                        IncludeInventory = true,
                        IncludeObjects = true,
                        IncludeNpcs = true,
                        IncludeChat = true,
                        IncludeNonActionableObjects = true,
                        IncludeClickContextFrames = true,
                        IncludePlayerExtras = true,
                        IncludeEquipment = true,
                        IncludeInterfaces = true,
                        IncludeDgSignals = true,
                        IncludeInterfaceComponents = true,
                        MaxObjectsPerFrame = 300,
                        MaxNpcsPerFrame = 120,
                        MaxInterfaceComponents = 180,
                    }),
            };
        }

        private static TraceSignalProfile BuiltIn(string id, string name, string description, TraceRecorderOptions options)
        {
            options.ProfileId = id;
            options.ProfileName = name;
            return new TraceSignalProfile
            {
                Id = id,
                Name = name,
                Description = description,
                IsBuiltIn = true,
                Options = options
            };
        }

        private static string Slug(string name)
        {
            var sb = new StringBuilder();
            foreach (var c in name.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
                else if (sb.Length > 0 && sb[^1] != '-')
                {
                    sb.Append('-');
                }
            }

            var slug = sb.ToString().Trim('-');
            return string.IsNullOrWhiteSpace(slug) ? "custom" : slug;
        }
    }
}
