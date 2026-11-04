using Content.Shared.DoAfter;
using Content.Shared.EntityList;
using Content.Shared.EntityTable.EntitySelectors;
using Content.Shared.Tag;
using Content.Shared.Tools;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.LivingWorld;

/// <summary>
/// A plant picked by hand, or with a tool of <see cref="Tool"/> quality, for its loot; it stays and regrows after
/// <see cref="RegrowTime"/>. Its appearance carries <see cref="WFRegrowingHarvestVisuals.Harvested"/>.
/// </summary>
[RegisterComponent]
public sealed partial class WFRegrowingHarvestComponent : Component
{
    /// <summary>A loot table, the same kind <c>Gatherable</c> uses.</summary>
    [DataField]
    public ProtoId<EntityLootTablePrototype>? Loot;

    /// <summary>An entity table rolled as well as <see cref="Loot"/>.</summary>
    [DataField]
    public EntityTableSelector? Table;

    [DataField]
    public TimeSpan RegrowTime = TimeSpan.FromMinutes(10);

    [DataField]
    public TimeSpan HarvestTime = TimeSpan.FromSeconds(1.5);

    /// <summary>A tool quality needed to harvest, such as Slicing; none means bare hands.</summary>
    [DataField]
    public ProtoId<ToolQualityPrototype>? Tool;

    /// <summary>Tools with any of these tags harvest it too, such as a pickaxe, which has no tool quality.</summary>
    [DataField]
    public List<ProtoId<TagPrototype>> ToolTags = new();

    /// <summary>What the "needs a tool" popup names instead of the tool quality's name.</summary>
    [DataField]
    public LocId? ToolHint;

    [DataField]
    public SoundSpecifier? HarvestSound = new SoundPathSpecifier("/Audio/Effects/plant_rustle.ogg");

    /// <summary>Starts harvested, such as a bush grown bare.</summary>
    [DataField]
    public bool Harvested;

    [ViewVariables]
    public TimeSpan RegrowAt;
}

[Serializable, NetSerializable]
public enum WFRegrowingHarvestVisuals : byte
{
    Harvested,
}

[Serializable, NetSerializable]
public sealed partial class WFHarvestDoAfterEvent : SimpleDoAfterEvent;
