using Content.Server._WF.Remade.Conversion;
using Content.Shared._WF.Lattice;
using Content.Shared.Alert;
using Content.Shared.Bed.Sleep;
using Content.Shared.Ghost;
using Content.Shared.Hands;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Lattice;

/// <summary>
/// The Drift a person carries in their own head, apart from their ship's: put there by a Monolith, by a
/// <see cref="WFDriftSourceComponent"/> they stand near, touch or carry, or by anything that calls <see cref="Add"/>.
/// It runs the same hallucinations as the Unmooring and fades once nothing is feeding it.
/// </summary>
public sealed partial class WFDriftMindSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFDriftEffectsSystem _effects = default!;

    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(1);

    private static readonly ProtoId<AlertPrototype> DriftAlert = "WFDrift";

    /// <summary>The gauge's top step, one per Drift level; one more severity past it is the garbled dial of the Deep.</summary>
    private const short GaugeSteps = 20;

    /// <summary>The profile whose hallucinations play off a map with no Lattice.</summary>
    private static readonly ProtoId<WFLatticeProfilePrototype> FallbackProfile = "WFDefault";

    /// <summary>Drift a second a head loses once nothing is feeding it.</summary>
    public const float Decay = 0.4f;

    /// <summary>How much faster a sleeping head clears.</summary>
    private const float SleepDecay = 3f;

    /// <summary>How long a person's felt Drift holds without being worked out again, off the Lattice.</summary>
    private static readonly TimeSpan FeltStale = TimeSpan.FromSeconds(3);

    private readonly HashSet<EntityUid> _fed = new();
    private readonly HashSet<Entity<ActorComponent>> _near = new();
    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFDriftSourceComponent, InteractHandEvent>(OnTouched);
        SubscribeLocalEvent<WFDriftSourceComponent, GotEquippedHandEvent>(OnPickedUp);
        SubscribeLocalEvent<WFGroundEvent>(OnGrounded);
    }

    /// <summary>A grounding drug keeps Drift off a person for a while and clears some out of their head.</summary>
    private void OnGrounded(ref WFGroundEvent args)
    {
        if (!CanDrift(args.Target))
            return;

        var grounded = EnsureComp<WFGroundedComponent>(args.Target);
        grounded.Until = _timing.CurTime + args.Duration;
        grounded.Share = args.Share;
        if (TryComp<WFDriftComponent>(args.Target, out var drift) && drift.Mind > 0f)
        {
            drift.Mind = Math.Max(0f, drift.Mind - args.Clear);
            Sync(args.Target, drift);
        }
    }

    /// <summary>Whether this person can hold Drift in their head at all.</summary>
    public bool CanDrift(EntityUid mob)
    {
        return HasComp<MobStateComponent>(mob)
               && !HasComp<GhostComponent>(mob)
               && !HasComp<WFRemadeComponent>(mob)
               && !HasComp<WFChoirMemberComponent>(mob)
               && !_mobState.IsDead(mob);
    }

    /// <summary>Puts Drift into a person's head, up to <paramref name="cap"/>, and holds off its fading this second.</summary>
    public void Add(EntityUid mob, float amount, WFDriftFlavour flavour, float cap = 100f)
    {
        if (amount <= 0f || !CanDrift(mob))
            return;

        var drift = EnsureComp<WFDriftComponent>(mob);
        _fed.Add(mob);
        if (drift.Mind < cap)
            drift.Mind = Math.Min(cap, drift.Mind + amount);

        if (drift.Flavour != flavour)
        {
            drift.Flavour = flavour;
            Dirty(mob, drift);
        }

        Sync(mob, drift);
    }

    private void OnTouched(EntityUid uid, WFDriftSourceComponent source, InteractHandEvent args)
    {
        Add(args.User, source.Touch, source.Flavour, source.Cap);
    }

    private void OnPickedUp(EntityUid uid, WFDriftSourceComponent source, GotEquippedHandEvent args)
    {
        Add(args.User, source.Touch, source.Flavour, source.Cap);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _next)
            return;

        var seconds = (float) TickRate.TotalSeconds;
        _next = now + TickRate;
        Feed(seconds);
        Fade(seconds);
        Gauge();
        _fed.Clear();
    }

    private void Feed(float seconds)
    {
        var sources = EntityQueryEnumerator<WFDriftSourceComponent, TransformComponent>();
        while (sources.MoveNext(out var uid, out var source, out var xform))
        {
            if (xform.MapID == MapId.Nullspace)
                continue;

            var carrier = CarrierOf(uid);
            if (carrier is { } holder)
                Add(holder, source.Carried * seconds, source.Flavour, source.Cap);

            if (source.Rate <= 0f || source.Range <= 0f)
                continue;

            var at = _transform.GetMapCoordinates(uid, xform);
            _near.Clear();
            _lookup.GetEntitiesInRange(at, source.Range, _near);
            foreach (var person in _near)
            {
                if (person.Owner == carrier)
                    continue;

                var away = (_transform.GetMapCoordinates(person).Position - at.Position).Length();
                var share = Math.Clamp(1f - away / source.Range, 0.25f, 1f);
                Add(person, source.Rate * share * seconds, source.Flavour, source.Cap);
            }
        }
    }

    /// <summary>Lets unfed heads fade, and plays hallucinations off them where the Lattice isn't already doing so.</summary>
    private void Fade(float seconds)
    {
        _proto.TryIndex(FallbackProfile, out var profile);
        var heads = EntityQueryEnumerator<WFDriftComponent, ActorComponent, TransformComponent>();
        while (heads.MoveNext(out var uid, out var drift, out var actor, out var xform))
        {
            if (drift.Mind <= 0f)
                continue;

            if (!_fed.Contains(uid) || !CanDrift(uid))
                drift.Mind = Math.Max(0f, drift.Mind - Decay * seconds * (HasComp<SleepingComponent>(uid) ? SleepDecay : 1f));

            Sync(uid, drift);
            if (profile != null && CanDrift(uid) && !HasComp<WFLatticeFieldComponent>(xform.MapUid))
                _effects.Roll(actor.PlayerSession, uid, drift.Mind, profile);
        }
    }

    /// <summary>
    /// Shows each player the Drift gauge while they are out of anchored space or hold any Drift, filled to the greater of
    /// their ship's and their own; in a garbling band it reads nothing.
    /// </summary>
    private void Gauge()
    {
        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out _, out var xform))
        {
            var own = CompOrNull<WFDriftComponent>(uid);
            var carrier = xform.GridUid is { } grid && TryComp<WFDriftComponent>(grid, out var ship) ? ship : own;
            var band = carrier?.Band ?? 0;

            // What they feel, once worked out; off the Lattice it goes stale and drops away.
            var fresh = own != null && _timing.CurTime - own.FeltAt < FeltStale;
            if (own is { FeltLevel: > 0 } && !fresh)
            {
                own.Felt = 0f;
                own.FeltLevel = 0;
                Dirty(uid, own);
            }

            var level = Math.Max(fresh ? own!.FeltLevel : carrier?.Level ?? 0, own?.MindLevel ?? 0);
            if (!CanDrift(uid) || band == 0 && (carrier?.Drift ?? 0f) <= 0f && (own?.Mind ?? 0f) <= 0f)
            {
                _alerts.ClearAlert(uid, DriftAlert);
                continue;
            }

            var severity = (short) Math.Clamp(level, 0, GaugeSteps);
            if (Garbled(xform.MapUid, band))
                severity = GaugeSteps + 1;
            // A Monolith's hold on the head reads in red, when it is the greater.
            else if (own is { Flavour: WFDriftFlavour.Monolith, Mind: > 0f } && own.MindLevel >= (fresh ? own.FeltLevel : carrier?.Level ?? 0))
                severity += GaugeSteps + 2;

            _alerts.ShowAlert(uid, DriftAlert, severity);
        }
    }

    private bool Garbled(EntityUid? map, int band)
    {
        return TryComp<WFLatticeFieldComponent>(map, out var field)
               && _proto.TryIndex(field.Profile, out var profile)
               && band < profile.Bands.Count
               && profile.Bands[band].Garbled;
    }

    private void Sync(EntityUid mob, WFDriftComponent drift)
    {
        var level = (int) (drift.Mind / WFDriftComponent.LevelStep);
        if (drift.MindLevel == level)
            return;

        drift.MindLevel = level;
        Dirty(mob, drift);
    }

    /// <summary>The person carrying a thing, however deep in bags or hands.</summary>
    private EntityUid? CarrierOf(EntityUid thing)
    {
        var current = thing;
        while (_container.TryGetContainingContainer((current, null, null), out var container))
        {
            current = container.Owner;
            if (HasComp<ActorComponent>(current))
                return current;
        }

        return null;
    }
}
