using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Caverns;

/// <summary>Raised on a climb point (climbing up) or a shade (climbing down) when the climb's DoAfter ends.</summary>
[Serializable, NetSerializable]
public sealed partial class WFCavernClimbDoAfterEvent : SimpleDoAfterEvent;
