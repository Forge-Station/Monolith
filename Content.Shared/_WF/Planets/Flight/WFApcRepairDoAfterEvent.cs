using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Planets.Flight;

/// <summary>Do-after for pulsing a crash-faulted APC back to working order.</summary>
[Serializable, NetSerializable]
public sealed partial class WFApcRepairDoAfterEvent : SimpleDoAfterEvent;
