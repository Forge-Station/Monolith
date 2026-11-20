using System.Linq;
using System.Numerics;
using Content.Shared._CE.ZLevels.Damage;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Chimera;

/// <summary>Runs flesh tick leaping, latching, feeding and retirement.</summary>
public sealed partial class WFFleshTickSystem : EntitySystem
{
    private static readonly ProtoId<TagPrototype> ChimeraTag = "Chimera";
    private static readonly ProtoId<NpcFactionPrototype> ChimeraFaction = "Chimera";
    private static readonly SoundSpecifier BiteSound = new SoundCollectionSpecifier("WFFleshTickBite");
    private static readonly SoundSpecifier HuntSound = new SoundCollectionSpecifier("WFFleshTickHunt");
    private static readonly SoundSpecifier DieSound = new SoundCollectionSpecifier("WFFleshTickDie");
    private static readonly TargetBodyPart[] TargetParts =
    {
        TargetBodyPart.Head,
        TargetBodyPart.Torso,
        TargetBodyPart.LeftArm,
        TargetBodyPart.LeftHand,
        TargetBodyPart.RightArm,
        TargetBodyPart.RightHand,
        TargetBodyPart.LeftLeg,
        TargetBodyPart.LeftFoot,
        TargetBodyPart.RightLeg,
        TargetBodyPart.RightFoot,
    };

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.2);
    private static readonly TimeSpan LeapLandingGrace = TimeSpan.FromSeconds(0.5);

    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private ReactiveSystem _reactive = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedProjectileSystem _projectiles = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;

    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFFleshTickComponent, AttemptMeleeEvent>(OnAttemptMelee);
        SubscribeLocalEvent<WFFleshTickComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<WFFleshTickComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFFleshTickComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<WFFleshTickComponent, ThrowDoHitEvent>(OnThrowHit);
        SubscribeLocalEvent<WFFleshTickComponent, LandEvent>(OnLand);
        SubscribeLocalEvent<WFFleshTickComponent, CEZFallingDamageCalculateEvent>(OnFallDamage);
        SubscribeLocalEvent<WFFleshTickComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<WFFleshTickComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<WFFleshTickComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnAttemptMelee(Entity<WFFleshTickComponent> ent, ref AttemptMeleeEvent args)
    {
        if (ent.Comp.Host != null || _timing.CurTime < ent.Comp.ResumeBitingAt)
            args.Cancelled = true;
    }

    private void OnMeleeHit(Entity<WFFleshTickComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.User != ent.Owner || ent.Comp.Host != null ||
            _timing.CurTime < ent.Comp.ResumeBitingAt)
            return;

        foreach (var target in args.HitEntities)
        {
            // The melee weapon already plays the bite sound; leap latches play their own.
            if (TryLatch(ent, target, playSound: false))
                break;
        }
    }

    private void OnTerminating(Entity<WFFleshTickComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.Host is { } host && !TerminatingOrDeleted(host) &&
            TryComp<EmbeddedContainerComponent>(host, out var container))
        {
            container.EmbeddedObjects.Remove(ent.Owner);
            Dirty(host, container);
        }
        ClearLatch(ent.Comp);
    }

    private void OnMapInit(Entity<WFFleshTickComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.SpawnedAt = _timing.CurTime;
        ent.Comp.NextLeap = _timing.CurTime;
        ScheduleHunt(ent.Comp);
    }

    private void OnThrowHit(Entity<WFFleshTickComponent> ent, ref ThrowDoHitEvent args)
    {
        if (!ent.Comp.Leaping)
            return;

        ent.Comp.Leaping = false;
        TryLatch(ent, args.Target);
    }

    private void OnLand(Entity<WFFleshTickComponent> ent, ref LandEvent args)
    {
        if (ent.Comp.Leaping)
            ent.Comp.LeapLandsBy = _timing.CurTime + LeapLandingGrace;

        ent.Comp.Leaping = false;
    }

    // A throw arcs through the z-level, so a missed leap comes down fast enough to count as a fall.
    private void OnFallDamage(Entity<WFFleshTickComponent> ent, ref CEZFallingDamageCalculateEvent args)
    {
        var now = _timing.CurTime;
        if (args.Fallen != ent.Owner || !(ent.Comp.Leaping && now < ent.Comp.NextLeap || now <= ent.Comp.LeapLandsBy))
            return;

        args.DamageMultiplier = 0f;
        args.StunMultiplier = 0f;
    }

    private void OnInteractHand(Entity<WFFleshTickComponent> ent, ref InteractHandEvent args)
    {
        ent.Comp.ProtectedFromRetirement = true;
        if (ent.Comp.Host != args.User)
            return;

        if (!TryComp<EmbeddableProjectileComponent>(ent, out var embedded) || embedded.EmbeddedIntoUid != args.User)
            return;

        args.Handled = true;
        Detach(ent);
    }

    private void OnDamageChanged(Entity<WFFleshTickComponent> ent, ref DamageChangedEvent args)
    {
        if (args.Damageable.TotalDamage > 0)
            ent.Comp.ProtectedFromRetirement = true;
    }

    private void OnMobStateChanged(Entity<WFFleshTickComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        if (!ent.Comp.DeathSoundPlayed)
        {
            ent.Comp.DeathSoundPlayed = true;
            _audio.PlayPvs(DieSound, ent.Owner, AudioParams.Default.WithMaxDistance(12f));
        }

        Detach(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextUpdate)
            return;
        _nextUpdate = now + UpdateInterval;

        var query = EntityQueryEnumerator<WFFleshTickComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (TerminatingOrDeleted(uid) || MetaData(uid).EntityPaused)
                continue;

            ProtectIfClaimed(uid, component);

            if (!_mobState.IsAlive(uid))
                continue;

            if (TryComp<EmbeddableProjectileComponent>(uid, out var embedded) && embedded.EmbeddedIntoUid is { } host)
            {
                if (component.Host != host || !IsAttachedHostValid((uid, component), host))
                {
                    Detach((uid, component));
                    continue;
                }

                if (now >= component.NextPulse)
                {
                    Pulse((uid, component));
                    component.NextPulse = now + TimeSpan.FromSeconds(component.PulseInterval);
                }
                continue;
            }

            ClearLatch(component);
            if (_containers.IsEntityInContainer(uid) || HasComp<ActorComponent>(uid))
                continue;
            if (component.Leaping)
                continue;

            if (ShouldRetire(uid, component, now))
            {
                QueueDel(uid);
                continue;
            }

            if (now >= component.NextHuntSound)
            {
                _audio.PlayPvs(HuntSound, uid, AudioParams.Default.WithMaxDistance(12f));
                ScheduleHunt(component);
            }

            if (now < component.NextLeap || FindLeapTarget(uid, component) is not { } target)
                continue;

            BeginLeap((uid, component), target);
        }
    }

    public bool CanLatch(Entity<WFFleshTickComponent> tick, EntityUid target)
    {
        if (TerminatingOrDeleted(tick.Owner) || !_mobState.IsAlive(tick.Owner))
            return false;
        if (TryComp<EmbeddableProjectileComponent>(tick, out var tickEmbedded) && tickEmbedded.EmbeddedIntoUid != null)
            return false;
        if (target == tick.Owner || TerminatingOrDeleted(target) || !_mobState.IsAlive(target))
            return false;
        if (_tags.HasTag(target, ChimeraTag))
            return false;
        if (_factions.IsMember(target, ChimeraFaction))
            return false;
        if (!HasComp<BloodstreamComponent>(target) || !TryComp<BodyComponent>(target, out var body))
            return false;
        if (!TargetParts.Any(part => HasBodyPart(target, part, body)))
            return false;

        if (!TryComp<EmbeddedContainerComponent>(target, out var container))
            return true;

        var count = 0;
        foreach (var embedded in container.EmbeddedObjects)
        {
            if (HasComp<WFFleshTickComponent>(embedded) && ++count >= tick.Comp.MaxTicksPerHost)
                return false;
        }
        return true;
    }

    public bool TryLatch(Entity<WFFleshTickComponent> tick, EntityUid target, bool playSound = true)
    {
        if (!CanLatch(tick, target) || !TrySelectBodyPart(target, out var targetPart))
            return false;

        tick.Comp.Host = target;
        tick.Comp.TargetPart = targetPart;
        tick.Comp.NextPulse = _timing.CurTime + TimeSpan.FromSeconds(tick.Comp.PulseInterval);

        var hit = new ProjectileHitEvent(new DamageSpecifier(), target, tick.Owner);
        RaiseLocalEvent(tick.Owner, ref hit);

        if (!TryComp<EmbeddableProjectileComponent>(tick, out var embedded) || embedded.EmbeddedIntoUid != target)
        {
            ClearLatch(tick.Comp);
            return false;
        }

        tick.Comp.ProtectedFromRetirement = true;
        if (playSound)
            _audio.PlayPvs(BiteSound, tick.Owner, AudioParams.Default.WithMaxDistance(12f));
        return true;
    }

    public bool Pulse(Entity<WFFleshTickComponent> tick)
    {
        if (tick.Comp.Host is not { } host || tick.Comp.TargetPart is not { } targetPart)
            return false;
        if (!TryComp<EmbeddableProjectileComponent>(tick, out var embedded) || embedded.EmbeddedIntoUid != host)
            return false;
        if (!IsAttachedHostValid(tick, host))
        {
            Detach(tick);
            return false;
        }

        var injection = new Solution("NaturalLetoferol", tick.Comp.InjectionAmount);
        if (_bloodstream.TryAddToChemicals(host, injection))
            _reactive.DoEntityReaction(host, injection, ReactionMethod.Injection);

        _bloodstream.TryModifyBloodLevel(host, -tick.Comp.BloodDrain);

        var damage = new DamageSpecifier();
        damage.DamageDict["Piercing"] = tick.Comp.BruteDamage;
        _damageable.TryChangeDamage(host,
            damage,
            origin: tick.Owner,
            targetPart: targetPart,
            partMultiplier: 1f,
            canSever: false);
        return true;
    }

    public bool RemoveFromHost(Entity<WFFleshTickComponent> tick, EntityUid host)
    {
        if (tick.Comp.Host != host)
            return false;
        if (!TryComp<EmbeddableProjectileComponent>(tick, out var embedded) || embedded.EmbeddedIntoUid != host)
            return false;

        Detach(tick);
        return true;
    }

    private void BeginLeap(Entity<WFFleshTickComponent> tick, EntityUid target)
    {
        tick.Comp.NextLeap = _timing.CurTime + TimeSpan.FromSeconds(tick.Comp.LeapCooldown);

        var origin = _transform.GetMapCoordinates(tick.Owner);
        var destination = _transform.GetMapCoordinates(target);
        if (origin.MapId != destination.MapId)
            return;

        var direction = destination.Position - origin.Position;
        if (direction.LengthSquared() < 0.09f)
        {
            TryLatch(tick, target);
            return;
        }

        tick.Comp.Leaping = true;
        _throwing.TryThrow(tick.Owner,
            direction,
            tick.Comp.LeapSpeed,
            tick.Owner,
            pushbackRatio: 0f,
            compensateFriction: false,
            recoil: false,
            animated: true,
            playSound: false,
            doSpin: false);

        if (!HasComp<ThrownItemComponent>(tick.Owner))
            tick.Comp.Leaping = false;
    }

    private EntityUid? FindLeapTarget(EntityUid tick, WFFleshTickComponent component)
    {
        EntityUid? nearest = null;
        var nearestDistance = float.MaxValue;
        var origin = _transform.GetMapCoordinates(tick);

        foreach (var candidate in _lookup.GetEntitiesInRange<MobStateComponent>(origin, component.LeapRange))
        {
            if (!CanLatch((tick, component), candidate.Owner))
                continue;
            if (!_interaction.InRangeUnobstructed(tick, candidate.Owner, component.LeapRange, popup: false))
                continue;

            var target = _transform.GetMapCoordinates(candidate.Owner);
            if (target.MapId != origin.MapId)
                continue;
            var distance = Vector2.DistanceSquared(origin.Position, target.Position);
            if (distance >= nearestDistance)
                continue;

            nearest = candidate.Owner;
            nearestDistance = distance;
        }
        return nearest;
    }

    private bool IsAttachedHostValid(Entity<WFFleshTickComponent> tick, EntityUid host)
    {
        return !TerminatingOrDeleted(host)
            && _mobState.IsAlive(host)
            && tick.Comp.TargetPart is { } part
            && TryComp<BodyComponent>(host, out var body)
            && HasBodyPart(host, part, body);
    }

    private bool TrySelectBodyPart(EntityUid host, out TargetBodyPart selected)
    {
        selected = default;
        if (!TryComp<BodyComponent>(host, out var body))
            return false;

        var candidates = new List<TargetBodyPart>();
        foreach (var part in TargetParts)
        {
            if (HasBodyPart(host, part, body))
                candidates.Add(part);
        }

        if (candidates.Count == 0)
            return false;
        selected = candidates[_random.Next(candidates.Count)];
        return true;
    }

    private bool HasBodyPart(EntityUid host, TargetBodyPart target, BodyComponent? body = null)
    {
        var (type, symmetry) = _body.ConvertTargetBodyPart(target);
        return _body.GetBodyChildrenOfType(host, type, body, symmetry).Any();
    }

    private void Detach(Entity<WFFleshTickComponent> tick)
    {
        tick.Comp.Leaping = false;
        tick.Comp.NextLeap = _timing.CurTime + TimeSpan.FromSeconds(tick.Comp.LeapCooldown);
        tick.Comp.ResumeBitingAt = tick.Comp.NextLeap;
        if (TryComp<EmbeddableProjectileComponent>(tick, out var embedded) && embedded.EmbeddedIntoUid != null)
            _projectiles.EmbedDetach(tick.Owner, embedded);
        if (!TerminatingOrDeleted(tick.Owner))
            _physics.SetBodyType(tick.Owner, BodyType.KinematicController);
        ClearLatch(tick.Comp);
    }

    private static void ClearLatch(WFFleshTickComponent component)
    {
        component.Host = null;
        component.TargetPart = null;
    }

    private void ProtectIfClaimed(EntityUid uid, WFFleshTickComponent component)
    {
        if (component.ProtectedFromRetirement)
            return;
        if (HasComp<ActorComponent>(uid)
            || TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind
            || _containers.IsEntityInContainer(uid))
        {
            component.ProtectedFromRetirement = true;
        }
    }

    private bool ShouldRetire(EntityUid uid, WFFleshTickComponent component, TimeSpan now)
    {
        if (component.ProtectedFromRetirement
            || now - component.SpawnedAt < TimeSpan.FromSeconds(component.RetirementTime))
            return false;

        var coordinates = _transform.GetMapCoordinates(uid);
        return !_lookup.GetEntitiesInRange<ActorComponent>(coordinates, component.RetirementPlayerRange).Any();
    }

    private void ScheduleHunt(WFFleshTickComponent component)
    {
        component.NextHuntSound = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(12f, 25f));
    }
}
