namespace MESharp.Recording
{
    public enum TraceScreenshotMode
    {
        EventOnly,
        EventPlusKeyframes,
        EventPlusBurst,
        Periodic1s
    }

    /// <summary>Tunables for a <see cref="TraceRecorder"/> session. Defaults suit a hand-played dungeon floor.</summary>
    public sealed class TraceRecorderOptions
    {
        /// <summary>Human-facing recorder profile name written into meta.json.</summary>
        public string ProfileName { get; set; } = "Minimal";

        /// <summary>Stable profile id written into meta.json. Built-ins use lowercase ids; custom profiles use their file name.</summary>
        public string ProfileId { get; set; } = "minimal";

        /// <summary>How often the continuous state heartbeat is written (ms).</summary>
        public int HeartbeatMs { get; set; } = 1000;

        /// <summary>How often the loop wakes — fast enough to catch every click promptly between heartbeats (ms).</summary>
        public int PollMs { get; set; } = 150;

        /// <summary>Tile radius for nearby objects/NPCs captured per sample.</summary>
        public int Radius { get; set; } = 16;

        /// <summary>Emit a full absolute-state keyframe every N heartbeats (so deltas can be re-anchored).</summary>
        public int KeyframeEveryHeartbeats { get; set; } = 15;

        /// <summary>Capture a PNG on clicks and when an interface opens (dialogs/puzzle/loot UIs).</summary>
        public bool CaptureScreenshots { get; set; } = true;

        /// <summary>Visual capture cadence. EventOnly keeps folders lean; burst/periodic modes are profile-driven diagnostics.</summary>
        public TraceScreenshotMode ScreenshotMode { get; set; } = TraceScreenshotMode.EventOnly;

        /// <summary>Also capture a PNG on every keyframe. Kept for old saved profiles; EventPlusKeyframes is preferred.</summary>
        public bool ScreenshotOnKeyframe { get; set; } = false;

        /// <summary>Seconds of 1 Hz screenshots after a click/interface event in EventPlusBurst mode.</summary>
        public int ScreenshotBurstSeconds { get; set; } = 12;

        /// <summary>Interval between burst screenshots.</summary>
        public int ScreenshotBurstIntervalMs { get; set; } = 1000;

        /// <summary>Interval for Periodic1s mode. Defaults to 1 Hz.</summary>
        public int PeriodicScreenshotMs { get; set; } = 1000;

        /// <summary>Native/player clicks from the ME DoAction detour. On for every useful profile.</summary>
        public bool IncludeNativeClicks { get; set; } = true;

        /// <summary>Managed API DoAction records, including failed script/tool dispatches. Useful when debugging automation.</summary>
        public bool IncludeManagedDoActions { get; set; }

        /// <summary>Inventory item id/amount map and inventory deltas.</summary>
        public bool IncludeInventory { get; set; } = true;

        /// <summary>Nearby actionable scene objects. Keep on for most domains.</summary>
        public bool IncludeObjects { get; set; } = true;

        /// <summary>Nearby NPC identities/health. Keep on for most domains.</summary>
        public bool IncludeNpcs { get; set; } = true;

        /// <summary>Include non-actionable nearby objects in keyframes/deltas. Heavier, but important for puzzle rooms.</summary>
        public bool IncludeNonActionableObjects { get; set; }

        /// <summary>Recent chat messages and deltas. Useful for quest, puzzle, and refusal diagnostics.</summary>
        public bool IncludeChat { get; set; }

        /// <summary>Maximum recent chat rows kept in a frame.</summary>
        public int MaxChatMessages { get; set; } = 30;

        /// <summary>Capture click-adjacent state frames before/after interactions for postcondition analysis.</summary>
        public bool IncludeClickContextFrames { get; set; }

        /// <summary>Maximum objects kept per frame after distance sorting.</summary>
        public int MaxObjectsPerFrame { get; set; } = 160;

        /// <summary>Maximum NPCs kept per frame after distance sorting.</summary>
        public int MaxNpcsPerFrame { get; set; } = 80;

        /// <summary>Visible interface component text/name/item snapshot. Heavier than open-status capture.</summary>
        public bool IncludeInterfaceComponents { get; set; }

        /// <summary>Maximum visible interface components kept per frame.</summary>
        public int MaxInterfaceComponents { get; set; } = 120;

        // ── Extended reads (off by default) ──────────────────────────────────────────
        // The baseline heartbeat uses ONLY native reads the proven scripts already run on a background loop
        // (tile/hp/moving/combat/targeting + Inventory/Objects/Npcs GetAll). The reads below are novel; a native
        // access violation in any of them crashes the client and is NOT catchable by C# try/catch, so they stay
        // opt-in until each is verified live. Flip them on one group at a time to bisect safely.

        /// <summary>Player prayer%, adrenaline, animation, target-HP (only when targeting), run state.</summary>
        public bool IncludePlayerExtras { get; set; }

        /// <summary>Equipped items (via Equipment.GetAllItems()).</summary>
        public bool IncludeEquipment { get; set; }

        /// <summary>Open interface statuses (InterfaceStatus.GetOpenStatuses()).</summary>
        public bool IncludeInterfaces { get; set; }

        /// <summary>DG room-signal summary + per-object Dungeoneering.ClassifyObject tagging.</summary>
        public bool IncludeDgSignals { get; set; }

        /// <summary>Turn on every extended read group at once.</summary>
        public void EnableAllExtended()
        {
            IncludePlayerExtras = IncludeEquipment = IncludeInterfaces = IncludeDgSignals = true;
        }

        public TraceRecorderOptions Clone()
        {
            return (TraceRecorderOptions)MemberwiseClone();
        }
    }
}
