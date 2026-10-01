using Content.Shared._Shitmed.Targeting;
using Content.Shared.MedicalScanner;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Autodoc;

/// <summary>The autodoc window's UI key.</summary>
[Serializable, NetSerializable]
public enum AutodocUiKey : byte
{
    Key,
}

/// <summary>
/// Everything the pod window draws. The diagnostics field is the analyzer's own scan message, so the window
/// can feed the analyzer's doll and wound panel without a second payload type.
/// </summary>
[Serializable, NetSerializable]
public sealed class AutodocBuiState : BoundUserInterfaceState
{
    public HealthAnalyzerScannedUserMessage? Diagnostics;
    public List<AutodocProcedureEntry> Available = new();
    public List<AutodocQueueEntry> Queue = new();
    public List<AutodocReservoirEntry> Reservoir = new();
    /// <summary>Playtest 5: the reagents the pod uses, named, so a beaker it refuses says what it would take instead.</summary>
    public string ReservoirAccepted = string.Empty;
    public AutodocState State;
    public string Status = string.Empty;
    public string? CurrentStep;
    public float Progress;
    /// <summary>Game time the current step ends at, so the client can run the bar between updates.</summary>
    public TimeSpan StepEnds;
    public float StepLength;
    public string? DiskProgram;
    public bool DefibModule;
    public bool AutofixModule;
    /// <summary>The autofix module is fitted and switched on, so the pod plans and starts by itself.</summary>
    public bool Auto;
    public bool SelfService;
    public bool Anaesthesia;
    public bool Occupied;
    public string? TrayItem;
    /// <summary>What S.A.M. said last, for the terminal line.</summary>
    public string? LastLine;
    /// <summary>Waiting on clothing rather than on the tray, which is what the CUT button is for.</summary>
    public bool ClothingBlocked;
    /// <summary>The pod is topping the occupant's blood up out of the reservoir.</summary>
    public bool Transfusing;
    /// <summary>Playtest 3: whether the occupant breathes the pod's own air, and if not why not.</summary>
    public WolfmedAutodocSeal Seal;
}

/// <summary>A procedure the occupant's current condition allows on one part.</summary>
[Serializable, NetSerializable]
public readonly record struct AutodocProcedureEntry(EntProtoId Surgery, TargetBodyPart Part, bool Known);

/// <summary>A queued procedure with its live requirement lines.</summary>
[Serializable, NetSerializable]
public readonly record struct AutodocQueueEntry(EntProtoId Surgery, TargetBodyPart Part, List<string> Requirements);

/// <summary>One reservoir beaker slot: name, fill, capacity, and whether it holds anything usable.</summary>
[Serializable, NetSerializable]
public readonly record struct AutodocReservoirEntry(string Name, float Volume, float Max, bool Usable);

/// <summary>
/// The one rule both ends of the queue UI follow: while a procedure is running the top entry is under the
/// knife and cannot be moved or removed. The window used to work this out per row, so it left the buttons
/// around the running procedure enabled while the server refused every message they sent.
/// </summary>
public static class AutodocQueueRules
{
    public static int FirstMovable(AutodocState state) =>
        state is AutodocState.Idle or AutodocState.Complete ? 0 : 1;
}

/// <summary>Queues a surgery on a body part.</summary>
[Serializable, NetSerializable]
public sealed class AutodocQueueAddMessage(EntProtoId surgery, TargetBodyPart part) : BoundUserInterfaceMessage
{
    public readonly EntProtoId Surgery = surgery;
    public readonly TargetBodyPart Part = part;
}

/// <summary>Removes the queue entry at an index.</summary>
[Serializable, NetSerializable]
public sealed class AutodocQueueRemoveMessage(int index) : BoundUserInterfaceMessage
{
    public readonly int Index = index;
}

/// <summary>Moves the queue entry at an index one place up or down.</summary>
[Serializable, NetSerializable]
public sealed class AutodocQueueMoveMessage(int index, bool up) : BoundUserInterfaceMessage
{
    public readonly int Index = index;
    public readonly bool Up = up;
}

/// <summary>The window's control buttons.</summary>
public enum AutodocControl : byte
{
    Start,
    Pause,
    Abort,
    Eject,

    /// <summary>Fill the queue from the triage plan. In self-service it also starts: the FIX ME button.</summary>
    Plan,

    /// <summary>Toggle the autofix module's automatic mode.</summary>
    Auto,

    /// <summary>Cut and destroy whatever is in the way of the skin.</summary>
    CutClothing,
}

/// <summary>A press of one of the window's control buttons.</summary>
[Serializable, NetSerializable]
public sealed class AutodocControlMessage(AutodocControl control) : BoundUserInterfaceMessage
{
    public readonly AutodocControl Control = control;
}

/// <summary>Switches self-service anaesthesia on or off.</summary>
[Serializable, NetSerializable]
public sealed class AutodocAnaesthesiaMessage(bool enabled) : BoundUserInterfaceMessage
{
    public readonly bool Enabled = enabled;
}
