using Content.Shared.Actions;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Chimera;

/// <summary>Action that plants a flesh pustule on the targeted tile.</summary>
public sealed partial class WFPlantFleshPustuleActionEvent : WorldTargetActionEvent
{
    [DataField]
    public EntProtoId Entity = "WFFleshPustule";
}

/// <summary>Appearance keys the flesh pustule writes.</summary>
[Serializable, NetSerializable]
public enum WFFleshPustuleVisuals : byte
{
    Bursted,
}

/// <summary>Sprite layers of the flesh pustule.</summary>
[Serializable, NetSerializable]
public enum WFFleshPustuleVisualLayers : byte
{
    Closed,
    Popped,
}
