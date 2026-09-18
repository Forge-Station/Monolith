using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Goobstation.SlotMachine.ClawGame;

[Serializable, NetSerializable]
public sealed partial class ClawGameDoAfterEvent : SimpleDoAfterEvent;
