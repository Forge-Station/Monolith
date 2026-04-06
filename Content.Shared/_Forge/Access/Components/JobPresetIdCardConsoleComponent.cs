using Content.Shared.Containers.ItemSlots;
using Content.Shared.Roles;
using Content.Shared._Forge.Access.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Access.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedJobPresetIdCardConsoleSystem))]
public sealed partial class JobPresetIdCardConsoleComponent : Component
{
    public const string PrivilegedIdCardSlotId = "JobPresetIdCardConsole-privilegedId";
    public const string TargetIdCardSlotId = "JobPresetIdCardConsole-targetId";

    [DataField]
    public ItemSlot PrivilegedIdSlot = new();

    [DataField]
    public ItemSlot TargetIdSlot = new();

    [DataField, AutoNetworkedField]
    public List<ProtoId<JobPrototype>> RankPresets = new();

    [DataField]
    public List<EntProtoId> BodyImplants = new();
}
