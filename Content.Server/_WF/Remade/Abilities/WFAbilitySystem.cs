using System.Numerics;
using Content.Server._WF.Remade.Conversion;
using Content.Server.NPC.HTN;
using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Camera;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Content.Shared.Jittering;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Abilities;

/// <summary>
/// Runs the abilities of awake Remade that have a target. It owns the cooldowns and picks what starts; what each ability
/// does is in its own <see cref="WFAbility"/> subclass, which uses the helpers here.
/// </summary>
public sealed partial class WFAbilitySystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedWFRemadeLimbsSystem _limbs = default!;

    /// <summary>Damage from this far off or more counts as being shot at.</summary>
    private const float ShotDistance = 2.5f;

    /// <summary>A shot passes close enough to count if it goes by this far to the side, and well ahead of the gun and within reach of it.</summary>
    private const float NearMissInner = 0.3f;
    private const float NearMissOuter = 1.6f;
    private const float NearMissMin = 1.5f;
    private const float NearMissReach = 30f;

    private readonly HashSet<Entity<MobStateComponent>> _mobs = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFRemadeAbilitiesComponent, RefreshMovementSpeedModifiersEvent>(OnSpeed);
        SubscribeLocalEvent<WFRemadeAbilitiesComponent, DamageChangedEvent>(OnDamaged);
    }

    private void OnSpeed(Entity<WFRemadeAbilitiesComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Active is { } active)
            args.ModifySpeed(active.SpeedModifier);
    }

    private void OnDamaged(Entity<WFRemadeAbilitiesComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased
            || args.Origin is not { } origin
            || origin == ent.Owner
            || ent.Comp.Active != null
            || _mobState.IsDead(ent))
        {
            return;
        }

        if (Distance(ent, origin) < ShotDistance)
            return;

        var ctx = new WFAbilityContext(this, ent, origin, false, _timing.CurTime);
        foreach (var ability in ent.Comp.Abilities)
        {
            if (ctx.Now < ability.NextUse || !Allowed(ent, ability))
                continue;

            if (Begin(ent, ability, ability.React(ctx, WFAbilityTrigger.Shot, origin), ctx))
                return;
        }
    }

    /// <summary>
    /// A gun was fired from a shooter at an aim point. Awake Remade standing just off that line get to react to the shot
    /// going past them.
    /// </summary>
    public void NearMiss(EntityUid shooter, MapCoordinates aim)
    {
        var from = _transform.GetMapCoordinates(shooter);
        var line = aim.Position - from.Position;
        var length = line.Length();
        if (from.MapId != aim.MapId || length < 0.5f)
            return;

        var direction = line / length;
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFRemadeAbilitiesComponent, ActiveNPCComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out _, out var xform))
        {
            if (xform.MapID != from.MapId || comp.Active != null || uid == shooter)
                continue;

            var offset = _transform.GetWorldPosition(xform) - from.Position;
            var along = Vector2.Dot(offset, direction);
            var across = MathF.Abs(offset.X * direction.Y - offset.Y * direction.X);
            if (along < NearMissMin || along > NearMissReach || across < NearMissInner || across > NearMissOuter || _mobState.IsDead(uid))
                continue;

            var ent = new Entity<WFRemadeAbilitiesComponent>(uid, comp);
            var ctx = new WFAbilityContext(this, uid, shooter, false, now);
            foreach (var ability in comp.Abilities)
            {
                if (now < ability.NextUse || !Allowed(ent, ability))
                    continue;

                if (Begin(ent, ability, ability.React(ctx, WFAbilityTrigger.NearMiss, shooter), ctx))
                    break;
            }
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFRemadeAbilitiesComponent, HTNComponent, ActiveNPCComponent>();
        while (query.MoveNext(out var uid, out var comp, out var htn, out _))
        {
            EntityUid? target = htn.Blackboard.TryGetValue<EntityUid>("Target", out var found, EntityManager) ? found : null;
            if (target != null && !IsLive(target.Value))
                target = null;

            var sighted = target != null && target != comp.LastTarget;
            comp.LastTarget = target;
            var ctx = new WFAbilityContext(this, uid, target, sighted, now);
            var ent = new Entity<WFRemadeAbilitiesComponent>(uid, comp);

            if (_mobState.IsDead(uid))
            {
                if (comp.Active != null)
                    End(ent, ctx);
                continue;
            }

            if (comp.Active is { } active)
            {
                if (!active.Update(ctx, frameTime))
                    End(ent, ctx);
                continue;
            }

            if (target == null)
                continue;

            foreach (var ability in comp.Abilities)
            {
                if (now < ability.NextUse || !Allowed(ent, ability))
                    continue;

                if (Begin(ent, ability, ability.TryStart(ctx), ctx))
                    break;
            }
        }
    }

    private bool Allowed(Entity<WFRemadeAbilitiesComponent> ent, WFAbility ability)
    {
        foreach (var need in ability.Needs)
        {
            if (!_limbs.Can(ent, need))
                return false;
        }

        return true;
    }

    private bool Begin(Entity<WFRemadeAbilitiesComponent> ent, WFAbility ability, WFAbilityResult result, in WFAbilityContext ctx)
    {
        if (result == WFAbilityResult.None)
            return false;

        ability.NextUse = ctx.Now + TimeSpan.FromSeconds(ability.Cooldown);
        if (result == WFAbilityResult.Running)
        {
            ent.Comp.Active = ability;
            RefreshSpeed(ent);
        }

        return true;
    }

    private void End(Entity<WFRemadeAbilitiesComponent> ent, in WFAbilityContext ctx)
    {
        if (ent.Comp.Active is not { } ability)
            return;

        ability.Stop(ctx);
        ability.NextUse = ctx.Now + TimeSpan.FromSeconds(ability.Cooldown);
        ent.Comp.Active = null;
        RefreshSpeed(ent);
    }

    /// <summary>The ability with this type, if the mob has one. Tests and later systems use it to start abilities by hand.</summary>
    public T? Find<T>(EntityUid uid) where T : WFAbility
    {
        if (!TryComp<WFRemadeAbilitiesComponent>(uid, out var comp))
            return null;

        foreach (var ability in comp.Abilities)
        {
            if (ability is T found)
                return found;
        }

        return null;
    }

    /// <summary>Whether the mob is up and fightable: not dead, not downed.</summary>
    public bool IsLive(EntityUid uid)
    {
        return !TerminatingOrDeleted(uid) && _mobState.IsAlive(uid);
    }

    /// <summary>Whether it's gone or dead. A downed mob isn't, and can still be run down.</summary>
    public bool IsDead(EntityUid uid)
    {
        return TerminatingOrDeleted(uid) || _mobState.IsDead(uid);
    }

    public float Distance(EntityUid a, EntityUid b)
    {
        return (_transform.GetWorldPosition(a) - _transform.GetWorldPosition(b)).Length();
    }

    /// <summary>Whether nothing solid lies between the two, within range.</summary>
    public bool CanSee(EntityUid from, EntityUid to, float range)
    {
        return _interaction.InRangeUnobstructed(from, to, range, CollisionGroup.Impassable);
    }

    /// <summary>Puts the target back on the mob's blackboard if its planning dropped it, so it keeps chasing.</summary>
    public void KeepTarget(EntityUid uid, EntityUid target)
    {
        if (TryComp<HTNComponent>(uid, out var htn) && !htn.Blackboard.TryGetValue<EntityUid>("Target", out _, EntityManager))
            htn.Blackboard.SetValue("Target", target);
    }

    public void RefreshSpeed(EntityUid uid)
    {
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    public void Play(EntityUid source, SoundSpecifier? sound)
    {
        _audio.PlayPvs(sound, source);
    }

    public void Popup(string message, EntityUid source, PopupType type = PopupType.Medium)
    {
        _popup.PopupEntity(message, source, type);
    }

    public bool Knockdown(EntityUid uid, float seconds)
    {
        return _stun.TryKnockdown(uid, TimeSpan.FromSeconds(seconds), true);
    }

    /// <summary>Living things within range of the mob that aren't Remade, nearest first not guaranteed.</summary>
    public List<EntityUid> Prey(EntityUid around, float range)
    {
        var result = new List<EntityUid>();
        _mobs.Clear();
        _lookup.GetEntitiesInRange(Transform(around).Coordinates, range, _mobs);
        foreach (var mob in _mobs)
        {
            if (mob.Owner != around && !HasComp<WFRemadeComponent>(mob) && !HasComp<WFRemadeAbilitiesComponent>(mob) && !IsDead(mob))
                result.Add(mob);
        }

        return result;
    }

    /// <summary>Shakes everything living nearby: jitters it and kicks its camera, less the further off it is.</summary>
    public void Frighten(EntityUid source, float radius, float seconds, string message)
    {
        var origin = _transform.GetWorldPosition(source);
        foreach (var prey in Prey(source, radius))
        {
            var offset = _transform.GetWorldPosition(prey) - origin;
            var closeness = 1f - Math.Clamp(offset.Length() / radius, 0f, 1f);
            _jitter.DoJitter(prey, TimeSpan.FromSeconds(seconds * (0.5f + closeness)), true, 8f, 12f, true);
            _recoil.KickCamera(prey, (offset.LengthSquared() > 0.01f ? offset.Normalized() : Vector2.UnitY) * (0.4f + closeness));
            _popup.PopupEntity(message, prey, prey, PopupType.MediumCaution);
        }
    }

    /// <summary>Steps the mob sideways to the line from where it was shot, either way, if there's room. Returns whether it moved.</summary>
    public bool Sidestep(EntityUid uid, EntityUid from, float distance)
    {
        var position = _transform.GetWorldPosition(uid);
        var away = position - _transform.GetWorldPosition(from);
        if (away.LengthSquared() < 0.01f)
            return false;

        away = away.Normalized();
        var side = new Vector2(-away.Y, away.X) * (_random.Prob(0.5f) ? 1f : -1f);
        var map = _transform.GetMapId(uid);
        for (var i = 0; i < 2; i++)
        {
            var ray = new CollisionRay(position, side, (int) CollisionGroup.MobMask);
            var blocked = false;
            foreach (var _ in _physics.IntersectRay(map, ray, distance + 0.4f, uid, true))
            {
                blocked = true;
                break;
            }

            if (!blocked)
            {
                _transform.SetWorldPosition(uid, position + side * distance);
                return true;
            }

            side = -side;
        }

        return false;
    }
}
