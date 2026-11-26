using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Gore;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Systems;
using Robust.Server.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Gore;

/// <summary>
/// G2: a body losing blood from a cut artery throws some of it around every few seconds. An arterial bleed or
/// an untreated amputation stump qualifies and nothing else does (playtest 5: a heavy ordinary bleed used to
/// spurt past a rate line; now it drips, the tier under this one).
/// </summary>
/// <remarks>
/// Nothing here scans wounds per frame. <see cref="WolfmedBleedSpurtComponent"/> is put on a body only
/// while it has a source and taken off the moment it does not, so the tick walks a handful of entities at
/// most; the condition is re-evaluated off the bleeding system's own <c>PartBleedingChangedEvent</c>,
/// which every treatment, tourniquet, clot and heal already goes through, and again on each spurt, so a
/// bleed that stops without one still switches the clock off.
/// </remarks>
public sealed class WolfmedBleedSpurtSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedDismembermentSystem _dismemberment = default!;
    [Dependency] private WolfmedGoreSystem _gore = default!;
    [Dependency] private WolfmedWoundSfxSystem _sfx = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;
    [Dependency] private WoundSystem _wounds = default!;

    private readonly HashSet<EntityUid> _pending = new();
    private readonly List<EntityUid> _finished = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundableComponent, PartBleedingChangedEvent>(OnPartBleedingChanged);
    }

    private void OnPartBleedingChanged(Entity<WoundableComponent> part, ref PartBleedingChangedEvent args)
    {
        // Coalesced: one refresh of a body raises this once per bleeding part, and each would walk the
        // whole body. One walk per body per tick instead.
        _pending.Add(args.Body);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        if (_pending.Count > 0)
        {
            foreach (var body in _pending)
                Refresh(body);

            _pending.Clear();
        }

        if (_sfx.Profile is not { } profile || !profile.BleedSpurt.Enabled ||
            !_config.GetCVar(WolfmedCVars.BleedSpurts))
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WolfmedBleedSpurtComponent>();
        while (query.MoveNext(out var uid, out var spurt))
        {
            if (now < spurt.NextSpurt)
                continue;

            // BRAIN: a spurt is arterial pressure. A stopped heart has none, so the clock waits instead
            // of firing; it picks straight back up when the heart does.
            if (HasComp<WolfmedCardiacArrestComponent>(uid))
            {
                spurt.NextSpurt = now + Next(profile.BleedSpurt);
                continue;
            }

            if (!TrySpurt(uid))
            {
                _finished.Add(uid);
                continue;
            }

            spurt.NextSpurt = now + Next(profile.BleedSpurt);
        }

        foreach (var uid in _finished)
            RemComp<WolfmedBleedSpurtComponent>(uid);

        _finished.Clear();
    }

    /// <summary>
    /// Starts or stops this body's spurt clock from what its wounds currently say. Public so a test, or
    /// anything that changes a bleed outside the bleeding system, can force the question.
    /// </summary>
    public void Refresh(EntityUid body)
    {
        if (TerminatingOrDeleted(body) || _sfx.Profile is not { } profile)
            return;

        var spec = profile.BleedSpurt;
        if (!spec.Enabled || !HasSpurtSource(body, spec, out _, out _))
        {
            RemComp<WolfmedBleedSpurtComponent>(body);
            return;
        }

        if (HasComp<WolfmedBleedSpurtComponent>(body))
            return;

        EnsureComp<WolfmedBleedSpurtComponent>(body).NextSpurt = _timing.CurTime + Next(spec);
    }

    /// <summary>
    /// Whether this body is bleeding hard enough to throw blood, and whether the worst of it is an open
    /// stump. A body that has already run dry cannot spurt however bad its wounds are.
    /// </summary>
    public bool HasSpurtSource(EntityUid body, WolfmedBleedSpurtSpec spec, out bool stump, out bool mechanical)
    {
        stump = false;
        mechanical = false;
        // A wound host without a bloodstream (a trader NPC) has nothing to throw.
        if (!HasComp<WoundHostComponent>(body) || !TryComp<BloodstreamComponent>(body, out var blood) ||
            _bloodstream.GetBloodLevelPercentage(body, blood) < spec.MinBloodLevel)
            return false;

        var major = false;
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (!TryComp(part, out WoundableComponent? woundable))
                continue;

            foreach (var wound in _wounds.GetWounds((part, woundable)))
            {
                if (!TryComp(wound, out WoundBleedingComponent? bleeding) || bleeding.CurrentRate <= 0f)
                    continue;

                if (spec.StumpWounds.Contains(wound.Comp.Prototype) &&
                    bleeding.Treatment == BleedingTreatment.None)
                {
                    stump = true;
                    mechanical = !_traits.IsOrganic((part, woundable));
                    return true;
                }

                // Playtest 5: arteries only. The rate line (spec.MajorRate) no longer makes an ordinary bleed spurt.
                if (_traits.TryGetBehavior(wound.Owner, out WolfmedArterialBleedBehavior _))
                {
                    major = true;
                    mechanical |= !_traits.IsOrganic((part, woundable));
                }
            }
        }

        return major;
    }

    /// <summary>
    /// One spurt: a spray in a random direction, a wet noise and a little of the body's own blood on the
    /// deck. Returns false when there is nothing left to spurt, which is what stops the clock.
    /// </summary>
    public bool TrySpurt(EntityUid body)
    {
        if (TerminatingOrDeleted(body) || _sfx.Profile is not { } profile)
            return false;

        var spec = profile.BleedSpurt;
        if (!HasSpurtSource(body, spec, out var stump, out var mechanical))
            return false;

        // No direction: the gore system picks a random cardinal, which is what a spurt should look like.
        _gore.TrySpawnSplatter(body, profile.HitSplatter, null, spec.Severity);

        // A chassis leaks under pressure; nothing about it is wet.
        var sound = mechanical && spec.MechanicalSound != null
            ? spec.MechanicalSound
            : stump ? spec.StumpSound ?? spec.Sound : spec.Sound;
        if (sound != null)
            _audio.PlayPvs(sound, body);

        _dismemberment.TrySpill(body, spec.SpillVolume);
        var spurted = new WolfmedBleedSpurtEvent(body, stump);
        RaiseLocalEvent(ref spurted);
        return true;
    }

    private TimeSpan Next(WolfmedBleedSpurtSpec spec)
    {
        var jitter = spec.Jitter.TotalSeconds * (_random.NextDouble() * 2 - 1);
        return TimeSpan.FromSeconds(Math.Max(1, spec.Interval.TotalSeconds + jitter));
    }
}
