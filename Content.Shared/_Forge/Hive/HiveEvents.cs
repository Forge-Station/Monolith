using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Hive;


public sealed partial class HiveShopActionEvent : InstantActionEvent;


public sealed partial class HiveDevourActionEvent : EntityTargetActionEvent;


[Serializable, NetSerializable]
public sealed partial class HiveDevourDoAfterEvent : SimpleDoAfterEvent;


[Serializable, NetSerializable]
public sealed partial class HiveInjectDoAfterEvent : SimpleDoAfterEvent;


[ByRefEvent]
public readonly record struct HiveBodyDevouredEvent(EntityUid Target, int DnaGained, bool HadMind);
