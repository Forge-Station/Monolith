using System.Linq;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Alert;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory; // Playtest 5
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>
/// Wound infection, from a contaminated cut through an infected limb to a body dying of sepsis and septic shock.
/// </summary>
/// <remarks>
/// One tick walks only wounds that are already contaminated, parts that are already infected and bodies that are
/// already septic, so the cost is in injuries rather than in players. Everything here is server-side: wound
/// creation, damage, temperature and pain all are. <see cref="Update"/> advances by whatever time has accumulated,
/// so a test can hand it ten minutes in one call; an infection moves one part per call.
/// </remarks>
// A wound with WolfmedInfectionRiskBehavior carries WolfmedInfectionComponent and gains progress on a slow batched
// tick, scaled by that risk, by what has been done to the wound and by whether something dirty has been in it. Past
// the profile's thresholds it goes local (hurts and widens), then spreading (feverish). INFECTION: a spreading wound
// infects its part (WolfmedPartInfectionComponent), a spreading part infects its parent, towards the torso, and only
// an infected torso or head feeds WolfmedSepsisComponent on the body, the stage that kills. Neither stage deals
// Poison; the toxin load is its own route. Playtest 5: septic shock is a consciousness pressure (the patient is out,
// not dying) and the organ damage past wolfmed.sepsis_organ_damage_from is the only death in it: the lungs fail and
// the brain starves.
public sealed class WolfmedInfectionSystem : EntitySystem
{
    /// <summary>The shipped profile. A downstream server retunes the prototype, not this file.</summary>
    public const string DefaultProfile = "WFWolfmedDefaultInfection";

    /// <summary>Alert shown while the patient is septic. Severity 0 is sepsis, 1 septic shock.</summary>
    public static readonly ProtoId<AlertPrototype> SepsisAlert = "WFWolfmedSepsis";

    /// <summary>Playtest 5: the consciousness pressure septic shock holds at full. Unconscious, never merely Downed.</summary>
    public const string ShockPressure = "septic-shock";

    /// <summary>INFECTION: a part's infection runs 0 to this; at it the part is Septic.</summary>
    public const float PartSepticAt = 100f;

    /// <summary>Seconds between batches. Nothing in the model needs finer resolution than this.</summary>
    private const float TickSeconds = 5f;

    [Dependency] private Content.Shared.Mobs.Systems.MobStateSystem _mobState = default!;
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private PainSystem _pain = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TemperatureSystem _temperature = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private Life.WolfmedBodyTemperatureSystem _bodyTemperature = default!; // M5
    [Dependency] private OrganHealthSystem _organs = default!; // Playtest 4 (SEPSIS)
    [Dependency] private Consciousness.WolfmedConsciousnessSystem _consciousness = default!; // Playtest 5
    [Dependency] private InventorySystem _inventory = default!; // Playtest 5

    private float _accumulator;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
        SubscribeLocalEvent<WoundHostComponent, WolfmedCleanWoundsEvent>(OnClean);
        SubscribeLocalEvent<WoundHostComponent, WolfmedAntibioticEvent>(OnAntibiotic);
        SubscribeLocalEvent<WolfmedSepsisComponent, ComponentShutdown>(OnSepsisShutdown);
    }

    /// <summary>True while the infection tick is widening a wound, which is not an injury.</summary>
    public bool ApplyingCreep { get; private set; }

    /// <summary>The profile every timer in the model comes from.</summary>
    public WolfmedInfectionProfilePrototype Profile => _prototypes.Index<WolfmedInfectionProfilePrototype>(DefaultProfile);

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        // WOLFGATE (W6): a chassis does not go septic. The gate is the part profile rather than the wound,
        // so a wound that can be infected in flesh (an embedded fragment, a surgical incision) is simply
        // inert on an IPC or a cybernetic limb, and no mechanical wound has to opt out one at a time.
        if (args.Kind == WolfmedWoundLifecycle.Removed || TerminatingOrDeleted(args.Wound) ||
            !_traits.IsOrganic(args.Part) ||
            !_prototypes.TryIndex(args.Prototype, out var prototype) ||
            !prototype.TryGetBehavior(args.Severity, out WolfmedInfectionRiskBehavior behavior) ||
            behavior.RiskMultiplier <= 0f)
            return;

        EnsureComp<WolfmedInfectionComponent>(args.Wound);
    }

    private void OnClean(Entity<WoundHostComponent> body, ref WolfmedCleanWoundsEvent args)
    {
        args.Cleaned += Clean(body);
    }

    private void OnAntibiotic(Entity<WoundHostComponent> body, ref WolfmedAntibioticEvent args)
    {
        args.Treated |= Treat(body, args.Units);
    }

    /// <summary>
    /// M2 (P20): a suture went into this part. Every open wound it treats (its prototype shares a damage type with
    /// <paramref name="types"/>, or any wound when that is null) counts as sutured for infection. Returns how many.
    /// </summary>
    public int MarkSutured(EntityUid part, IReadOnlyCollection<string>? types)
    {
        if (!TryComp(part, out WoundableComponent? woundable))
            return 0;

        var marked = 0;
        foreach (var wound in _wounds.GetWounds((part, woundable)))
        {
            if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
                !_prototypes.TryIndex(wound.Comp.Prototype, out var prototype) ||
                types != null && !prototype.DamageTypes.Keys.Any(type => types.Contains(type.Id)))
                continue;

            EnsureComp<WolfmedSuturedComponent>(wound).TreatedSeverity = wound.Comp.Severity;
            marked++;
        }

        return marked;
    }

    /// <summary>
    /// Playtest 5: something dirty closed wounds on this part (<see cref="WolfmedDirtyTreatmentComponent"/>, the makeshift
    /// suture). Every open wound it treats, chosen as <see cref="MarkSutured"/> chooses, that can go bad is contaminated.
    /// Returns how many.
    /// </summary>
    public int ContaminateTreated(EntityUid part, IReadOnlyCollection<string>? types)
    {
        if (!TryComp(part, out WoundableComponent? woundable))
            return 0;

        var contaminated = 0;
        foreach (var wound in _wounds.GetWounds((part, woundable)).ToArray())
        {
            if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
                !HasComp<WolfmedInfectionComponent>(wound) ||
                !_prototypes.TryIndex(wound.Comp.Prototype, out var prototype) ||
                types != null && !prototype.DamageTypes.Keys.Any(type => types.Contains(type.Id)))
                continue;

            Contaminate(wound);
            contaminated++;
        }

        return contaminated;
    }

    /// <summary>
    /// The Sutured openness for a sutured wound, or null for one that is not, or whose suture no longer holds because
    /// the wound grew wolfmed.suture_treatment_lost_severity past it (the marker goes then).
    /// </summary>
    private float? SuturedOpenness(EntityUid wound, WoundComponent core, WolfmedInfectionProfilePrototype profile)
    {
        if (!TryComp(wound, out WolfmedSuturedComponent? sutured))
            return null;

        if (core.Severity - sutured.TreatedSeverity >= FixedPoint2.New(_config.GetCVar(WolfmedCVars.SutureTreatmentLostSeverity)))
        {
            RemComp<WolfmedSuturedComponent>(wound);
            return null;
        }

        return profile.TreatmentMultipliers.GetValueOrDefault(BleedingTreatment.Sutured, 0f);
    }

    private void OnSepsisShutdown(Entity<WolfmedSepsisComponent> body, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(body))
            return;

        _alerts.ClearAlert(body, SepsisAlert);
        _consciousness.SetExternalPressure(body, ShockPressure, 0f); // Playtest 5
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < TickSeconds)
            return;

        var elapsed = _accumulator;
        _accumulator = 0f;

        if (!_config.GetCVar(WolfmedCVars.InfectionEnabled))
            return;

        var minutes = elapsed / 60f * _config.GetCVar(WolfmedCVars.InfectionRate);
        var profile = Profile;

        // Every body running a fever this tick, so each gets one rise however many sources it has.
        var fevered = new HashSet<EntityUid>();

        // Buffered: a tick removes the component, and its damage can create wounds that gain one.
        var dueWounds = new List<Entity<WolfmedInfectionComponent, WoundComponent>>();
        var wounds = EntityQueryEnumerator<WolfmedInfectionComponent, WoundComponent>();
        while (wounds.MoveNext(out var uid, out var infection, out var wound))
            dueWounds.Add((uid, infection, wound));

        foreach (var due in dueWounds)
        {
            if (!TerminatingOrDeleted(due) && HasComp<WolfmedInfectionComponent>(due))
                TickWound(due, profile, minutes, fevered);
        }

        TickParts(profile, minutes, fevered);
        SeedSepsis();

        var dueSepsis = new List<Entity<WolfmedSepsisComponent>>();
        var sepsis = EntityQueryEnumerator<WolfmedSepsisComponent>();
        while (sepsis.MoveNext(out var uid, out var component))
            dueSepsis.Add((uid, component));

        foreach (var due in dueSepsis)
        {
            if (!TerminatingOrDeleted(due) && HasComp<WolfmedSepsisComponent>(due))
                TickSepsis(due, profile, minutes, fevered);
        }

        foreach (var body in fevered)
        {
            if (!TerminatingOrDeleted(body))
                Fever(body, profile, minutes);
        }
    }

    private void TickWound(Entity<WolfmedInfectionComponent, WoundComponent> wound,
        WolfmedInfectionProfilePrototype profile,
        float minutes,
        HashSet<EntityUid> fevered)
    {
        var (infection, core) = (wound.Comp1, wound.Comp2);
        var part = core.HoldingPart;

        // A closed wound has nothing left to infect. The contamination goes with it.
        if (TerminatingOrDeleted(part) || core.State is WoundState.Healed or WoundState.Scarred)
        {
            RemComp<WolfmedInfectionComponent>(wound);
            return;
        }

        var body = CompOrNull<BodyPartComponent>(part)?.Body;
        var treatment = TryComp(wound, out WoundBleedingComponent? bleeding)
            ? bleeding.Treatment
            : BleedingTreatment.None;
        var openness = profile.TreatmentMultipliers.GetValueOrDefault(treatment, 1f);
        // M1b (P20): a burn has no bleed to bandage; its dressing is the treatment.
        if (treatment == BleedingTreatment.None && HasComp<WolfmedDressedComponent>(wound))
            openness = profile.DressedMultiplier;

        // M2 (P20): a sutured wound is closed at the profile's Sutured rate, whatever its bleeding says.
        if (SuturedOpenness(wound, core, profile) is { } sutured)
            openness = MathF.Min(openness, sutured);

        if (infection.Cleaned && infection.Stage < WolfmedInfectionStage.Spreading)
            SetProgress(wound, infection.Progress - profile.CleanDecayPerMinute * minutes, profile);
        else if (HasReason(wound, part)) // Playtest 5: a wound with nothing to go bad from holds where it is
            SetProgress(wound,
                infection.Progress +
                profile.ProgressPerMinute * minutes * openness * infection.Contamination *
                _traits.GetInfectionRisk(wound.Owner),
                profile);

        if (infection.Progress <= 0f)
            return;

        if (infection.Stage >= WolfmedInfectionStage.Local)
        {
            _pain.ChangePain(part, profile.LocalPainPerMinute * minutes);

            // "Slower healing", expressed as the wound reopening: treatment has to outrun the infection
            // rather than be cancelled by it. Capped so an ignored cut cannot widen forever.
            var creep = FixedPoint2.Min(profile.SeverityPerMinute * minutes,
                profile.MaxSeverityAdded - infection.SeverityAdded);
            if (creep > FixedPoint2.Zero)
            {
                // Flagged so the bleeding system does not read it as a fresh hit: that stripped the dressing
                // off an infected wound every tick, so gauze never held.
                ApplyingCreep = true;
                try
                {
                    if (_wounds.ChangeSeverity(wound.Owner, creep))
                        infection.SeverityAdded += creep;
                }
                finally
                {
                    ApplyingCreep = false;
                }
            }
        }

        // A corpse does not run a fever. M5 (OD13): a spreading infection no longer deals Poison; toxins are their own
        // route, and sepsis has its own brain drain. INFECTION: a septic wound no longer starts sepsis; it infects its
        // part (TickParts), and only the torso and head feed sepsis. Playtest 5: nothing the body feels starts before
        // the infection has reached the chest or the head, so a limb's wound runs no fever either.
        if (infection.Stage >= WolfmedInfectionStage.Spreading && body is { } host && !_mobState.IsDead(host) &&
            TryComp(part, out BodyPartComponent? holder) && IsCore(holder))
            fevered.Add(host);
    }

    /// <summary>
    /// Playtest 5, "an infection has to have an actual reason to start": whether this wound has one. Something dirty
    /// went into it, or it is dirty by nature (<see cref="WolfmedInfectionRiskBehavior.Dirty"/>), or it is open to the
    /// air: not under a sealed suit, and more than a minor wound. Off with wolfmed.infection_needs_reason, every open
    /// wound is a reason.
    /// </summary>
    public bool HasReason(Entity<WolfmedInfectionComponent, WoundComponent> wound, EntityUid part)
    {
        if (!_config.GetCVar(WolfmedCVars.InfectionNeedsReason) || wound.Comp1.Contamination > 1f)
            return true;

        if (_traits.TryGetBehavior(wound.Owner, out WolfmedInfectionRiskBehavior behavior) && behavior.Dirty)
            return true;

        // Playtest 5, "minor injuries shouldn't cause infections": a scald or a scratch left in the open went septic.
        if (_config.GetCVar(WolfmedCVars.InfectionSparesMinor) && IsMinor(wound.Comp2))
            return false;

        return IsExposed(part);
    }

    /// <summary>
    /// A wound the analyzer calls minor: its current stage is Minor. Charring, an operative incision and a stump have
    /// no such stage, so they are never minor.
    /// </summary>
    public bool IsMinor(WoundComponent wound) =>
        _prototypes.TryIndex(wound.Prototype, out var prototype) && prototype.GetStage(wound.Severity) == MinorStage;

    private const string MinorStage = "Minor";

    /// <summary>
    /// Playtest 5: whether the part is open to the air. A pressure-tight suit over it (a hardsuit or EVA suit in the
    /// outer slot; for the head, its helmet) seals it; a jumpsuit or a vest does not. A part off the body is not.
    /// </summary>
    public bool IsExposed(EntityUid part)
    {
        if (CompOrNull<BodyPartComponent>(part)?.Body is not { } body)
            return false;

        var slot = CompOrNull<BodyPartComponent>(part)?.PartType == BodyPartType.Head ? "head" : "outerClothing";
        return !_inventory.TryGetSlotEntity(body, slot, out var worn) ||
               !HasComp<Content.Server.Atmos.Components.PressureProtectionComponent>(worn);
    }

    /// <summary>
    /// INFECTION: every part's own infection. A part is fed by each spreading wound on it (a septic one twice) and by
    /// each child part that is spreading or septic; a necrotic part is pinned at the top; an unfed part recovers.
    /// The stages are read as the pass starts, so an infection moves one part a tick whatever order they come in.
    /// </summary>
    private void TickParts(WolfmedInfectionProfilePrototype profile, float minutes, HashSet<EntityUid> fevered)
    {
        // Per minute per part; an infected part with nothing feeding it is in here at 0 and recovers.
        var feed = new Dictionary<EntityUid, float>();
        var pinned = new HashSet<EntityUid>();

        var spreading = new List<EntityUid>();
        var infected = EntityQueryEnumerator<WolfmedPartInfectionComponent>();
        while (infected.MoveNext(out var uid, out var infection))
        {
            feed.TryAdd(uid, 0f);
            if (infection.Stage >= profile.PartTransferStage) // Playtest 5: septic, not merely spreading
                spreading.Add(uid);
        }

        // Towards the torso only: a part feeds the part it hangs off, never its own children.
        foreach (var part in spreading)
        {
            if (!TerminatingOrDeleted(part) && _body.GetParentPartOrNull(part) is { } parent && CanCarry(parent))
                feed[parent] = feed.GetValueOrDefault(parent) + profile.PartSpreadPerMinute;
        }

        var wounds = EntityQueryEnumerator<WolfmedInfectionComponent, WoundComponent>();
        while (wounds.MoveNext(out _, out var infection, out var wound))
        {
            if (infection.Stage < WolfmedInfectionStage.Spreading ||
                wound.State is WoundState.Healed or WoundState.Scarred ||
                TerminatingOrDeleted(wound.HoldingPart) || !CanCarry(wound.HoldingPart))
                continue;

            var rate = profile.PartFromWoundPerMinute * (infection.Stage == WolfmedInfectionStage.Septic ? 2f : 1f);
            feed[wound.HoldingPart] = feed.GetValueOrDefault(wound.HoldingPart) + rate;
        }

        var necrotic = EntityQueryEnumerator<WolfmedNecrosisComponent>();
        while (necrotic.MoveNext(out var uid, out var necrosis))
        {
            if (!necrosis.Necrotic || TerminatingOrDeleted(uid) || !CanCarry(uid))
                continue;

            pinned.Add(uid);
            feed.TryAdd(uid, 0f);
        }

        foreach (var (part, perMinute) in feed)
        {
            if (TerminatingOrDeleted(part))
                continue;

            var current = CompOrNull<WolfmedPartInfectionComponent>(part)?.Progress ?? 0f;
            var progress = pinned.Contains(part) ? PartSepticAt
                : perMinute > 0f ? current + perMinute * minutes
                : current - profile.PartRecoveryPerMinute * minutes;

            if (SetPartProgress(part, progress, profile) is not { } stage ||
                !TryComp(part, out BodyPartComponent? bodyPart) || bodyPart.Body is not { } body)
                continue;

            // The part's own effects: it hurts from Local, as a local wound does. Playtest 5: the fever, like every
            // other thing the body feels, waits until the infection has reached the chest or the head.
            if (stage >= WolfmedInfectionStage.Local)
                _pain.ChangePain(part, profile.LocalPainPerMinute * minutes);

            if (stage >= WolfmedInfectionStage.Spreading && IsCore(bodyPart) && !_mobState.IsDead(body))
                fevered.Add(body);
        }
    }

    /// <summary>INFECTION: a living body whose torso or head is spreading or septic goes septic.</summary>
    private void SeedSepsis()
    {
        if (!_config.GetCVar(WolfmedCVars.SepsisEnabled))
            return;

        // Buffered, so nothing is added while the query is walked.
        var septic = new List<EntityUid>();
        var parts = EntityQueryEnumerator<WolfmedPartInfectionComponent, BodyPartComponent>();
        while (parts.MoveNext(out _, out var infection, out var part))
        {
            if (infection.Stage >= WolfmedInfectionStage.Spreading && IsCore(part) && part.Body is { } body)
                septic.Add(body);
        }

        foreach (var body in septic)
        {
            if (!TerminatingOrDeleted(body) && !_mobState.IsDead(body))
                EnsureComp<WolfmedSepsisComponent>(body);
        }
    }

    private void TickSepsis(Entity<WolfmedSepsisComponent> body,
        WolfmedInfectionProfilePrototype profile,
        float minutes,
        HashSet<EntityUid> fevered)
    {
        if (TerminatingOrDeleted(body))
            return;

        var sources = CountSources(body);
        var progress = Math.Clamp(body.Comp.Progress + (sources > 0
                ? profile.SepsisPerMinute * minutes * sources
                : -profile.SepsisRecoveryPerMinute * minutes),
            0f,
            100f);

        // Pinned at 100 is the common case for a patient nobody is treating; do not send that every tick.
        var shock = progress >= _config.GetCVar(WolfmedCVars.SepticShockAt);
        if (progress != body.Comp.Progress || shock != body.Comp.Shock)
        {
            body.Comp.Progress = progress;
            body.Comp.Shock = shock;
            Dirty(body);
        }

        if (body.Comp.Progress <= 0f)
        {
            RemComp<WolfmedSepsisComponent>(body);
            return;
        }

        // INFECTION: severity 1 is septic shock.
        _alerts.ShowAlert(body, SepsisAlert, (short) (shock ? 1 : 0));
        var dead = _mobState.IsDead(body);

        // Playtest 5: septic shock puts the patient out and holds them there, the way arrest does; nothing about it
        // drains the brain (wolfmed.brain_sepsis_seconds is 0). The organ damage below is what kills: the lungs fail
        // and the brain starves, about 15 minutes after the damage starts.
        _consciousness.SetExternalPressure(body, ShockPressure, shock && !dead ? 1f : 0f);
        if (dead)
            return;

        // M5 (OD13): sepsis deals no Poison.
        fevered.Add(body);

        // Playtest 4 (SEPSIS): past wolfmed.sepsis_organ_damage_from it eats the torso organs as well, the route for a
        // body whose brain is kept topped up while nobody treats the infection.
        if (DamagingOrgans(body))
            DamageOrgans(body, profile, minutes);
        else
            body.Comp.OrganDamageOwed.Clear();
    }

    /// <summary>
    /// Playtest 4 (SEPSIS): the body's sepsis is past wolfmed.sepsis_organ_damage_from and the damage is on. The
    /// analyzer's sepsis line and "Do first" read it. Never for the dead.
    /// </summary>
    public bool DamagingOrgans(EntityUid body) =>
        _config.GetCVar(WolfmedCVars.SepsisOrganDamagePerMinute) > 0f &&
        GetSepsis(body) >= _config.GetCVar(WolfmedCVars.SepsisOrganDamageFrom) &&
        !_mobState.IsDead(body);

    /// <summary>
    /// INFECTION: the body's sepsis is at or past wolfmed.septic_shock_at, the late stage. Playtest 5: its effects are
    /// <see cref="ShockPressure"/> (the patient is out) and the organ damage (wolfmed.sepsis_organ_damage_from); the
    /// brain drain behind wolfmed.arrest_sepsis is off unless a server sets wolfmed.brain_sepsis_seconds.
    /// </summary>
    public bool InSepticShock(EntityUid body) =>
        TryComp(body, out WolfmedSepsisComponent? sepsis) &&
        sepsis.Progress >= _config.GetCVar(WolfmedCVars.SepticShockAt);

    /// <summary>
    /// Takes the per-minute rate, times the profile's weight for each organ's slot, off every Wolfmed organ in the
    /// organic torso, through <see cref="OrganHealthSystem"/> so the bands and the analyzer's organ rows follow.
    /// </summary>
    private void DamageOrgans(Entity<WolfmedSepsisComponent> body, WolfmedInfectionProfilePrototype profile, float minutes)
    {
        var perMinute = _config.GetCVar(WolfmedCVars.SepsisOrganDamagePerMinute);

        // Buffered: an organ reaching zero raises its function change, which can move organs.
        var due = new List<(Entity<WolfmedOrganComponent> Organ, float Weight)>();
        foreach (var (part, bodyPart) in _body.GetBodyChildren(body))
        {
            if (bodyPart.PartType != BodyPartType.Torso || !_traits.IsOrganic(part))
                continue;

            foreach (var (organ, slotted) in _body.GetPartOrgans(part, bodyPart))
            {
                if (TryComp(organ, out WolfmedOrganComponent? health) && health.Health > FixedPoint2.Zero)
                    due.Add(((organ, health), profile.SepsisOrganWeights.GetValueOrDefault(slotted.SlotId, 1f)));
            }
        }

        var owed = body.Comp.OrganDamageOwed;
        foreach (var (organ, weight) in due)
        {
            if (weight <= 0f || TerminatingOrDeleted(organ))
                continue;

            var amount = owed.GetValueOrDefault(organ.Owner) + perMinute * weight * minutes;
            var taken = FixedPoint2.New(amount);
            owed[organ.Owner] = amount - taken.Float();
            if (taken > FixedPoint2.Zero)
                _organs.ChangeHealth(organ, -taken);
        }

        // A failed or removed organ owes nothing any more.
        foreach (var gone in owed.Keys.Where(uid => !due.Any(d => d.Organ.Owner == uid)).ToList())
            owed.Remove(gone);
    }

    /// <summary>
    /// Playtest 4 (SEPSIS): a fever is running, the state <see cref="Fever"/> keeps: the body is septic, or (playtest
    /// 5: on the torso or the head only) a wound or a part is spreading. An infected limb is a local matter until it
    /// gets there.
    /// </summary>
    public bool HasFever(EntityUid body)
    {
        if (GetSepsis(body) > 0f)
            return true;

        foreach (var part in Parts(body))
        {
            if (!TryComp(part, out BodyPartComponent? bodyPart) || !IsCore(bodyPart))
                continue;

            if (GetPartStage(part) >= WolfmedInfectionStage.Spreading ||
                GetWorstWoundStage(part) >= WolfmedInfectionStage.Spreading)
                return true;
        }

        return false;
    }

    /// <summary>INFECTION: the torso and the head that are spreading or septic. Nothing else feeds sepsis.</summary>
    private int CountSources(EntityUid body)
    {
        var sources = 0;
        foreach (var (part, component) in _body.GetBodyChildren(body))
        {
            if (IsCore(component) && GetPartStage(part) >= WolfmedInfectionStage.Spreading)
                sources++;
        }

        return sources;
    }

    /// <summary>INFECTION: the parts sepsis starts from.</summary>
    private static bool IsCore(BodyPartComponent part) => part.PartType is BodyPartType.Torso or BodyPartType.Head;

    /// <summary>INFECTION: a part that can hold an infection of its own: woundable living tissue.</summary>
    private bool CanCarry(EntityUid part) =>
        HasComp<BodyPartComponent>(part) && TryComp(part, out WoundableComponent? woundable) &&
        _traits.IsOrganic((part, woundable));

    private void Fever(EntityUid body, WolfmedInfectionProfilePrototype profile, float minutes)
    {
        // M5 (plan §3.10): never past the point where this species' heat starts to count; a fever never Downs.
        var ceiling = _bodyTemperature.FeverCeiling(body, profile.FeverTemperature);
        if (!TryComp(body, out TemperatureComponent? temperature) || temperature.CurrentTemperature >= ceiling)
            return;

        _temperature.ForceChangeTemperature(body,
            Math.Min(ceiling, temperature.CurrentTemperature + profile.FeverRise * minutes * 60f),
            temperature);
    }

    private void SetProgress(Entity<WolfmedInfectionComponent, WoundComponent> wound,
        float progress,
        WolfmedInfectionProfilePrototype profile)
    {
        var infection = wound.Comp1;
        var (oldProgress, oldStage, oldCleaned) = (infection.Progress, infection.Stage, infection.Cleaned);
        infection.Progress = Math.Clamp(progress, 0f, profile.SepsisAt);

        // Cleaning and antibiotics buy a reset, not immunity: an open wound that has been cleared starts
        // accumulating again from nothing.
        if (infection.Progress <= 0f)
        {
            infection.Cleaned = false;
            infection.SeverityAdded = FixedPoint2.Zero;
        }

        infection.Stage = infection.Progress switch
        {
            var value when value >= profile.SepsisAt => WolfmedInfectionStage.Septic,
            var value when value >= profile.SpreadingAt => WolfmedInfectionStage.Spreading,
            var value when value >= profile.LocalAt => WolfmedInfectionStage.Local,
            _ => WolfmedInfectionStage.None,
        };

        // Most wounds on a populated server sit at a pinned progress (sutured, or already cleaned), and
        // every one of them is in PVS for everyone who can see the patient. Only send a state that moved.
        if (infection.Progress != oldProgress || infection.Stage != oldStage || infection.Cleaned != oldCleaned)
            Dirty(wound.Owner, infection);
    }

    /// <summary>
    /// INFECTION: sets a part's infection, adding the component on the first progress and removing it at 0. Returns
    /// the stage, or null once the part is clear.
    /// </summary>
    private WolfmedInfectionStage? SetPartProgress(EntityUid part, float progress, WolfmedInfectionProfilePrototype profile)
    {
        progress = Math.Clamp(progress, 0f, PartSepticAt);
        if (progress <= 0f)
        {
            RemComp<WolfmedPartInfectionComponent>(part);
            return null;
        }

        var infection = EnsureComp<WolfmedPartInfectionComponent>(part);
        var stage = progress switch
        {
            >= PartSepticAt => WolfmedInfectionStage.Septic,
            var value when value >= profile.PartSpreadingAt => WolfmedInfectionStage.Spreading,
            var value when value >= profile.PartLocalAt => WolfmedInfectionStage.Local,
            _ => WolfmedInfectionStage.None,
        };

        // A necrotic limb sits pinned at the top; only send a state that moved.
        if (infection.Progress != progress || infection.Stage != stage)
        {
            infection.Progress = progress;
            infection.Stage = stage;
            Dirty(part, infection);
        }

        return stage;
    }

    private IEnumerable<EntityUid> Parts(EntityUid body)
    {
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (HasComp<WoundableComponent>(part))
                yield return part;
        }
    }

    /// <summary>
    /// Marks a wound as having had something dirty in it, which multiplies everything it will ever
    /// accumulate. W1's knife removal is the caller.
    /// </summary>
    public void Contaminate(EntityUid wound)
    {
        // WOLFGATE (W6): nothing to contaminate on a chassis, so digging a fragment out of one with a knife
        // costs nothing later.
        if (TerminatingOrDeleted(wound) || !TryComp(wound, out WoundComponent? core) ||
            !_traits.IsOrganic(core.HoldingPart))
            return;

        var infection = EnsureComp<WolfmedInfectionComponent>(wound);
        infection.Contamination = Profile.ContaminationMultiplier;
        infection.Cleaned = false;
        Dirty(wound, infection);
    }

    /// <summary>
    /// Antiseptic over every open wound on the body. Returns how many it reached. Cleaning is prevention:
    /// it sheds a local infection over the next minute but does nothing for one that has already spread,
    /// nor (INFECTION) for a part's own infection.
    /// </summary>
    public int Clean(EntityUid body)
    {
        var cleaned = 0;
        foreach (var part in Parts(body))
        {
            foreach (var wound in _wounds.GetWounds(part).ToArray())
            {
                if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
                    !TryComp(wound, out WolfmedInfectionComponent? infection) || infection.Cleaned)
                    continue;

                infection.Cleaned = true;
                infection.Contamination = 1f;
                Dirty(wound.Owner, infection);
                cleaned++;
            }
        }

        if (cleaned > 0)
            _popup.PopupEntity(Loc.GetString("wolfmed-wound-cleaned"), body, body);

        return cleaned;
    }

    /// <summary>
    /// An antibiotic dose: clears contamination everywhere at once, (INFECTION) the parts' own infections with it,
    /// and pulls sepsis back. Returns whether there was anything to treat.
    /// </summary>
    public bool Treat(EntityUid body, float units)
    {
        var profile = Profile;
        var treated = false;

        foreach (var part in Parts(body).ToArray())
        {
            foreach (var wound in _wounds.GetWounds(part).ToArray())
            {
                if (!TryComp(wound, out WolfmedInfectionComponent? infection))
                    continue;

                treated |= infection.Progress > 0f;
                SetProgress((wound.Owner, infection, wound.Comp),
                    infection.Progress - profile.AntibioticPerUnit * units,
                    profile);
            }

            if (!TryComp(part, out WolfmedPartInfectionComponent? partInfection))
                continue;

            treated |= partInfection.Progress > 0f;
            SetPartProgress(part, partInfection.Progress - profile.PartAntibioticPerUnit * units, profile);
        }

        if (!TryComp(body, out WolfmedSepsisComponent? sepsis))
            return treated;

        sepsis.Progress -= profile.SepsisAntibioticPerUnit * units;
        if (sepsis.Progress <= 0f)
            RemComp<WolfmedSepsisComponent>(body);
        else
        {
            sepsis.Shock = sepsis.Progress >= _config.GetCVar(WolfmedCVars.SepticShockAt);
            Dirty(body, sepsis);
            _consciousness.SetExternalPressure(body, ShockPressure, sepsis.Shock ? 1f : 0f); // Playtest 5
        }

        return true;
    }

    /// <summary>
    /// Playtest 5: whether anything on this body is infected past contamination, or the body is septic. What the pod's
    /// antibiotic course reads.
    /// </summary>
    public bool HasInfection(EntityUid body)
    {
        if (GetSepsis(body) > 0f)
            return true;

        foreach (var part in Parts(body))
        {
            if (GetPartStage(part) >= WolfmedInfectionStage.Local ||
                GetWorstWoundStage(part) >= WolfmedInfectionStage.Local)
                return true;
        }

        return false;
    }

    /// <summary>The infection on one wound, for the analyzer and for tests.</summary>
    public WolfmedInfectionStage GetStage(EntityUid wound) =>
        CompOrNull<WolfmedInfectionComponent>(wound)?.Stage ?? WolfmedInfectionStage.None;

    /// <summary>The worst infection stage among a part's wounds.</summary>
    public WolfmedInfectionStage GetWorstWoundStage(Entity<WoundableComponent?> part)
    {
        var stage = WolfmedInfectionStage.None;
        foreach (var wound in _wounds.GetWounds(part))
            stage = (WolfmedInfectionStage) Math.Max((byte) stage, (byte) GetStage(wound));

        return stage;
    }

    /// <summary>INFECTION: the part's own infection stage, which travels towards the torso.</summary>
    public WolfmedInfectionStage GetPartStage(EntityUid part) =>
        CompOrNull<WolfmedPartInfectionComponent>(part)?.Stage ?? WolfmedInfectionStage.None;

    /// <summary>INFECTION: the part's own infection progress, 0 to 100.</summary>
    public float GetPartProgress(EntityUid part) =>
        CompOrNull<WolfmedPartInfectionComponent>(part)?.Progress ?? 0f;

    /// <summary>How far the body's systemic infection has got, or 0 when it has none.</summary>
    public float GetSepsis(EntityUid body) => CompOrNull<WolfmedSepsisComponent>(body)?.Progress ?? 0f;
}
