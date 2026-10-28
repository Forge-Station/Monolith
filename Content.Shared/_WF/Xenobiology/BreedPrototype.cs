using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenobiology;

/// <summary>
/// A slime breed: its name, its extract and the slime components it spawns with.
/// </summary>
[Prototype]
public sealed partial class BreedPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string BreedName = string.Empty;

    [DataField]
    public EntProtoId ProducedExtract = "GreySlimeExtract";

    /// <summary>
    /// Components added to the slime, usually its <c>Slime</c> component.
    /// </summary>
    [DataField]
    public ComponentRegistry Components = new();
}

public sealed partial class SlimeLatchEvent : EntityTargetActionEvent;

public sealed partial class XenoVacEvent : EntityTargetActionEvent;

public sealed partial class XenoVacClearEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class SlimeLatchDoAfterEvent : SimpleDoAfterEvent;
