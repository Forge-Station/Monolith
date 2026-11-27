using System.Numerics;
using Content.Server._WF.Lattice;
using Content.Shared._WF.AsteroidMining;
using Content.Shared._WF.WolfChem.Narcotics;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.WolfChem.Narcotics;

/// <summary>
/// A high with <see cref="WFDrugTrait.ExoticSense"/> feels where exotic matter is: the nearest rock that would drop
/// at least a shard is sent to its owner's screen as a light, further out the deeper the high. The carrier it rides
/// gains Drift faster for it.
/// </summary>
public sealed class WFExoticSenseSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFIntoxicationSystem _intoxication = default!;
    [Dependency] private WFLatticeSystem _lattice = default!;

    /// <summary>Seconds between looks.</summary>
    public const float Interval = 2f;

    /// <summary>Shards a rock must be expected to drop to be felt.</summary>
    public const float MinShards = 1f;

    /// <summary>Expected shards at which the light is at its brightest.</summary>
    public const float RichShards = 8f;

    /// <summary>How much faster a carrier gains Drift with a sensing mind aboard.</summary>
    public const float DriftGain = 1.5f;

    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFIntoxicationComponent, WFDriftGainEvent>(OnDriftGain);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + TimeSpan.FromSeconds(Interval);
        Tick();
    }

    /// <summary>
    /// Metres the sense reaches in a phase. Rocks only exist near a ship: a shuttle console loads them 700 m out and
    /// a mass scanner about 1,500 m, so more reach than that finds nothing more.
    /// </summary>
    public static float Reach(WFDrugPhase phase)
    {
        return phase switch
        {
            WFDrugPhase.Light => 500f,
            WFDrugPhase.Heavy => 1000f,
            WFDrugPhase.Overdose => 1500f,
            _ => 0f,
        };
    }

    /// <summary>Points every sensing body at its nearest rock, and stops the ones that came down.</summary>
    public void Tick()
    {
        var sensing = EntityQueryEnumerator<WFExoticSenseComponent>();
        while (sensing.MoveNext(out var uid, out _))
        {
            if (!Senses(uid))
                RemCompDeferred<WFExoticSenseComponent>(uid);
        }

        var query = EntityQueryEnumerator<WFIntoxicationComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!Senses(uid))
                continue;

            var sense = EnsureComp<WFExoticSenseComponent>(uid);
            var found = TryFindRock(_transform.GetMapCoordinates(uid), Reach(SensePhase(comp)), out var target, out var shards);
            var richness = found ? Math.Clamp(shards / RichShards, 0.25f, 1f) : 0f;
            if (sense.HasTarget == found
                && (!found || ((sense.Target - target).LengthSquared() < 0.25f && MathHelper.CloseTo(sense.Richness, richness))))
                continue;

            sense.HasTarget = found;
            sense.Target = target;
            sense.Richness = richness;
            Dirty(uid, sense);
        }
    }

    /// <summary>The nearest rock within reach that is expected to drop at least <see cref="MinShards"/>.</summary>
    public bool TryFindRock(MapCoordinates from, float reach, out Vector2 target, out float shards)
    {
        target = default;
        shards = 0f;
        if (from.MapId == MapId.Nullspace || reach <= 0f)
            return false;

        var best = reach * reach;
        var found = false;
        var rocks = EntityQueryEnumerator<WFAsteroidComponent>();
        while (rocks.MoveNext(out var rock, out var asteroid))
        {
            if (asteroid.Cracking)
                continue;

            var at = _transform.GetMapCoordinates(rock);
            if (at.MapId != from.MapId)
                continue;

            var distance = (at.Position - from.Position).LengthSquared();
            if (distance >= best)
                continue;

            var expected = asteroid.Mass * _lattice.ExoticAt(at);
            if (expected < MinShards)
                continue;

            best = distance;
            target = at.Position;
            shards = expected;
            found = true;
        }

        return found;
    }

    private bool Senses(EntityUid uid)
    {
        return _intoxication.HasTrait(uid, WFDrugTrait.ExoticSense) && !_mobState.IsDead(uid);
    }

    /// <summary>The furthest phase of any profile whose tier senses.</summary>
    private WFDrugPhase SensePhase(WFIntoxicationComponent comp)
    {
        var phase = WFDrugPhase.None;
        foreach (var (profile, level) in comp.Levels)
        {
            if (!_proto.TryIndex(profile, out var proto))
                continue;

            var tier = proto.TierAt(level);
            if (tier < 0 || !proto.Tiers[tier].Traits.Contains(WFDrugTrait.ExoticSense))
                continue;

            var reached = proto.PhaseOf(tier);
            if (reached > phase)
                phase = reached;
        }

        return phase;
    }

    private void OnDriftGain(Entity<WFIntoxicationComponent> ent, ref WFDriftGainEvent args)
    {
        if (Senses(ent))
            args.Multiplier = MathF.Max(args.Multiplier, DriftGain);
    }
}
