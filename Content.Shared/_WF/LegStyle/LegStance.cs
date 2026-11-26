using Robust.Shared.Serialization;

namespace Content.Shared._WF.LegStyle;

/// <summary>
/// How a character stands. Default is whatever the species draws, so a saved character keeps its legs.
/// </summary>
[Serializable, NetSerializable]
public enum LegStance : byte
{
    Default = 0,
    Plantigrade = 1,
    Digitigrade = 2,
}
