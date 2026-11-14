using Content.Shared._WF.Xenonids.ManageHive;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Construction;

[RegisterComponent, NetworkedComponent]
[Access(typeof(ManageHiveSystem))]
public sealed partial class HiveConstructionSuppressAnnouncementsComponent : Component;
