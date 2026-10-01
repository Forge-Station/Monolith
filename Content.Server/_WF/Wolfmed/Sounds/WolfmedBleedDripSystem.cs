using Content.Server.Body.Components;
using Content.Server._WF.Wolfmed.Gore;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Sounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Mobs.Systems;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Sounds;

/// <summary>
/// A body bleeding lightly drips: one of Bob's drips every <c>wolfmed.drip_sound_interval</c> seconds while its summed
/// external bleed rate is above zero and under <c>wolfmed.drip_sound_below</c>. The tier under G2's spurts: a body
/// with a spurt source never drips, and neither does a corpse or a chassis.
/// </summary>
/// <remarks>
/// Built like <see cref="WolfmedBleedSpurtSystem"/>: the clock component sits only on bodies that drip, is
/// re-evaluated off the bleeding system's <c>PartBleedingChangedEvent</c>, and again at every drip, so a bleed that
/// stops without an event still switches the clock off.
/// </remarks>
public sealed class WolfmedBleedDripSystem : EntitySystem
{
    public static readonly ProtoId<SoundCollectionPrototype> DripCollection = "WFWolfmedBloodDrip";

    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedBleedSpurtSystem _spurts = default!;
    [Dependency] private WolfmedOrganicSoundSystem _organic = default!;
    [Dependency] private WolfmedWoundSfxSystem _sfx = default!;
    [Dependency] private WoundSystem _wounds = default!;

    private readonly HashSet<EntityUid> _pending = new();
    private readonly List<EntityUid> _finished = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        // The bleeding system raises this on the part; the WoundableComponent pair belongs to the spurt system.
        SubscribeLocalEvent<BodyPartComponent, PartBleedingChangedEvent>(OnPartBleedingChanged);
    }

    private void OnPartBleedingChanged(Entity<BodyPartComponent> part, ref PartBleedingChangedEvent args) =>
        _pending.Add(args.Body);

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        if (_pending.Count > 0)
        {
            foreach (var body in _pending)
                Refresh(body);

            _pending.Clear();
        }

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WolfmedBleedDripComponent>();
        while (query.MoveNext(out var uid, out var drip))
        {
            if (now < drip.NextDrip)
                continue;

            if (!TryDrip(uid))
            {
                _finished.Add(uid);
                continue;
            }

            drip.NextDrip = now + Interval;
        }

        foreach (var uid in _finished)
            RemComp<WolfmedBleedDripComponent>(uid);

        _finished.Clear();
    }

    private TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(0.5f, _config.GetCVar(WolfmedCVars.DripSoundInterval)));

    /// <summary>
    /// Starts or stops this body's drip clock from what its wounds say now. Public so a test, or anything that changes
    /// a bleed outside the bleeding system, can force the question. The first drip comes one interval later.
    /// </summary>
    public void Refresh(EntityUid body)
    {
        if (TerminatingOrDeleted(body))
            return;

        if (!HasDripSource(body))
        {
            RemComp<WolfmedBleedDripComponent>(body);
            return;
        }

        if (HasComp<WolfmedBleedDripComponent>(body))
            return;

        EnsureComp<WolfmedBleedDripComponent>(body).NextDrip = _timing.CurTime + Interval;
    }

    /// <summary>
    /// Whether this body is bleeding lightly enough to drip: living flesh with a bloodstream, bleeding, under the
    /// drip line, and with no spurt source (an arterial bleed or an open stump spurts whatever its rate).
    /// </summary>
    public bool HasDripSource(EntityUid body)
    {
        if (!HasComp<WoundHostComponent>(body) || !HasComp<BloodstreamComponent>(body) || _mobState.IsDead(body) ||
            !_organic.IsOrganicBody(body))
            return false;

        var rate = ExternalBleedRate(body);
        if (rate <= 0f || rate >= _config.GetCVar(WolfmedCVars.DripSoundBelow))
            return false;

        return _sfx.Profile is not { } profile || !_spurts.HasSpurtSource(body, profile.BleedSpurt, out _, out _);
    }

    /// <summary>Every open wound's bleed rate summed over the body: what the inspection calls oozing or worse.</summary>
    public float ExternalBleedRate(EntityUid body)
    {
        var rate = 0f;
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (!TryComp(part, out WoundableComponent? woundable))
                continue;

            foreach (var wound in _wounds.GetWounds((part, woundable)))
            {
                if (TryComp(wound, out WoundBleedingComponent? bleeding))
                    rate += bleeding.CurrentRate;
            }
        }

        return rate;
    }

    /// <summary>One drip, if the body still qualifies. Returns false when it does not, which stops the clock.</summary>
    public bool TryDrip(EntityUid body)
    {
        if (TerminatingOrDeleted(body) || !HasDripSource(body))
            return false;

        var volume = _config.GetCVar(WolfmedCVars.DripSoundVolume);
        _audio.PlayPvs(new SoundCollectionSpecifier(DripCollection), body,
            AudioParams.Default.WithVolume(volume).WithVariation(0.1f));

        if (TryComp(body, out WolfmedBleedDripComponent? drip))
            drip.LastDrip = _timing.CurTime;

        return true;
    }
}
