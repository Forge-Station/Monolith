using Content.Shared.Construction.Components;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Dictionary;

namespace Content.Shared._Forge.ShipWeapons.Components;

[RegisterComponent]
public sealed partial class ShipWeaponFabricatorComponent : Component
{
    public const string BoardContainerName = "weapon_board";
    public const string PartContainerName = "weapon_parts";

    [ViewVariables]
    public bool HasBoard => BoardContainer?.Count > 0;

    [ViewVariables]
    public bool Fabricating;

    [ViewVariables]
    public TimeSpan FabricationEndTime;

    [ViewVariables]
    public Dictionary<ProtoId<MachinePartPrototype>, int> Progress = new();

    [ViewVariables]
    public readonly Dictionary<ProtoId<StackPrototype>, int> MaterialProgress = new();

    [ViewVariables]
    public readonly Dictionary<string, int> ComponentProgress = new();

    [ViewVariables]
    public readonly Dictionary<ProtoId<TagPrototype>, int> TagProgress = new();

    [ViewVariables]
    public Dictionary<ProtoId<MachinePartPrototype>, int> Requirements = new();

    [ViewVariables]
    public Dictionary<ProtoId<StackPrototype>, int> MaterialRequirements = new();

    [ViewVariables]
    public Dictionary<string, GenericPartInfo> ComponentRequirements = new();

    [ViewVariables]
    public Dictionary<ProtoId<TagPrototype>, GenericPartInfo> TagRequirements = new();

    [DataField("boardWhitelist")]
    public EntityWhitelist? BoardWhitelist;

    [DataField("boardBlacklist")]
    public EntityWhitelist? BoardBlacklist;

    [DataField]
    public float PowerLoadIdle = 1000f;

    [DataField]
    public float PowerLoadActive = 10000f;

    [DataField]
    public EntProtoId OutputFlatpackPrototype = "ForgeShipWeaponFlatpack";

    [DataField]
    public int MaxBoardQueue = 8;

    [ViewVariables]
    public Container BoardContainer = default!;

    [ViewVariables]
    public Container PartContainer = default!;
}
