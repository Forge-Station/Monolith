using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Tether.Harpoon;

/// <summary>
/// Manning a harpoon turret: who is buckled in, where the turret is pointing, and whether a shot is allowed.
/// Everything here runs on both sides so the operator's shot is predicted like any other gun.
/// </summary>
public abstract class SharedShipHarpoonTurretSystem : EntitySystem
{
    [Dependency] protected IGameTiming Timing = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] protected SharedTransformSystem Transforms = default!;

    /// <summary>Seconds between two refusals from the same turret, so holding the trigger does not spam.</summary>
    private static readonly TimeSpan PopupCooldown = TimeSpan.FromSeconds(2);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipHarpoonTurretComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ShipHarpoonTurretComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<ShipHarpoonTurretComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<ShipHarpoonTurretComponent, ComponentShutdown>(OnTurretShutdown);
        SubscribeLocalEvent<ShipHarpoonTurretComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<ShipHarpoonTurretComponent, ShotAttemptedEvent>(OnShotAttempted);
        SubscribeLocalEvent<MannedTurretOperatorComponent, MannedTurretGetGunEvent>(OnGetGun);
        SubscribeAllEvent<HarpoonTurretAimEvent>(OnAim);
    }

    /// <summary>The arc is measured from wherever the hardpoint was bolted down.</summary>
    private void OnMapInit(Entity<ShipHarpoonTurretComponent> turret, ref MapInitEvent args)
    {
        turret.Comp.MountRotation = Transform(turret).LocalRotation;
        Dirty(turret);
    }

    #region Manning

    private void OnStrapped(Entity<ShipHarpoonTurretComponent> turret, ref StrappedEvent args)
    {
        if (turret.Comp.Operator != null)
            return;

        // A dead hardpoint gives no controls, so do not leave the user sat in it either.
        if (!IsPowered(turret))
        {
            _buckle.Unbuckle(args.Buckle.Owner, null);
            return;
        }

        turret.Comp.Operator = GetNetEntity(args.Buckle.Owner);
        Dirty(turret);

        var operatorComp = EnsureComp<MannedTurretOperatorComponent>(args.Buckle.Owner);
        operatorComp.Turret = GetNetEntity(turret);
        Dirty(args.Buckle.Owner, operatorComp);
        GrantControls(turret, args.Buckle.Owner, operatorComp);
    }

    private void OnUnstrapped(Entity<ShipHarpoonTurretComponent> turret, ref UnstrappedEvent args)
    {
        EndManning(turret);
    }

    private void OnTurretShutdown(Entity<ShipHarpoonTurretComponent> turret, ref ComponentShutdown args)
    {
        EndManning(turret);
    }

    /// <summary>A dead hardpoint throws the operator out of the seat.</summary>
    private void OnPowerChanged(Entity<ShipHarpoonTurretComponent> turret, ref PowerChangedEvent args)
    {
        if (args.Powered || turret.Comp.Operator is not { } net || !TryGetEntity(net, out var user))
            return;

        _buckle.Unbuckle(user.Value, null);
        EndManning(turret);
    }

    /// <summary>Drops the operator, their controls and the turret's reference to them.</summary>
    protected void EndManning(Entity<ShipHarpoonTurretComponent> turret)
    {
        if (turret.Comp.Operator is not { } net)
            return;

        turret.Comp.Operator = null;
        if (!TerminatingOrDeleted(turret))
            Dirty(turret);

        if (!TryGetEntity(net, out var user) || !TryComp<MannedTurretOperatorComponent>(user, out var operatorComp))
            return;

        RevokeControls(user.Value, operatorComp);
        RemComp<MannedTurretOperatorComponent>(user.Value);
    }

    /// <summary>Server only: the reel and release actions live for as long as the manning does.</summary>
    protected virtual void GrantControls(EntityUid turret, EntityUid user, MannedTurretOperatorComponent component)
    {
    }

    protected virtual void RevokeControls(EntityUid user, MannedTurretOperatorComponent component)
    {
    }

    #endregion

    #region Shooting

    /// <summary>Hands the operator's shoot input to the turret's gun instead of whatever is in their hands.</summary>
    private void OnGetGun(Entity<MannedTurretOperatorComponent> ent, ref MannedTurretGetGunEvent args)
    {
        if (!TryGetEntity(ent.Comp.Turret, out var turret) || !HasComp<GunComponent>(turret))
            return;

        args.Gun = turret;
    }

    private void OnShotAttempted(Entity<ShipHarpoonTurretComponent> turret, ref ShotAttemptedEvent args)
    {
        if (turret.Comp.Operator != GetNetEntity(args.User))
        {
            args.Cancel();
            return;
        }

        if (!IsPowered(turret))
        {
            Refuse(turret, args.User, "wf-harpoon-turret-unpowered");
            args.Cancel();
            return;
        }

        if (TryComp<RopeAttachPointComponent>(turret, out var attach) && attach.Ropes.Count > 0)
        {
            Refuse(turret, args.User, "wf-harpoon-turret-cable-attached");
            args.Cancel();
            return;
        }

        if (args.Used.Comp.ShootCoordinates is { } target && !InArc(turret, target))
        {
            Refuse(turret, args.User, "wf-harpoon-turret-out-of-arc");
            args.Cancel();
        }
    }

    /// <summary>Whether a shot at these coordinates lies inside the turret's cone.</summary>
    public bool InArc(Entity<ShipHarpoonTurretComponent> turret, EntityCoordinates target)
    {
        var aim = Transforms.ToMapCoordinates(target).Position - Transforms.GetWorldPosition(turret);
        if (aim.LengthSquared() < 0.0001f)
            return true;

        return Math.Abs(LocalAim(turret, aim.ToWorldAngle())) <= turret.Comp.Arc.Theta / 2;
    }

    /// <summary>A world aim as an offset from the mounting rotation, wrapped to [-pi, pi].</summary>
    protected double LocalAim(Entity<ShipHarpoonTurretComponent> turret, Angle world)
    {
        var xform = Transform(turret);
        var parent = xform.ParentUid.IsValid() ? Transforms.GetWorldRotation(xform.ParentUid) : Angle.Zero;
        return Wrap(world.Theta - parent.Theta - turret.Comp.MountRotation.Theta);
    }

    private static double Wrap(double theta)
    {
        theta = (theta + Math.PI) % MathHelper.TwoPi;
        if (theta < 0)
            theta += MathHelper.TwoPi;

        return theta - Math.PI;
    }

    private void Refuse(Entity<ShipHarpoonTurretComponent> turret, EntityUid user, string message)
    {
        if (!Timing.IsFirstTimePredicted || Timing.CurTime < turret.Comp.NextPopup)
            return;

        turret.Comp.NextPopup = Timing.CurTime + PopupCooldown;
        _popup.PopupClient(Loc.GetString(message), turret, user);
    }

    #endregion

    /// <summary>Points the turret where the operator is aiming, clamped to the arc.</summary>
    private void OnAim(HarpoonTurretAimEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user ||
            !TryGetEntity(msg.Turret, out var uid) ||
            !TryComp<ShipHarpoonTurretComponent>(uid, out var comp) ||
            comp.Operator != GetNetEntity(user))
            return;

        var turret = new Entity<ShipHarpoonTurretComponent>(uid.Value, comp);
        var half = comp.Arc.Theta / 2;
        var clamped = Math.Clamp(LocalAim(turret, msg.Angle), -half, half);
        Transforms.SetLocalRotation(uid.Value, new Angle(comp.MountRotation.Theta + clamped));
    }

    /// <summary>A turret with no power receiver at all counts as powered, for test and admin spawns.</summary>
    public bool IsPowered(EntityUid turret)
    {
        return _power.IsPowered(turret);
    }
}
