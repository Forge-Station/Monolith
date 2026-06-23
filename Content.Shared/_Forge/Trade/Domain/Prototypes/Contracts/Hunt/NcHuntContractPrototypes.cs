using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;


namespace Content.Shared._Forge.Trade;


[Serializable, NetSerializable,]
public enum NcHuntCompletionMode : byte
{
    ConfirmedKill = 0,
    TrophyTurnIn = 1,
    BodyTurnIn = 2
}

[DataDefinition]
public sealed partial class NcHuntTargetData
{
    [DataField("group")]
    public string Group { get; set; } = string.Empty;

    [DataField("prototype")]
    public string Prototype { get; set; } = string.Empty;

    [DataField("count", required: true)]
    public int Count { get; set; }

    /// <summary>
    ///     For BodyTurnIn hunts, marks the spawned target whose corpse must be brought back.
    /// </summary>
    [DataField("body")]
    public bool Body { get; set; }
}

[DataDefinition]
public sealed partial class NcHuntCompletionData
{
    [DataField("mode", required: true)]
    public NcHuntCompletionMode Mode { get; set; } = NcHuntCompletionMode.ConfirmedKill;

    [DataField("trophy")]
    public string Trophy { get; set; } = string.Empty;
}

[DataDefinition]
public sealed partial class NcHuntDebrisEntry
{
    [DataField("prototype", required: true)]
    public string Prototype { get; set; } = string.Empty;

    [DataField("weight")]
    public int Weight { get; set; } = 1;
}

[DataDefinition]
public sealed partial class NcHuntDungeonEntry
{
    [DataField("prototype", required: true)]
    public string Prototype { get; set; } = string.Empty;

    [DataField("weight")]
    public int Weight { get; set; } = 1;
}

[DataDefinition]
public sealed partial class NcHuntDungeonExteriorTileEntry
{
    [DataField("prototype", required: true)]
    public string Prototype { get; set; } = string.Empty;

    [DataField("weight")]
    public int Weight { get; set; } = 1;
}

[DataDefinition]
public sealed partial class NcHuntDungeonExteriorRockEntry
{
    [DataField("prototype", required: true)]
    public string Prototype { get; set; } = string.Empty;

    [DataField("weight")]
    public int Weight { get; set; } = 1;
}

[Prototype("ncHuntDungeonExteriorTilePreset")]
public sealed partial class NcHuntDungeonExteriorTilePresetPrototype : IPrototype
{
    public const string Default = "ForgeHuntDungeonExteriorTilesAsteroid";

    [DataField("entries", required: true)]
    public List<NcHuntDungeonExteriorTileEntry> Entries { get; private set; } = new();

    [IdDataField] public string ID { get; private set; } = default!;
}

[Prototype("ncHuntDungeonExteriorRockPreset")]
public sealed partial class NcHuntDungeonExteriorRockPresetPrototype : IPrototype
{
    public const string Default = "ForgeHuntDungeonExteriorRocksAsteroid";

    [DataField("entries", required: true)]
    public List<NcHuntDungeonExteriorRockEntry> Entries { get; private set; } = new();

    [IdDataField] public string ID { get; private set; } = default!;
}

[DataDefinition]
public sealed partial class NcHuntSpawnData
{
    [DataField("point", required: true)]
    public ContractPointSelectorPrototype Point { get; set; } = new();

    [DataField("debris")]
    public List<NcHuntDebrisEntry> Debris { get; set; } = new();

    [DataField("dungeons")]
    public List<NcHuntDungeonEntry> Dungeons { get; set; } = new();

    [DataField("dungeonExteriorTilePreset")]
    public string DungeonExteriorTilePreset { get; set; } = NcHuntDungeonExteriorTilePresetPrototype.Default;

    [DataField("dungeonExteriorTiles")]
    public List<NcHuntDungeonExteriorTileEntry> DungeonExteriorTiles { get; set; } = new();

    [DataField("dungeonExteriorRockPreset")]
    public string DungeonExteriorRockPreset { get; set; } = NcHuntDungeonExteriorRockPresetPrototype.Default;

    [DataField("dungeonExteriorRocks")]
    public List<NcHuntDungeonExteriorRockEntry> DungeonExteriorRocks { get; set; } = new();

    [DataField("debrisMinDistance")]
    public float DebrisMinDistance { get; set; }

    [DataField("debrisMaxDistance")]
    public float DebrisMaxDistance { get; set; }

    [DataField("debrisSafetyRadius")]
    public float DebrisSafetyRadius { get; set; }

    [DataField("debrisPlacementAttempts")]
    public int DebrisPlacementAttempts { get; set; }
}

[Prototype("ncHuntGroup")]
public sealed partial class NcHuntGroupPrototype : IPrototype
{
    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    [DataField("icon")]
    public string Icon { get; private set; } = string.Empty;

    [DataField("prototypes", required: true)]
    public List<string> Prototypes { get; private set; } = new();

    [IdDataField] public string ID { get; private set; } = default!;
}

[Prototype("ncHuntContract")]
public sealed partial class NcHuntContractPrototype : IPrototype
{
    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    [DataField("repeatable")]
    public bool Repeatable { get; private set; } = true;

    [DataField("icon")]
    public string Icon { get; private set; } = string.Empty;

    [DataField("activeTimeLimitSeconds")]
    public int ActiveTimeLimitSeconds { get; private set; } = 30 * 60;

    [DataField("gridName")]
    public string GridName { get; private set; } = string.Empty;

    [DataField("gridNames")]
    public List<string> GridNames { get; private set; } = new();

    [DataField("targets", required: true)]
    public List<NcHuntTargetData> Targets { get; private set; } = new();

    [DataField("completion", required: true)]
    public NcHuntCompletionData Completion { get; private set; } = new();

    [DataField("spawn", required: true)]
    public NcHuntSpawnData Spawn { get; private set; } = new();

    [DataField("reward", required: true)]
    public List<NcSupplyRewardEntry> Reward { get; private set; } = new();

    /// <summary>Optional extension conditions evaluated by registered server-side handlers.</summary>
    [DataField("conditions")]
    public List<ContractConditionDef> Conditions { get; private set; } = new();

    [IdDataField] public string ID { get; private set; } = default!;
}
