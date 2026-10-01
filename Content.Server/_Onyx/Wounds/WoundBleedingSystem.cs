using System.Linq;
using Content.Shared.CCVar;
using Content.Shared.Body;
using Content.Server.Body.Components; // WOLFGATE(Wolfmed): D13, BloodstreamComponent is server-only in Wolfgate.
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Server.Body.Systems; // WOLFGATE(Wolfmed): D13, BloodstreamSystem is server-only in Wolfgate.
using Content.Shared.Bed.Sleep;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared._Onyx.Chemistry.Circulation;

namespace Content.Shared._Onyx.Wounds;

public sealed partial class WoundBleedingSystem : EntitySystem
{
    private static readonly ProtoId<WoundPrototype> SystemicBleedingWound = "SystemicBleedingWound";

    [Dependency] private Content.Server._WF.Wolfmed.Wounds.WolfmedInfectionSystem _wfInfection = default!; // WOLFGATE(Wolfmed)
    [Dependency] private Content.Server._WF.Wolfmed.Wounds.WolfmedCauterySystem _wfCautery = default!; // WOLFGATE(Wolfmed): playtest 1
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private CirculatoryStreamSystem _circulation = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WoundSystem _wounds = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundBleedingComponent, ComponentInit>(OnBleedingInit);
        SubscribeLocalEvent<WoundBleedingComponent, ComponentShutdown>(OnBleedingShutdown);
        SubscribeLocalEvent<WoundBleedingComponent, WoundCreatedEvent>(OnWoundCreated);
        SubscribeLocalEvent<WoundBleedingComponent, WoundChangedEvent>(OnWoundChanged);
        SubscribeLocalEvent<WoundBleedingComponent, WoundStateChangedEvent>(OnWoundStateChanged);
        SubscribeLocalEvent<WoundBleedingComponent, WoundRemovedEvent>(OnWoundRemoved);
        SubscribeLocalEvent<WoundHostComponent, SleepStateChangedEvent>(OnSleepStateChanged);
    }

    private void OnBleedingShutdown(Entity<WoundBleedingComponent> wound, ref ComponentShutdown args)
    {
        // Shutdown runs before the component is removed from entity queries. Clear its contribution
        // first, or healing below the bleeding threshold leaves a cached bleed with no wound source.
        wound.Comp.CurrentRate = 0f;
        if (TryComp(wound, out WoundComponent? core))
            RefreshBodyForPart(core.HoldingPart);
    }

    // WOLFGATE(Wolfmed) START: a bleed that starts on an old wound under a tourniquet is tied off as well
    // private void OnBleedingInit(Entity<WoundBleedingComponent> wound, ref ComponentInit args) => RestartAutomaticClotting(wound);
    private void OnBleedingInit(Entity<WoundBleedingComponent> wound, ref ComponentInit args)
    {
        TieOffIfStrapped(wound);
        RestartAutomaticClotting(wound);
    }
    // WOLFGATE END
    private void OnWoundCreated(Entity<WoundBleedingComponent> wound, ref WoundCreatedEvent args)
    {
        // WOLFGATE(Wolfmed): playtest 4: a bleed opening on a part under a tourniquet (or below one) is tied off
        // already; the strap covers the whole limb, not just the wounds it found when it went on.
        if (TryComp(wound, out WoundComponent? core) && IsTiedOff(core.HoldingPart) &&
            _traits.CanTourniquet(wound.Owner, core.HoldingPart))
            wound.Comp.Treatment = BleedingTreatment.Clamped;

        RestartAutomaticClotting(wound);
    }
    private void OnWoundChanged(Entity<WoundBleedingComponent> wound, ref WoundChangedEvent args)
    {
        if (args.Severity > args.OldSeverity && !_wfInfection.ApplyingCreep) // WOLFGATE(Wolfmed): infection creep is not a new injury, it must not strip the dressing
        {
            wound.Comp.BleedingSeverity += args.Severity - args.OldSeverity;
            // WOLFGATE(Wolfmed): playtest 4: a tourniquet stays tied whatever the wound does under it (the strap's own
            // Blunt was undoing the clamp it had just put on); every other dressing still comes off a wound that reopens.
            if (wound.Comp.Treatment != BleedingTreatment.Clamped ||
                !TryComp(wound, out WoundComponent? changedCore) || !IsTiedOff(changedCore.HoldingPart))
                wound.Comp.Treatment = BleedingTreatment.None;
            RestartAutomaticClotting(wound);
        }
        else
            RecomputeAutomaticClotting(wound);
    }

    private void OnWoundStateChanged(Entity<WoundBleedingComponent> wound, ref WoundStateChangedEvent args)
    {
        if (args.State == WoundState.Open)
            RestartAutomaticClotting(wound);
        else
        {
            wound.Comp.AutomaticClottingStartedAt = null;
            wound.Comp.AutomaticClottingAt = null;
            RefreshWound(wound);
        }
    }

    private void OnWoundRemoved(Entity<WoundBleedingComponent> wound, ref WoundRemovedEvent args) => RefreshBodyForPart(args.Part);

    private void OnSleepStateChanged(Entity<WoundHostComponent> body, ref SleepStateChangedEvent args)
    {
        foreach (var wound in GetAttachedBleedingWounds(body))
            RefreshWound((wound.Owner, wound.Comp2), false);
        RefreshBody(body);
    }

    internal void HandlePartDamageApplied(Entity<WoundableComponent> part, ref PartDamageAppliedEvent args)
    {
        if (!_net.IsServer || !TryComp(args.Body, out BloodstreamComponent? bloodstream) ||
            !_prototypes.TryIndex(bloodstream.DamageBleedModifiers, out var modifiers))
            return;

        var reduction = -DamageSpecifier.ApplyModifierSet(
                DamageSpecifier.GetPositive(args.Damage),
                modifiers)
            .GetTotal();
        if (reduction <= FixedPoint2.Zero || GetPartRate(part.AsNullable()) <= 0f ||
            !ReducePartBleeding(part.AsNullable(), reduction))
            return;

        if (-reduction.Float() <= bloodstream.BloodHealedSoundThreshold &&
            _wfCautery.TryAnnounceWoundsClosing(args.Body)) // WOLFGATE(Wolfmed): playtest 1: once per cooldown, not every vacuum tick
        {
            _popup.PopupEntity(Loc.GetString("bloodstream-component-wounds-cauterized"), args.Body, args.Body,
                PopupType.Medium);
            _audio.PlayPredicted(bloodstream.BloodHealedSound, args.Body, args.Origin);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        var query = EntityQueryEnumerator<WoundBleedingComponent, WoundComponent>();
        while (query.MoveNext(out var uid, out var bleeding, out var wound))
        {
            if (bleeding.AutomaticClottingAt is not { } deadline || _timing.CurTime < deadline)
                continue;

            bleeding.AutomaticClottingStartedAt = null;
            bleeding.AutomaticClottingAt = null;
            bleeding.NaturalClotting = Math.Max(bleeding.NaturalClotting, bleeding.BaseRate);
            if (_prototypes.TryIndex(wound.Prototype, out var prototype))
                RefreshWound((uid, bleeding), wound, prototype);
        }
    }

    public bool SetTreatment(Entity<WoundComponent?> wound, BleedingTreatment treatment)
    {
        if (!_net.IsServer || !Resolve(wound, ref wound.Comp, false) ||
            !TryComp(wound, out WoundBleedingComponent? bleeding))
            return false;

        bleeding.Treatment = treatment;
        RefreshWound((wound, bleeding));
        return true;
    }

    public bool ReduceBleeding(Entity<WoundComponent?> wound, FixedPoint2 amount, bool dressing = false) // WOLFGATE(Wolfmed): dressing
    {
        if (!_net.IsServer || amount <= FixedPoint2.Zero || !Resolve(wound, ref wound.Comp, false) ||
            !TryComp(wound, out WoundBleedingComponent? bleeding))
            return false;

        bleeding.BleedingSeverity = FixedPoint2.Max(FixedPoint2.Zero, bleeding.BleedingSeverity - amount);
        // WOLFGATE(Wolfmed): gauze that stops a bleed stays on the wound as a dressing. Removing the component here let the
        // next severity change re-roll the bleed at full strength, and left nothing for the limb overlay to show.
        if (dressing)
        {
            bleeding.Treatment = BleedingTreatment.Bandaged;
            RefreshWound((wound, bleeding));
            return true;
        }

        if (bleeding.BleedingSeverity == FixedPoint2.Zero)
        {
            RemComp<WoundBleedingComponent>(wound.Owner);
            RefreshBodyForPart(wound.Comp.HoldingPart);
            return true;
        }

        bleeding.Treatment = BleedingTreatment.None;
        RefreshWound((wound, bleeding));
        return true;
    }

    public bool ModifyBodyBleeding(EntityUid body, float amount)
    {
        if (!_net.IsServer || amount == 0f || !HasComp<WoundHostComponent>(body))
            return false;

        if (amount > 0f)
        {
            foreach (var wound in GetAttachedBleedingWounds(body))
            {
                if (wound.Comp1.Prototype != SystemicBleedingWound)
                    continue;

                return _wounds.CreateOrMergeWound(wound.Comp1.HoldingPart,
                    SystemicBleedingWound,
                    FixedPoint2.New(amount)) != null;
            }

            foreach (var (part, _) in _body.GetBodyChildren(body))
            {
                if (_wounds.CanBleed(part) && _wounds.CanCreateWound(part, SystemicBleedingWound))
                {
                    return _wounds.CreateOrMergeWound(part, SystemicBleedingWound, FixedPoint2.New(amount)) != null;
                }
            }

            return false;
        }

        var wounds = GetAttachedBleedingWounds(body)
            .Where(wound => wound.Comp2.CurrentRate > 0f)
            .OrderByDescending(wound => wound.Comp2.CurrentRate)
            .ToArray();
        var remaining = FixedPoint2.New(-amount);
        var modified = false;
        foreach (var wound in wounds)
        {
            var rate = FixedPoint2.New(wound.Comp2.CurrentRate);
            var reduction = remaining >= rate
                ? wound.Comp2.BleedingSeverity
                : wound.Comp2.BleedingSeverity * remaining /
                  FixedPoint2.New(wound.Comp2.CurrentRate + wound.Comp2.NaturalClotting);
            if (reduction <= FixedPoint2.Zero || !ReduceBleeding(wound.Owner, reduction))
                continue;

            modified = true;
            remaining -= FixedPoint2.Min(remaining, rate);
            if (remaining <= FixedPoint2.Zero)
                break;
        }

        return modified;
    }

    public bool StopBodyBleeding(EntityUid body)
    {
        if (!_net.IsServer || !HasComp<WoundHostComponent>(body))
            return false;

        var modified = false;
        foreach (var wound in GetAttachedBleedingWounds(body).ToArray())
            modified |= ReduceBleeding(wound.Owner, wound.Comp2.BleedingSeverity);

        return modified;
    }

    public bool ReducePartBleeding(Entity<WoundableComponent?> part, FixedPoint2 amount, bool dressing = false) // WOLFGATE(Wolfmed): dressing
    {
        if (!_net.IsServer || amount <= FixedPoint2.Zero || !Resolve(part, ref part.Comp, false))
            return false;

        var wounds = _wounds.GetWounds(part)
            .Select(wound => (Wound: wound, Bleeding: CompOrNull<WoundBleedingComponent>(wound)))
            // WOLFGATE(Wolfmed): W2: an arterial bleed does not give up severity to a dressing.
            .Where(entry => entry.Bleeding is { CurrentRate: > 0f } && AllowsTopicalBleedReduction(entry.Wound))
            .OrderByDescending(entry => entry.Bleeding!.CurrentRate)
            .ToArray();
        var remaining = amount;
        var modified = false;
        foreach (var (wound, bleeding) in wounds)
        {
            var rate = FixedPoint2.New(bleeding!.CurrentRate);
            var reduction = remaining >= rate
                ? bleeding.BleedingSeverity
                : bleeding.BleedingSeverity * remaining /
                  FixedPoint2.New(bleeding.CurrentRate + bleeding.NaturalClotting);
            if (reduction <= FixedPoint2.Zero || !ReduceBleeding(wound.Owner, reduction, dressing)) // WOLFGATE(Wolfmed): dressing
                continue;

            modified = true;
            remaining -= FixedPoint2.Min(remaining, rate);
            if (remaining <= FixedPoint2.Zero)
                break;
        }

        return modified;
    }

    public int TreatPart(Entity<WoundableComponent?> part, BleedingTreatment treatment,
        ProtoId<WoundPrototype>? prototype = null)
    {
        if (!Resolve(part, ref part.Comp, false))
            return 0;

        var treated = 0;
        foreach (var wound in _wounds.GetWounds(part).ToArray())
        {
            if ((prototype == null || wound.Comp.Prototype == prototype) && SetTreatment(wound.Owner, treatment))
                treated++;
        }

        return treated;
    }

    public float GetPartRate(Entity<WoundableComponent?> part)
    {
        if (!Resolve(part, ref part.Comp, false))
            return 0f;

        var rate = 0f;
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (TryComp(wound, out WoundBleedingComponent? bleeding))
                rate += bleeding.CurrentRate;
        }

        return rate;
    }

    public void OnPartChanged(EntityUid body) => RefreshBody(body);

    public void OnPartInserted(EntityUid part, EntityUid body) => RefreshBody(body, part);

    public void RefreshBody(EntityUid body, EntityUid? insertedPart = null)
    {
        if (!_net.IsServer || !HasComp<WoundHostComponent>(body) ||
            !TryComp(body, out BloodstreamComponent? bloodstream))
            return;

        var partRates = new Dictionary<EntityUid, float>();
        var streamRates = new Dictionary<ProtoId<CirculatoryStreamPrototype>, float>();
        foreach (var wound in GetAttachedBleedingWounds(body))
        {
            var part = wound.Comp1.HoldingPart;
            partRates[part] = partRates.GetValueOrDefault(part) + wound.Comp2.CurrentRate;
            if (TryComp(part, out WoundableComponent? woundable))
            {
                var stream = _circulation.GetPartStream((part, woundable));
                streamRates[stream] = streamRates.GetValueOrDefault(stream) + wound.Comp2.CurrentRate;
            }
        }

        if (insertedPart is { } inserted && TryComp(inserted, out WoundableComponent? insertedWoundable) &&
            !partRates.ContainsKey(inserted))
        {
            partRates[inserted] = 0f;
            foreach (var wound in _wounds.GetWounds((inserted, insertedWoundable)))
                if (TryComp(wound, out WoundBleedingComponent? bleeding))
                {
                    partRates[inserted] += bleeding.CurrentRate;
                    var stream = _circulation.GetPartStream((inserted, insertedWoundable));
                    streamRates[stream] = streamRates.GetValueOrDefault(stream) + bleeding.CurrentRate;
                }
        }

        // WOLFGATE(Wolfmed): BRAIN: cardiac arrest drops passive bleeding to a trickle. Applied to the body's
        // cached rates rather than to each wound, so the arrest never has to re-derive every wound.
        if (HasComp<Content.Shared._WF.Wolfmed.Life.WolfmedCardiacArrestComponent>(body))
        {
            foreach (var stream in streamRates.Keys.ToArray())
                streamRates[stream] *= Content.Server._WF.Wolfmed.Life.WolfmedLifeSystem.ArrestBleedFactor;
        }

        _circulation.SetBleedRates(body, streamRates);
        foreach (var (part, partRate) in partRates)
        {
            var partChanged = new PartBleedingChangedEvent(body, part, partRate);
            RaiseLocalEvent(part, ref partChanged);
        }

    }

    public void RefreshWound(Entity<WoundBleedingComponent> wound, bool refreshBody = true)
    {
        if (!_net.IsServer || !TryComp(wound, out WoundComponent? core) ||
            !_prototypes.TryIndex(core.Prototype, out var prototype))
            return;

        RefreshWound(wound, core, prototype, refreshBody);
    }

    private void RefreshWound(Entity<WoundBleedingComponent> wound,
        WoundComponent core,
        WoundPrototype prototype,
        bool refreshBody = true)
    {
        if (!_net.IsServer || !prototype.TryGetBehavior(core.Severity, out WoundBleedingBehavior behavior) ||
            core.Severity < behavior.MinimumSeverity ||
            !TryGetBleedingMultiplier(core.HoldingPart, out var bleedingMultiplier))
        {
            wound.Comp.BaseRate = 0f;
            wound.Comp.CurrentRate = 0f;
            Dirty(wound);
            if (refreshBody)
                RefreshBodyForPart(core.HoldingPart);
            return;
        }

        wound.Comp.BaseRate = wound.Comp.BleedingSeverity.Float() * behavior.Rate * bleedingMultiplier *
                              _configuration.GetCVar(Content.Shared._WF.Wolfmed.CCVar.WolfmedCVars.BleedRate); // WOLFGATE(Wolfmed): one knob for every bleed
        if (behavior.AwakeMultiplier > 1f && TryGetBody(core.HoldingPart, out var patient) &&
            !HasComp<SleepingComponent>(patient))
            wound.Comp.BaseRate *= behavior.AwakeMultiplier;
        // WOLFGATE(Wolfmed): W2: an arterial bleed answers to its own treatment table; see WoundBleedingSystem.Wolfmed.
        var multiplier = core.State == WoundState.Open ? GetTreatmentMultiplier(wound.Owner, wound.Comp.Treatment) : 0f;
        wound.Comp.CurrentRate = Math.Max(0f, wound.Comp.BaseRate * multiplier - wound.Comp.NaturalClotting);
        Dirty(wound);

        if (!refreshBody || !TryGetBody(core.HoldingPart, out var body))
            return;

        var changed = new WoundBleedingChangedEvent(body, core.HoldingPart, wound, wound.Comp.CurrentRate);
        RaiseLocalEvent(wound, ref changed);
        RefreshBody(body);
    }

    private bool TryGetBleedingMultiplier(EntityUid part, out float multiplier)
    {
        multiplier = 1f;
        if (!TryComp(part, out WoundableComponent? woundable) ||
            !_prototypes.TryIndex(woundable.Profile, out var profile))
            return true;

        if (profile.BleedingMultiplier <= 0f)
            return false;

        multiplier = profile.BleedingMultiplier;
        return true;
    }

    private void RestartAutomaticClotting(Entity<WoundBleedingComponent> wound)
    {
        wound.Comp.NaturalClotting = 0f;
        wound.Comp.AutomaticClottingStartedAt = _timing.CurTime;
        RecomputeAutomaticClotting(wound);
    }

    private void RecomputeAutomaticClotting(Entity<WoundBleedingComponent> wound)
    {
        if (!_net.IsServer || !TryComp(wound, out WoundComponent? core) ||
            !_prototypes.TryIndex(core.Prototype, out var prototype) ||
            !prototype.TryGetBehavior(core.Severity, out WoundBleedingBehavior behavior))
            return;

        var secondsPerSeverity = _configuration.GetCVar(CCVars.WoundsBleedingAutoStopSecondsPerSeverity);
        var maximum = _configuration.GetCVar(CCVars.WoundsBleedingAutoStopMaxSeconds);
        if (core.State != WoundState.Open || !_configuration.GetCVar(CCVars.WoundsBleedingAutoStopEnabled) ||
            secondsPerSeverity <= 0f || maximum <= 0f || behavior.ClottingMultiplier <= 0f)
        {
            wound.Comp.AutomaticClottingStartedAt = null;
            wound.Comp.AutomaticClottingAt = null;
            RefreshWound(wound, core, prototype);
            return;
        }

        var minimum = Math.Clamp(_configuration.GetCVar(CCVars.WoundsBleedingAutoStopMinSeconds), 0f, maximum);
        var duration = Math.Clamp(core.Severity.Float() * secondsPerSeverity * behavior.ClottingMultiplier,
            minimum,
            maximum);
        wound.Comp.AutomaticClottingStartedAt ??= _timing.CurTime;
        wound.Comp.AutomaticClottingAt = wound.Comp.AutomaticClottingStartedAt + TimeSpan.FromSeconds(duration);

        if (_timing.CurTime >= wound.Comp.AutomaticClottingAt)
        {
            wound.Comp.AutomaticClottingStartedAt = null;
            wound.Comp.AutomaticClottingAt = null;
            var rate = wound.Comp.BleedingSeverity.Float() * behavior.Rate;
            if (behavior.AwakeMultiplier > 1f && TryGetBody(core.HoldingPart, out var patient) &&
                !HasComp<SleepingComponent>(patient))
                rate *= behavior.AwakeMultiplier;
            wound.Comp.NaturalClotting = Math.Max(wound.Comp.NaturalClotting, rate);
        }

        RefreshWound(wound, core, prototype);
    }

    private IEnumerable<Entity<WoundComponent, WoundBleedingComponent>> GetAttachedBleedingWounds(EntityUid body)
    {
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (!TryComp(part, out WoundableComponent? woundable))
                continue;

            foreach (var wound in _wounds.GetWounds((part, woundable)))
                if (TryComp(wound, out WoundBleedingComponent? bleeding))
                    yield return (wound, wound.Comp, bleeding);
        }
    }

    private void RefreshBodyForPart(EntityUid part)
    {
        if (TryGetBody(part, out var body))
            RefreshBody(body, part);
    }

    private bool TryGetBody(EntityUid part, out EntityUid body)
    {
        body = default;
        if (!TryComp(part, out BodyPartComponent? partComp) || partComp.Body is not { } attachedBody)
            return false;

        body = attachedBody;
        return true;
    }

    private static float TreatmentMultiplier(BleedingTreatment treatment) => treatment switch
    {
        BleedingTreatment.None => 1f,
        BleedingTreatment.Bandaged => 0.25f,
        BleedingTreatment.Clamped => 0f,
        BleedingTreatment.Sutured => 0f,
        BleedingTreatment.Cauterized => 0f,
        _ => 1f,
    };
}
