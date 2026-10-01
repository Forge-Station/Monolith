// WOLFGATE(Wolfmed): ported from Onyx for Wolfmed.
using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.Medical;

[Serializable, NetSerializable]
public readonly record struct HealthAnalyzerOrganInfo(
    NetEntity Entity,
    FixedPoint2 Health,
    FixedPoint2 MaxHealth,
    int Order);
