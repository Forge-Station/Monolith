using System;
using System.Collections.Generic;
using Content.Shared.Access;
using Content.Shared.DoAfter;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Access.Components;

[Serializable, NetSerializable]
public sealed partial class JobReassignmentInjectorDoAfterEvent : SimpleDoAfterEvent
{
}

[RegisterComponent]
public sealed partial class JobReassignmentInjectorComponent : Component
{
    [DataField]
    public ProtoId<JobPrototype>? JobPrototype;

    [DataField]
    public List<ProtoId<AccessLevelPrototype>> AuthorizedAccess = new();

    [DataField]
    public List<EntProtoId> BodyImplants = new();

    [DataField]
    public TimeSpan InjectDelay = TimeSpan.FromSeconds(2);

    [DataField]
    public bool NeedHand = true;

    [DataField]
    public bool BreakOnHandChange = true;

    [DataField]
    public float MovementThreshold = 0.1f;
}
