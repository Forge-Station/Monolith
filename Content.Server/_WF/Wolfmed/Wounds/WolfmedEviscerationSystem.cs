using Content.Server._WF.Wolfmed.Gore;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Steps.Parts;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Surgery;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Server.Audio;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>
/// EVISC: a torso opened by a hit the cap could not absorb. D9 keeps the torso attached, so its overflow
/// buys disembowelment instead of dismemberment: one wound that will not clot or close, and the contents
/// of the belly on the deck.
/// </summary>
/// <remarks>
/// Server side, because organs, bleeding and puddles are. Driven by
/// <see cref="WolfmedTorsoOverflowEvent"/>, which the vendored amputation system raises and decides
/// nothing about; every threshold, every ejected slot and every sound is
/// <see cref="WolfmedEviscerationProfilePrototype"/> data named by the part's own profile, so an IPC and a
/// human differ only in YAML. The belly being open is expressed as Shitmed's own incision state, which is
/// what lets the existing organ surgeries run on a patient nobody has cut.
/// </remarks>
public sealed class WolfmedEviscerationSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private WolfmedBodyPartSystem _wfPart = default!;
    [Dependency] private WolfmedDismembermentSystem _dismemberment = default!;
    [Dependency] private WolfmedGoreSystem _gore = default!;
    [Dependency] private WolfmedMachineSparkSystem _sparks = default!;
    [Dependency] private WolfmedSurgeryConditionSystem _conditions = default!;
    [Dependency] private WolfmedWoundSfxSystem _sfx = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;
    [Dependency] private WoundBleedingSystem _bleeding = default!;
    [Dependency] private WoundSystem _wounds = default!;

    private readonly List<Entity<OrganComponent>> _ejecting = new();

    /// <summary>Test seam, mirroring <see cref="WolfmedWoundRuleSystem.ForcedRoll"/>: a forced chance roll.</summary>
    public float? ForcedRoll;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedTorsoOverflowEvent>(OnTorsoOverflow);
        SubscribeLocalEvent<WolfmedSurgeryCloseEviscerationEffectComponent, SurgeryStepEvent>(OnCloseEvisceration);
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
    }

    private void OnTorsoOverflow(ref WolfmedTorsoOverflowEvent args)
    {
        TryEviscerate(args.Body, args.Part, args.Damage, args.IsExplosion);
    }

    /// <summary>
    /// Tears the part open if this hit was big enough and the right kind. Returns the wound, or null when
    /// nothing happened. Public so a test and any future source can reach it without a real hit.
    /// </summary>
    public EntityUid? TryEviscerate(EntityUid body, EntityUid part, DamageSpecifier overflow, bool explosion)
    {
        if (!_net.IsServer || TerminatingOrDeleted(body) || TerminatingOrDeleted(part) ||
            !TryComp(part, out WoundableComponent? woundable) ||
            !TryComp(part, out BodyPartComponent? bodyPart) || bodyPart.Body != body ||
            HasComp<WolfmedEviscerationComponent>(part))
            return null;

        if (_wfPart.Get(part).EviscerationProfile is not { } profileId ||
            !_prototypes.TryIndex(profileId, out WolfmedEviscerationProfilePrototype? profile) ||
            !profile.IsFinishingHit(overflow, explosion))
            return null;

        var mechanical = _traits.IsMechanical((part, woundable));
        var spec = profile.Get(mechanical);
        if (spec.Wound is not { } woundId ||
            _wounds.CreateOrMergeWound((part, woundable), woundId, spec.Severity) is not { } wound)
            return null;

        // The belly is already open, so the operation that would have opened it is already done. Shitmed
        // reads its incision state off these two components, so granting them is the whole of the shortcut:
        // every torso surgery that wants an open incision now lists, with no scalpel and no retractor.
        var marker = EnsureComp<WolfmedEviscerationComponent>(part);
        marker.Wound = woundId;
        marker.GrantedIncision = !HasComp<IncisionOpenComponent>(part);
        marker.GrantedRetraction = !HasComp<SkinRetractedComponent>(part);
        EnsureComp<IncisionOpenComponent>(part);
        EnsureComp<SkinRetractedComponent>(part);

        Eject(body, (part, bodyPart), spec, explosion);
        Feedback(body, part, spec, mechanical, spec.Severity);

        if (mechanical)
            _sparks.QueueRefresh(body);

        return wound;
    }

    /// <summary>
    /// Puts the listed organ slots on the deck around the body. The brain is never listed: it is in the
    /// head, and a chassis keeps its positronic one for the same reason.
    /// </summary>
    private void Eject(
        EntityUid body,
        Entity<BodyPartComponent> part,
        WolfmedEviscerationSpec spec,
        bool explosion)
    {
        // Collected first: removing an organ mutates the container being walked.
        _ejecting.Clear();
        foreach (var (organ, comp) in _body.GetPartOrgans(part.Owner, part.Comp))
        {
            if (comp.SlotId is not { } slot || TerminatingOrDeleted(organ))
                continue;

            if (!spec.Organs.Contains(slot) &&
                !(spec.VitalOrgans.Contains(slot) && (explosion || Roll(spec.VitalChance))))
                continue;

            _ejecting.Add((organ, comp));
        }

        foreach (var organ in _ejecting)
        {
            if (!_body.RemoveOrgan(organ.Owner, organ.Comp))
                continue;

            _transform.DropNextTo(organ.Owner, body);
            _throwing.TryThrow(organ.Owner, _random.NextAngle().ToVec() * spec.ScatterSpeed,
                baseThrowSpeed: spec.ScatterSpeed, pushbackRatio: 0f, playSound: false, doSpin: true);
        }

        _ejecting.Clear();
    }

    /// <summary>The noise, the spray and the sentence. All of it reuses V2's and G1's shipped specs.</summary>
    private void Feedback(
        EntityUid body,
        EntityUid part,
        WolfmedEviscerationSpec spec,
        bool mechanical,
        FixedPoint2 severity)
    {
        // The tear itself: the dismemberment sound, effect and spill for this material.
        _dismemberment.Play(body, part);
        _dismemberment.TrySpill(body, spec.SpillVolume);

        if (spec.Sound != null)
            _audio.PlayPvs(spec.Sound, body);

        // G1's spray, at a severity past the top distance band, so it throws as far as the system ever does.
        if (_sfx.Profile is { } profile)
            _gore.TrySpawnSplatter(body, profile.HitSplatter, null, severity);

        _popup.PopupEntity(Loc.GetString(spec.Popup, ("target", body)), body, PopupType.LargeCaution);
    }

    /// <summary>
    /// The closing surgery's last step: the tear becomes an ordinary sutured wound, at the severity its
    /// <see cref="WolfmedClearedWoundBehavior"/> declares. It completes whether or not the organs are back,
    /// which is deliberate: a closed patient with no liver is a transplant, not an open abdomen.
    /// </summary>
    private void OnCloseEvisceration(
        Entity<WolfmedSurgeryCloseEviscerationEffectComponent> ent,
        ref SurgeryStepEvent args)
    {
        if (_conditions.FindWound(args.Part, ent.Comp.WoundPrototype) is not { } wound)
            return;

        var replacement = _traits.TryGetBehavior(wound.Owner, out WolfmedClearedWoundBehavior cleared)
            ? cleared
            : null;

        _wounds.RemoveWound(wound.Owner);

        if (replacement?.Wound is not { } id)
            return;

        var severity = replacement.Severity ?? wound.Comp.Severity;
        if (_wounds.CreateOrMergeWound(args.Part, id, severity) is { } closed)
            _bleeding.SetTreatment(closed, BleedingTreatment.Sutured);
    }

    /// <summary>
    /// The lock and the open-belly state live exactly as long as the wound does, however it went: the
    /// surgery, a rejuvenate or a limb that stopped existing all end up here.
    /// </summary>
    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        if (args.Kind != WolfmedWoundLifecycle.Removed ||
            !TryComp(args.Part, out WolfmedEviscerationComponent? evisceration) ||
            evisceration.Wound != args.Prototype)
            return;

        RemComp<WolfmedEviscerationComponent>(args.Part);

        if (TerminatingOrDeleted(args.Part))
            return;

        // Only what this system granted. A medic who cut the patient open properly keeps their incision.
        if (evisceration.GrantedIncision)
            RemComp<IncisionOpenComponent>(args.Part);

        if (evisceration.GrantedRetraction)
            RemComp<SkinRetractedComponent>(args.Part);
    }

    private bool Roll(float chance)
    {
        var clamped = Math.Clamp(chance, 0f, 1f);
        return ForcedRoll is { } forced ? forced < clamped : _random.Prob(clamped);
    }
}
