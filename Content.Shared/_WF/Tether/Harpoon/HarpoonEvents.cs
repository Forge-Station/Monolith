using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Tether.Harpoon;

/// <summary>
/// Raised on an entity to ask whether a mounted weapon shoots in place of whatever it is holding. The gun code
/// raises it while resolving an entity's gun; a manned turret answers with its own.
/// </summary>
[ByRefEvent]
public record struct MannedTurretGetGunEvent
{
    public EntityUid? Gun;
}

/// <summary>Where the operator is pointing the turret. Sent by the operator's client and mirrored on the server.</summary>
[Serializable, NetSerializable]
public sealed class HarpoonTurretAimEvent : EntityEventArgs
{
    public NetEntity Turret;

    /// <summary>The aim as a world rotation; the turret clamps it to its arc.</summary>
    public Angle Angle;

    public HarpoonTurretAimEvent(NetEntity turret, Angle angle)
    {
        Turret = turret;
        Angle = angle;
    }
}

/// <summary>Takes the cable in a metre at a time. Toggles.</summary>
public sealed partial class HarpoonReelInActionEvent : InstantActionEvent;

/// <summary>Lets the cable out. Toggles.</summary>
public sealed partial class HarpoonPayOutActionEvent : InstantActionEvent;

/// <summary>Cuts the cable loose at the turret.</summary>
public sealed partial class HarpoonReleaseActionEvent : InstantActionEvent;

/// <summary>Prying an embedded harpoon back out.</summary>
[Serializable, NetSerializable]
public sealed partial class HarpoonPryDoAfterEvent : SimpleDoAfterEvent;
