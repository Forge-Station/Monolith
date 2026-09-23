using System.Numerics;
using Content.Shared._WF.Tether;
using Content.Shared._WF.Tether.Harpoon;
using Content.Shared.Actions;
using Content.Shared.Examine;
using Content.Shared.Verbs;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Tools.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.GameStates;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Tether.Harpoon;

/// <summary>
/// The server half of the harpoon turret: the cable that pays out behind a shot harpoon, whether the harpoon bit
/// or glanced off, the tow cable it leaves behind, and the winch the operator works it with.
/// </summary>
public sealed class ShipHarpoonTurretSystem : SharedShipHarpoonTurretSystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private PvsOverrideSystem _pvs = default!;
    [Dependency] private RopeSystem _rope = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedProjectileSystem _projectiles = default!;
    [Dependency] private SharedToolSystem _tools = default!;

    private const string ReelInAction = "ActionWFHarpoonReelIn";
    private const string PayOutAction = "ActionWFHarpoonPayOut";
    private const string ReleaseAction = "ActionWFHarpoonRelease";
    private const string PryingQuality = "Prying";

    /// <summary>Loose bodies lighter than this are not worth sinking a harpoon into.</summary>
    private const float MinAnchorMass = 50f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipHarpoonTurretComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<ShipHarpoonTurretComponent, RopeDetachedEvent>(OnRopeDetached);

        // Runs ahead of the projectile code so a glancing hit can drop the embed before it happens.
        SubscribeLocalEvent<ShipHarpoonComponent, StartCollideEvent>(OnHarpoonCollide,
            before: new[] { typeof(SharedProjectileSystem) });
        SubscribeLocalEvent<ShipHarpoonComponent, EmbedEvent>(OnHarpoonEmbed);
        SubscribeLocalEvent<ShipHarpoonComponent, InteractUsingEvent>(OnHarpoonInteractUsing);
        SubscribeLocalEvent<ShipHarpoonComponent, HarpoonPryDoAfterEvent>(OnHarpoonPried);
        SubscribeLocalEvent<ShipHarpoonComponent, ExaminedEvent>(OnHarpoonExamined);
        SubscribeLocalEvent<ShipHarpoonComponent, GetVerbsEvent<AlternativeVerb>>(OnHarpoonAltVerbs);

        SubscribeLocalEvent<MannedTurretOperatorComponent, HarpoonReelInActionEvent>(OnReelIn);
        SubscribeLocalEvent<MannedTurretOperatorComponent, HarpoonPayOutActionEvent>(OnPayOut);
        SubscribeLocalEvent<MannedTurretOperatorComponent, HarpoonReleaseActionEvent>(OnRelease);
    }

    #region Controls

    protected override void GrantControls(EntityUid turret, EntityUid user, MannedTurretOperatorComponent component)
    {
        _actions.AddAction(user, ref component.ReelInAction, ReelInAction);
        _actions.AddAction(user, ref component.PayOutAction, PayOutAction);
        _actions.AddAction(user, ref component.ReleaseAction, ReleaseAction);
    }

    protected override void RevokeControls(EntityUid user, MannedTurretOperatorComponent component)
    {
        _actions.RemoveAction(user, component.ReelInAction);
        _actions.RemoveAction(user, component.PayOutAction);
        _actions.RemoveAction(user, component.ReleaseAction);
        component.ReelInAction = null;
        component.PayOutAction = null;
        component.ReleaseAction = null;

        // Operator is already cleared by the time controls are revoked, so resolve the turret directly.
        if (TryGetEntity(component.Turret, out var turret) && TryComp<ShipHarpoonTurretComponent>(turret, out var comp))
            SetReeling((turret.Value, comp), 0);
    }

    private void OnReelIn(EntityUid uid, MannedTurretOperatorComponent component, HarpoonReelInActionEvent args)
    {
        args.Handled = Toggle(uid, component, 1);
    }

    private void OnPayOut(EntityUid uid, MannedTurretOperatorComponent component, HarpoonPayOutActionEvent args)
    {
        args.Handled = Toggle(uid, component, -1);
    }

    private void OnRelease(EntityUid uid, MannedTurretOperatorComponent component, HarpoonReleaseActionEvent args)
    {
        if (!TryGetTurret(uid, component, out var turret))
            return;

        args.Handled = true;
        if (turret.Comp.Rope is not { } rope)
        {
            _popup.PopupEntity(Loc.GetString("wf-harpoon-turret-no-cable"), turret.Owner, uid);
            return;
        }

        SetReeling(turret, 0);
        _audio.PlayPvs(turret.Comp.ReleaseSound, turret.Owner);
        _rope.BreakRope(rope);
    }

    /// <summary>Starts or stops the winch. Pressing the same control twice stops it.</summary>
    private bool Toggle(EntityUid user, MannedTurretOperatorComponent component, int direction)
    {
        if (!TryGetTurret(user, component, out var turret))
            return false;

        if (turret.Comp.Rope == null)
        {
            _popup.PopupEntity(Loc.GetString("wf-harpoon-turret-no-cable"), turret.Owner, user);
            return true;
        }

        SetReeling(turret, turret.Comp.Reeling == direction ? 0 : direction);
        return true;
    }

    private void SetReeling(Entity<ShipHarpoonTurretComponent> turret, int direction)
    {
        if (turret.Comp.Reeling == direction)
            return;

        turret.Comp.Reeling = direction;
        if (direction == 0)
        {
            turret.Comp.ReelStream = _audio.Stop(turret.Comp.ReelStream);
            return;
        }

        turret.Comp.ReelStream ??= _audio
            .PlayPvs(turret.Comp.ReelSound, turret.Owner, AudioParams.Default.WithLoop(true))?.Entity;
    }

    private bool TryGetTurret(EntityUid user, MannedTurretOperatorComponent component, out Entity<ShipHarpoonTurretComponent> turret)
    {
        turret = default;
        if (!TryGetEntity(component.Turret, out var uid) || !TryComp<ShipHarpoonTurretComponent>(uid, out var comp) ||
            comp.Operator != GetNetEntity(user))
            return false;

        turret = (uid.Value, comp);
        return true;
    }

    #endregion

    #region Firing

    /// <summary>A fired harpoon belongs to the turret until it is cut loose, and trails a rope while it flies.</summary>
    private void OnGunShot(Entity<ShipHarpoonTurretComponent> turret, ref GunShotEvent args)
    {
        foreach (var (uid, _) in args.Ammo)
        {
            if (uid is not { } harpoon || !TryComp<ShipHarpoonComponent>(harpoon, out var comp))
                continue;

            ClearHarpoon(turret);
            Launch(harpoon);
            comp.Turret = GetNetEntity(turret);
            Dirty(harpoon, comp);
            turret.Comp.Harpoon = GetNetEntity(harpoon);
            Dirty(turret);
            StartFlightRope(turret, harpoon);
        }
    }

    /// <summary>
    /// The operator is buckled to the turret, so a round spawned at their feet is born parented to the turret and
    /// would fly in the mount's frame. Put it on the hull it was fired from, keeping the speed it left with.
    /// </summary>
    private void Launch(EntityUid harpoon)
    {
        var xform = Transform(harpoon);
        if (xform.ParentUid == xform.GridUid || xform.ParentUid == xform.MapUid)
            return;

        var velocity = _physics.GetMapLinearVelocity(harpoon);
        Transforms.AttachToGridOrMap(harpoon, xform);
        _physics.SetLinearVelocity(harpoon, velocity - _physics.GetMapLinearVelocity(Transform(harpoon).ParentUid));
    }

    /// <summary>
    /// A visual-only rope, built the way stage 1 builds a carried end: no joint, no forces, just something to
    /// watch pay out behind the harpoon.
    /// </summary>
    private void StartFlightRope(Entity<ShipHarpoonTurretComponent> turret, EntityUid harpoon)
    {
        var uid = Spawn(null, Transform(turret).Coordinates);
        var rope = EnsureComp<RopeComponent>(uid);
        rope.EndA = GetNetEntity(turret);
        rope.EndB = GetNetEntity(harpoon);
        rope.RopeType = turret.Comp.RopeType;
        rope.Length = 1f;
        rope.Carried = true;
        Dirty(uid, rope);
        _pvs.AddGlobalOverride(uid);
        turret.Comp.FlightRope = uid;
    }

    /// <summary>Drops whatever the turret still holds: its flight rope, its cable and its harpoon.</summary>
    private void ClearHarpoon(Entity<ShipHarpoonTurretComponent> turret)
    {
        SetReeling(turret, 0);
        if (turret.Comp.FlightRope is { } flight && !TerminatingOrDeleted(flight))
            Del(flight);

        turret.Comp.FlightRope = null;
        if (turret.Comp.Rope is { } rope && !TerminatingOrDeleted(rope))
            _rope.BreakRope(rope);

        turret.Comp.Rope = null;
        if (TryGetEntity(turret.Comp.Harpoon, out var harpoon) && TryComp<ShipHarpoonComponent>(harpoon, out var comp))
        {
            comp.Turret = null;
            Dirty(harpoon.Value, comp);
        }

        turret.Comp.Harpoon = null;
        if (!TerminatingOrDeleted(turret))
            Dirty(turret);
    }

    /// <summary>The cable being cut, snapped or untied at either end leaves the turret free to fire again.</summary>
    private void OnRopeDetached(Entity<ShipHarpoonTurretComponent> turret, ref RopeDetachedEvent args)
    {
        if (turret.Comp.Rope != args.Rope)
            return;

        SetReeling(turret, 0);
        turret.Comp.Rope = null;
        turret.Comp.Harpoon = null;
        Dirty(turret);
    }

    #endregion

    #region Impact

    /// <summary>
    /// Decides whether the harpoon was shot well: fast enough, and square enough on to the surface. A glancing
    /// hit loses the embed before the projectile code gets to it, so the harpoon simply drops.
    /// </summary>
    private void OnHarpoonCollide(Entity<ShipHarpoonComponent> harpoon, ref StartCollideEvent args)
    {
        if (args.OurFixtureId != SharedProjectileSystem.ProjectileFixture || !args.OtherFixture.Hard ||
            harpoon.Comp.Embedded || !HasComp<EmbeddableProjectileComponent>(harpoon))
            return;

        var velocity = _physics.GetMapLinearVelocity(harpoon) - _physics.GetMapLinearVelocity(args.OtherEntity);
        var speed = velocity.Length();
        if (speed >= harpoon.Comp.MinEmbedSpeed && CanHold(args.OtherEntity) &&
            Incidence(harpoon, args.OtherEntity, velocity / speed) <= harpoon.Comp.MaxIncidence.Theta)
            return;

        Glance(harpoon);
    }

    /// <summary>Angle between the flight path and the struck surface's normal, in radians.</summary>
    private double Incidence(EntityUid harpoon, EntityUid target, Vector2 direction)
    {
        var normal = EntryNormal(harpoon, target, direction);
        if (normal.LengthSquared() < 0.0001f)
            return Math.PI;

        return Math.Acos(Math.Clamp(Vector2.Dot(-direction, normal), -1f, 1f));
    }

    /// <summary>
    /// The face the harpoon flew in through, as a slab test of its flight path against the struck entity's bounds.
    /// The contact's own normal is no use here: a fast projectile is teleported into what it hit before the contact
    /// is generated, so the manifold points along the deepest overlap rather than out of the surface.
    /// </summary>
    private Vector2 EntryNormal(EntityUid harpoon, EntityUid target, Vector2 direction)
    {
        // Work in the target's own frame, so a rotated hull's faces are its real faces and not a world box.
        var (_, targetRot, worldMatrix, invMatrix) = Transforms.GetWorldPositionRotationMatrixWithInv(target);
        var box = invMatrix.TransformBox(_lookup.GetWorldAABB(target));
        var localDir = (-targetRot).RotateVec(direction);
        // Start well outside the bounds, so the slab test reads the entry face and not the overlap.
        var origin = Vector2.Transform(Transforms.GetWorldPosition(harpoon), invMatrix) - localDir * (box.Width + box.Height + 2f);
        var entry = float.NegativeInfinity;
        var normal = Vector2.Zero;
        Axis(localDir.X, origin.X, box.Left, box.Right, new Vector2(-1f, 0f), new Vector2(1f, 0f));
        Axis(localDir.Y, origin.Y, box.Bottom, box.Top, new Vector2(0f, -1f), new Vector2(0f, 1f));
        return targetRot.RotateVec(normal);

        // The last axis to be entered is the one whose face was struck.
        void Axis(float d, float o, float min, float max, Vector2 low, Vector2 high)
        {
            if (MathF.Abs(d) < 0.0001f)
                return;

            var near = ((d > 0f ? min : max) - o) / d;
            if (near <= entry)
                return;

            entry = near;
            normal = d > 0f ? low : high;
        }
    }

    /// <summary>Hulls, walls and anything heavy enough to anchor a tow cable.</summary>
    private bool CanHold(EntityUid target)
    {
        if (HasComp<MapGridComponent>(target) || Transform(target).Anchored)
            return true;

        return TryComp<PhysicsComponent>(target, out var physics) && physics.Mass >= MinAnchorMass;
    }

    /// <summary>A poor shot skips off, drops its cable and lies where it lands.</summary>
    private void Glance(Entity<ShipHarpoonComponent> harpoon)
    {
        RemComp<EmbeddableProjectileComponent>(harpoon);
        _audio.PlayPvs(harpoon.Comp.GlanceSound, harpoon);

        if (TryGetEntity(harpoon.Comp.Turret, out var turret) &&
            TryComp<ShipHarpoonTurretComponent>(turret, out var comp))
            ClearHarpoon((turret.Value, comp));
    }

    /// <summary>A harpoon that bit becomes the far end of a real tow cable.</summary>
    private void OnHarpoonEmbed(Entity<ShipHarpoonComponent> harpoon, ref EmbedEvent args)
    {
        harpoon.Comp.Embedded = true;
        Dirty(harpoon);
        EnsureComp<RopeAttachPointComponent>(harpoon);

        if (!TryGetEntity(harpoon.Comp.Turret, out var turret) ||
            !TryComp<ShipHarpoonTurretComponent>(turret, out var comp))
            return;

        var ent = new Entity<ShipHarpoonTurretComponent>(turret.Value, comp);
        if (ent.Comp.FlightRope is { } flight && !TerminatingOrDeleted(flight))
            Del(flight);

        ent.Comp.FlightRope = null;
        var distance = Vector2.Distance(_rope.GetAnchorPosition(turret.Value), _rope.GetAnchorPosition(harpoon));
        if (_rope.TryCreateRope(turret.Value, harpoon, ent.Comp.RopeType, distance * ent.Comp.Slack, out var rope))
        {
            ent.Comp.Rope = rope;
            // The winch cable comes with the turret; untying it must not mint coils.
            if (rope is { } ropeUid && TryComp<RopeComponent>(ropeUid, out var ropeComp))
                ropeComp.Refundable = false;
        }

        Dirty(ent);
    }

    #endregion

    #region Removal

    private void OnHarpoonInteractUsing(EntityUid uid, ShipHarpoonComponent component, InteractUsingEvent args)
    {
        if (args.Handled || !component.Embedded)
            return;

        args.Handled = _tools.UseTool(args.Used, args.User, uid, component.PryTime,
            new[] { PryingQuality }, new HarpoonPryDoAfterEvent(), out _);
    }

    private void OnHarpoonExamined(EntityUid uid, ShipHarpoonComponent component, ExaminedEvent args)
    {
        if (component.Embedded)
            args.PushMarkup(Loc.GetString("wf-harpoon-examine-embedded"));
    }

    /// <summary>The crowbar is the tool; the verb just makes it discoverable from the other hull.</summary>
    private void OnHarpoonAltVerbs(EntityUid uid, ShipHarpoonComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!component.Embedded || !args.CanAccess || !args.CanInteract || args.Using is not { } used ||
            !_tools.HasQuality(used, PryingQuality))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wf-harpoon-verb-pry"),
            Act = () => _tools.UseTool(used, user, uid, component.PryTime, new[] { PryingQuality },
                new HarpoonPryDoAfterEvent(), out _),
        });
    }

    private void OnHarpoonPried(EntityUid uid, ShipHarpoonComponent component, HarpoonPryDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !component.Embedded)
            return;

        args.Handled = true;
        component.Embedded = false;
        Dirty(uid, component);
        _projectiles.EmbedDetach(uid, null, args.User);
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ShipHarpoonTurretComponent>();
        while (query.MoveNext(out var uid, out var turret))
        {
            var ent = new Entity<ShipHarpoonTurretComponent>(uid, turret);
            UpdateFlight(ent);
            UpdateReel(ent, frameTime);
        }
    }

    /// <summary>Keeps the flight rope drawn along the harpoon, and cuts it once the harpoon outruns the cable.</summary>
    private void UpdateFlight(Entity<ShipHarpoonTurretComponent> turret)
    {
        if (turret.Comp.FlightRope is not { } flight)
            return;

        if (!TryComp<RopeComponent>(flight, out var rope) ||
            !TryGetEntity(turret.Comp.Harpoon, out var harpoon) || TerminatingOrDeleted(harpoon.Value))
        {
            ClearHarpoon(turret);
            return;
        }

        var distance = Vector2.Distance(
            _rope.GetAnchorPosition(turret.Owner), _rope.GetAnchorPosition(harpoon.Value));
        var max = _protos.TryIndex(turret.Comp.RopeType, out var proto) ? proto.MaxLength : 0f;
        if (max > 0f && distance > max)
        {
            ClearHarpoon(turret);
            return;
        }

        if (MathHelper.CloseTo(rope.Length, distance, 0.05f))
            return;

        rope.Length = MathF.Max(distance, 1f);
        Dirty(flight, rope);
    }

    /// <summary>Works the winch, stalling rather than snapping the cable when the load is too much.</summary>
    private void UpdateReel(Entity<ShipHarpoonTurretComponent> turret, float frameTime)
    {
        if (turret.Comp.Reeling == 0)
            return;

        if (turret.Comp.Rope is not { } rope || !TryComp<RopeComponent>(rope, out var comp))
        {
            SetReeling(turret, 0);
            return;
        }

        if (turret.Comp.Reeling > 0 && _rope.GetTension(rope) > turret.Comp.ReelMaxTension)
            return;

        // Reeling in shortens the cable, paying out lengthens it.
        var length = comp.Length - turret.Comp.Reeling * turret.Comp.ReelRate * frameTime;
        if (!_rope.SetLength(rope, length))
            SetReeling(turret, 0);
    }
}
