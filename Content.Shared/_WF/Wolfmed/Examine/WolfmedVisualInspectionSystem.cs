using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared._WF.Wolfmed.Wounds;
using Robust.Shared.Configuration;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Armor;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Wolfmed.Examine;

/// <summary>
/// LOOK: the health examine on a wound host, rewritten as what a person could actually see. Every finding it
/// reports is something visible on the body in front of the examiner; numbers, rates, severities and
/// anything under the skin stay on the health analyzer.
/// </summary>
/// <remarks>
/// LOOK2: the result is structured rather than prose. <see cref="GetLook"/> returns one
/// <see cref="WolfmedLookObservation"/> per finding, carrying a glyph, a palette colour, a short label and
/// the full sentence; <see cref="AddLookMarkup"/> writes those into the examine message through
/// <see cref="WolfmedLookTag"/> for the client to draw as rows, with the plain-text line alongside for
/// everything else that renders the message. Runs on the server (the examine verb is not client-exclusive),
/// so it reads wound state directly and networks nothing new. What each wound looks like is data
/// (<see cref="WolfmedWoundLookPrototype"/>), not a switch: no wound id appears in this file.
/// </remarks>
public sealed class WolfmedVisualInspectionSystem : EntitySystem
{
    /// <summary>Appended to a finding's locale key for its row label.</summary>
    private const string LabelSuffix = "-short";

    /// <summary>M5: the close-up sign each remaining cause leaves while it holds the body, as a locale key stem.</summary>
    private static readonly (WolfmedCause Cause, string Key)[] RemainingCauseSigns =
    {
        (WolfmedCause.Toxin, "wolfmed-look-poisoned"),
        (WolfmedCause.Radiation, "wolfmed-look-radiation-sick"),
        (WolfmedCause.Cold, "wolfmed-look-hypothermic"),
        (WolfmedCause.Heat, "wolfmed-look-overheated"),
    };

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IConfigurationManager _cfg = default!; // M2
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private PainSystem _pain = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;

    /// <summary>Wound prototype id to its appearance. Rebuilt whenever prototypes are reloaded.</summary>
    private readonly Dictionary<string, WolfmedWoundLookPrototype> _looks = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        BuildIndex();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<WolfmedWoundLookPrototype>())
            BuildIndex();
    }

    private void BuildIndex()
    {
        _looks.Clear();
        foreach (var look in _prototypes.EnumeratePrototypes<WolfmedWoundLookPrototype>())
            _looks[look.Wound.Id] = look;
    }

    /// <summary>The appearance declared for a wound prototype, if one is.</summary>
    public bool TryGetLook(string wound, [NotNullWhen(true)] out WolfmedWoundLookPrototype? look) =>
        _looks.TryGetValue(wound, out look);

    /// <summary>
    /// Appends the visual inspection of a wound host to an examine message. <paramref name="detailed"/> is
    /// the examiner's details range; examining yourself is always detailed and never hidden by clothing.
    /// </summary>
    public void AddLookMarkup(EntityUid examined, EntityUid examiner, FormattedMessage message, bool detailed)
    {
        if (GetLook(examined, examiner, detailed) is not { } report)
            return;

        if (!message.IsEmpty)
            message.PushNewline();

        message.AddMarkupOrThrow(report.Title);

        foreach (var part in report.Parts)
        {
            // The nodes are what the client draws; the line after them is what every other reader gets.
            WolfmedLookTag.WritePart(message, part);
            message.PushNewline();
            message.AddMarkupOrThrow(part.Line);
            WolfmedLookTag.WriteEnd(message);
        }

        foreach (var note in report.Notes)
        {
            message.PushNewline();
            message.AddMarkupOrThrow(note);
        }
    }

    /// <summary>
    /// LOOK2: everything one examiner can see on one patient, head to foot, with only the parts that have
    /// something to show. Public so tests and any other presentation read the findings rather than markup.
    /// </summary>
    public WolfmedLookReport? GetLook(EntityUid examined, EntityUid examiner, bool detailed)
    {
        var self = examined == examiner;
        detailed |= self;

        if (!_prototypes.TryIndex<WolfmedLookProfilePrototype>(WolfmedLookProfilePrototype.Default, out var profile))
            return null;

        var identity = Identity.Entity(examined, EntityManager);
        var report = new WolfmedLookReport
        {
            Title = Loc.GetString(self ? "wolfmed-look-title-self" : "wolfmed-look-title-other",
                ("target", identity)),
        };

        var parts = _body.GetBodyChildren(examined)
            .OrderBy(part => PartOrder(part.Component.PartType))
            .ThenBy(part => (int) part.Component.Symmetry)
            .ToList();

        var coverage = self ? null : GetCoverage(examined, profile);
        var hidden = false;
        var lines = 0;

        foreach (var (part, component) in parts)
        {
            var found = new WolfmedLookPart { Name = PartName(part, component) };
            var covered = coverage != null && IsCovered(coverage, component.PartType, component.Symmetry);
            hidden |= AddPartFindings(part, self, detailed, covered, profile, found);
            if (found.Findings.Count == 0)
                continue;

            found.Accent = Accent(profile, found);
            found.Line = Loc.GetString(self ? "wolfmed-look-part-self" : "wolfmed-look-part-other",
                ("target", identity),
                ("part", found.Name),
                ("findings", string.Join(", ", found.Findings.Select(finding => finding.Label))));
            report.Parts.Add(found);
            lines++;
        }

        // Sepsis is the one finding that belongs to the whole patient rather than to a part. INFECTION: in septic shock
        // the flush gives way to grey, clammy skin.
        if (detailed && TryComp(examined, out WolfmedSepsisComponent? sepsis) &&
            (sepsis.Shock || sepsis.Progress >= profile.SepsisVisibleAt))
        {
            var key = sepsis.Shock ? "wolfmed-look-septic-shock" : "wolfmed-look-sepsis";
            report.Notes.Add(Loc.GetString(self ? key + "-self" : key + "-other", ("target", identity)));
            lines++;
        }

        // ARREST: a stopped heart reads as a corpse from across the room. Nothing an examiner can see tells
        // it apart from a body that has actually died; the analyzer and the medical HUD still can.
        var arrested = HasComp<WolfmedCardiacArrestComponent>(examined) &&
                       !HasComp<WolfmedShutdownComponent>(examined);
        if (arrested)
        {
            report.Notes.Add(Loc.GetString(
                self ? "wolfmed-look-appears-dead-self" : "wolfmed-look-appears-dead-other",
                ("target", identity)));
            lines++;
        }

        // BRAIN: body-level, like sepsis, and the first thing a medic checks for.
        if (detailed && HasComp<WolfmedShutdownComponent>(examined))
        {
            report.Notes.Add(Loc.GetString(self ? "wolfmed-look-shutdown-self" : "wolfmed-look-shutdown-other",
                ("target", identity)));
            lines++;
        }
        else if (detailed && arrested)
        {
            report.Notes.Add(Loc.GetString(self ? "wolfmed-look-no-pulse-self" : "wolfmed-look-no-pulse-other",
                ("target", identity)));
            lines++;
        }

        // M1a: the chest and the pulse, read off the networked vitals (plan §4.5, §5.5). A machine has neither.
        // M4: every mechanical wound host carries the core-heat component, a synth included.
        var machine = HasComp<SiliconComponent>(examined) || HasComp<WolfmedShutdownComponent>(examined) ||
                      HasComp<WolfmedCoreHeatComponent>(examined);
        var vitals = CompOrNull<WolfmedConsciousnessComponent>(examined);
        if (!machine && BreathingKey(examined, vitals) is { } breathing &&
            (detailed || arrested && breathing == "wolfmed-look-not-breathing"))
        {
            report.Notes.Add(Loc.GetString(breathing + (self ? "-self" : "-other"), ("target", identity)));
            lines++;
        }

        if (!machine && detailed && !arrested && vitals != null && CirculationKey(vitals.BloodBand) is { } pulse)
        {
            report.Notes.Add(Loc.GetString(pulse + (self ? "-self" : "-other"), ("target", identity)));
            lines++;
        }

        // M1b (plan §3.7): burns deep enough to lose fluid, so a burn patient reads as a fluids patient.
        if (!machine && detailed && HasComp<WolfmedBurnFluidLossComponent>(examined))
        {
            report.Notes.Add(Loc.GetString(self ? "wolfmed-look-weeping-burns-self" : "wolfmed-look-weeping-burns-other",
                ("target", identity)));
            lines++;
        }

        // M4 (plan §3.11): a chassis heating toward thermal shutdown smokes, which shows at any range.
        if (TryComp(examined, out WolfmedCoreHeatComponent? heat) && heat.Hot)
        {
            report.Notes.Add(Loc.GetString(self ? "wolfmed-look-too-hot-self" : "wolfmed-look-too-hot-other",
                ("target", identity)));
            lines++;
        }

        // M2 (plan §5.5, §5.3): responsiveness, blue lips and the pupils close up; playing dead at any range.
        lines += AddM2Signs(examined, report, identity, self, detailed, machine, arrested, vitals);

        // M3 (plan §3.6, §8): what organ damage shows a hand on the neck and a penlight. An impaired heart beats
        // irregularly; a brain holding the patient down leaves unequal pupils.
        if (!machine && detailed && !arrested && vitals is { PulseIrregular: true })
        {
            report.Notes.Add(Loc.GetString(self ? "wolfmed-look-pulse-irregular-self" : "wolfmed-look-pulse-irregular-other",
                ("target", identity)));
            lines++;
        }

        if (!machine && detailed && vitals != null &&
            (vitals.Cause == WolfmedCause.Brain || (vitals.Blockers & WolfmedCauseFlags.Brain) != 0))
        {
            report.Notes.Add(Loc.GetString(self ? "wolfmed-look-unequal-pupils-self" : "wolfmed-look-unequal-pupils-other",
                ("target", identity)));
            lines++;
        }

        // M5 (plan §3.8-3.10): a poisoned patient retches, a sick one is grey, a cold one is stiff, a hot one is dry.
        if (!machine && detailed && vitals != null)
        {
            foreach (var (cause, key) in RemainingCauseSigns)
            {
                if (vitals.Cause != cause && (vitals.Blockers & WolfmedCauses.Flag(cause)) == 0)
                    continue;

                report.Notes.Add(Loc.GetString(key + (self ? "-self" : "-other"), ("target", identity)));
                lines++;
            }
        }

        if (lines == 0)
        {
            // Nothing shown at all reads differently when there was something and the clothing took it.
            report.Notes.Add(Loc.GetString(
                hidden ? "wolfmed-look-hidden" : self ? "wolfmed-look-none-self" : "wolfmed-look-none-other",
                ("target", identity)));
        }
        else if (hidden)
        {
            report.Notes.Add(Loc.GetString("wolfmed-look-covered"));
        }

        if (!detailed)
            report.Notes.Add(Loc.GetString("wolfmed-look-distant"));

        return report;
    }

    /// <summary>Adds the medic's close-up signs from networked values only; returns the lines added.</summary>
    // AVPU from the state and cause: alert while Downed, voice when sedated past wolfmed.sedation_warn, pain in a
    // faint, unresponsive when out or dying; nothing for somebody up and clear-headed. Blue lips under
    // wolfmed.examine_cyanosis_oxygenation, pinpoint pupils with sedation, unequal pupils with a concussion. A Downed
    // body playing dead reads "appears lifeless" from anywhere.
    private int AddM2Signs(EntityUid examined, WolfmedLookReport report, EntityUid identity, bool self, bool detailed,
        bool machine, bool arrested, WolfmedConsciousnessComponent? vitals)
    {
        // Signs somebody else reads off you; nobody sees their own pupils.
        if (self)
            return 0;

        var added = 0;
        var dead = TryComp(examined, out MobStateComponent? mob) && mob.CurrentState == MobState.Dead;

        if (!detailed && !dead && HasComp<WolfmedPlayingDeadComponent>(examined))
        {
            report.Notes.Add(Loc.GetString("wolfmed-look-lifeless-other", ("target", identity)));
            added++;
        }

        if (machine || !detailed || dead || vitals == null)
            return added;

        var sedation = CompOrNull<WolfmedPainReliefComponent>(examined)?.Sedation ?? 0f;
        var sedated = sedation >= _cfg.GetCVar(WolfmedCVars.SedationWarn);
        var avpu = vitals.State switch
        {
            _ when arrested => "unresponsive",
            WolfmedConsciousness.Unconscious when WolfmedCauses.IsFaint(vitals.Cause) => "pain",
            WolfmedConsciousness.Unconscious => "unresponsive",
            _ when sedated || vitals.Cause == WolfmedCause.Sedation => "voice",
            WolfmedConsciousness.Downed => "alert",
            _ => null,
        };

        // Up close, a Downed body playing dead is still breathing and blinking: the medic sees through it.
        if (avpu != null)
        {
            report.Notes.Add(Loc.GetString($"wolfmed-look-avpu-{avpu}", ("target", identity)));
            added++;
        }

        if (!arrested && vitals.Oxygenation < _cfg.GetCVar(WolfmedCVars.ExamineCyanosisOxygenation))
        {
            report.Notes.Add(Loc.GetString("wolfmed-look-blue-lips", ("target", identity)));
            added++;
        }

        if (sedated)
        {
            report.Notes.Add(Loc.GetString("wolfmed-look-pupils-pinpoint", ("target", identity)));
            added++;
        }

        if (BrainInjured(examined))
        {
            report.Notes.Add(Loc.GetString("wolfmed-look-pupils-unequal", ("target", identity)));
            added++;
        }

        return added;
    }

    /// <summary>
    /// M2: a brain injury, not a knock on the head: the brain organ under its concussion line, or a repaired brain
    /// still carrying its trauma. A head wound's own concussion has its own look.
    /// </summary>
    private bool BrainInjured(EntityUid body)
    {
        if (HasComp<WolfmedBrainTraumaComponent>(body))
            return true;

        foreach (var (organ, _) in _body.GetBodyOrgans(body))
        {
            if (!TryComp(organ, out WolfmedBrainComponent? brain) || !TryComp(organ, out WolfmedOrganComponent? health) ||
                health.MaxHealth <= FixedPoint2.Zero)
                continue;

            return health.Health.Float() / health.MaxHealth.Float() < brain.ConcussionAt;
        }

        return false;
    }

    /// <summary>
    /// M1a: what the chest is doing, from <see cref="WolfmedConsciousnessComponent.Breathing"/>, which the
    /// server's life tick sets for every cause: arrest, death, no lungs, no air, sedation past its depression
    /// line (slow and shallow, no longer "not breathing"). Null when it is breathing normally.
    /// </summary>
    private string? BreathingKey(EntityUid examined, WolfmedConsciousnessComponent? vitals)
    {
        // A corpse whose vitals have not been rewritten since it died is still not breathing.
        if (TryComp(examined, out MobStateComponent? mob) && mob.CurrentState == MobState.Dead ||
            HasComp<WolfmedCardiacArrestComponent>(examined))
            return "wolfmed-look-not-breathing";

        return vitals?.Breathing switch
        {
            WolfmedBreathing.None => "wolfmed-look-not-breathing",
            WolfmedBreathing.Gasping => "wolfmed-look-gasping",
            WolfmedBreathing.Depressed => "wolfmed-look-breathing-slow",
            WolfmedBreathing.Laboured => "wolfmed-look-short-of-breath", // M3: damaged lungs
            _ => null,
        };
    }

    /// <summary>
    /// M1a: circulation in a hand-on-the-neck's words, from <see cref="WolfmedConsciousnessComponent.BloodBand"/>.
    /// A strong pulse is not a finding; an arrested heart already has its own "no pulse" line.
    /// </summary>
    private static string? CirculationKey(WolfmedBloodBand band) => band switch
    {
        WolfmedBloodBand.Low => "wolfmed-look-pale",
        WolfmedBloodBand.Weak => "wolfmed-look-pulse-weak",
        WolfmedBloodBand.Critical => "wolfmed-look-pulse-faint",
        WolfmedBloodBand.None => "wolfmed-look-no-pulse",
        _ => null,
    };

    /// <summary>Collects everything visible on one part. Returns true when something was hidden by clothing.</summary>
    private bool AddPartFindings(
        EntityUid part,
        bool self,
        bool detailed,
        bool covered,
        WolfmedLookProfilePrototype profile,
        WolfmedLookPart found)
    {
        var mechanical = _traits.IsMechanical(part);
        var suffix = mechanical ? "-mechanical" : string.Empty;
        var seen = new HashSet<string>();
        var hidden = false;
        var rate = 0f;
        var treatment = BleedingTreatment.None;
        var infection = WolfmedInfectionStage.None;
        var scars = 0;

        foreach (var wound in _wounds.GetWounds(part))
        {
            if (wound.Comp.State == WoundState.Healed)
                continue;

            if (HasComp<WoundScarComponent>(wound))
            {
                scars++;
                continue;
            }

            if (TryComp(wound, out WoundBleedingComponent? bleeding))
            {
                // The dressing is a finding, and so is whatever still gets past it. A sutured, clamped or seared
                // wound has no rate left, so only a dressing that is losing (gauze over a deep wound, a bandaged
                // artery) reads as both dressed and bleeding. Hiding that told the medic the job was done.
                if (bleeding.Treatment != BleedingTreatment.None)
                    treatment = (BleedingTreatment) Math.Max((byte) treatment, (byte) bleeding.Treatment);

                rate += bleeding.CurrentRate;
            }

            if (TryComp(wound, out WolfmedInfectionComponent? infected) && infected.Stage > infection)
                infection = infected.Stage;

            if (!_looks.TryGetValue(wound.Comp.Prototype.Id, out var look) ||
                !_prototypes.TryIndex(wound.Comp.Prototype, out var prototype))
                continue;

            var finding = look.Resolve(prototype.GetStage(wound.Comp.Severity));
            if (finding.Description is not { } description || finding.Visibility == WolfmedLookVisibility.None)
            {
                // Nothing to see. The patient still knows their own body hurts in a particular way, and what
                // they know is a feeling rather than a sight, so it wears the pain glyph.
                if (self && finding.SelfHint is { } hint)
                    Add(found, seen, hint.Id, finding.HintLabel?.Id, Glyph(profile, WolfmedLookClasses.Pain));
                continue;
            }

            if (!detailed && !finding.Distant)
                continue;

            if (covered && finding.Visibility != WolfmedLookVisibility.Clothed)
            {
                hidden = true;
                continue;
            }

            // A wound draws with its analyzer category unless its look names something better.
            var state = WolfmedWoundCategories.IconState(WolfmedWoundCategories.Resolve(prototype));
            Add(found, seen, description.Id, finding.Label?.Id,
                new WolfmedLookGlyph { Icon = finding.Icon ?? state, Colour = finding.Colour ?? state });
        }

        if (rate > profile.BleedOozing && (detailed || rate >= profile.DistantBleed))
        {
            var band = rate >= profile.BleedSpurting ? "spurting"
                : rate >= profile.BleedFlowing ? "flowing"
                : "oozing";

            if (!covered)
                Add(found, seen, "wolfmed-look-bleed-" + band + suffix, null,
                    Glyph(profile, WolfmedLookClasses.Bleed));
            else if (rate >= profile.SoakThrough)
                Add(found, seen, "wolfmed-look-soaking" + suffix, null, Glyph(profile, WolfmedLookClasses.Soak));
            else
                hidden = true;
        }

        // Dressings sit against the skin; a splint or a tourniquet is strapped over whatever is worn.
        if (treatment != BleedingTreatment.None && detailed)
        {
            if (covered)
                hidden = true;
            else
                Add(found, seen, "wolfmed-look-treatment-" + treatment.ToString().ToLowerInvariant(), null,
                    Glyph(profile, WolfmedLookClasses.Treatment));
        }

        if (detailed && TryComp(part, out WolfmedSplintMarkComponent? splint))
            Add(found, seen, "wolfmed-look-splint-" + splint.Overlay.ToString().ToLowerInvariant(), null,
                Glyph(profile, WolfmedLookClasses.Splint));

        if (detailed && HasComp<WolfmedTourniquetComponent>(part))
            Add(found, seen, "wolfmed-look-tourniquet", null, Glyph(profile, WolfmedLookClasses.Tourniquet));

        if (detailed && infection is WolfmedInfectionStage.Local or WolfmedInfectionStage.Spreading)
        {
            if (covered)
                hidden = true;
            else
                Add(found, seen, "wolfmed-look-infection-" + infection.ToString().ToLowerInvariant(), null,
                    Glyph(profile, WolfmedLookClasses.Infection));
        }

        if (detailed && scars > 0)
        {
            if (covered)
                hidden = true;
            else
                Add(found, seen, "wolfmed-look-scars", null, Glyph(profile, WolfmedLookClasses.Scars),
                    ("count", scars));
        }

        if (self && detailed && TryComp(part, out PainComponent? pain))
        {
            if (pain.Suppression > 0)
                Add(found, seen, "wolfmed-look-numb", null, Glyph(profile, WolfmedLookClasses.Numb));

            if (GetPainLevel(part) is { } level)
                Add(found, seen, "health-examinable-pain-" + level, null,
                    Glyph(profile, WolfmedLookClasses.Pain));
        }

        return hidden;
    }

    /// <summary>
    /// Adds a finding once per locale key, so two identical wounds read as one observation. The key's string
    /// is the tooltip; the label is the key the data named, else the key's own <c>-short</c> form, else the
    /// whole sentence.
    /// </summary>
    private void Add(
        WolfmedLookPart part,
        HashSet<string> seen,
        string key,
        string? label,
        WolfmedLookGlyph glyph,
        params (string, object)[] args)
    {
        if (!seen.Add(key))
            return;

        var text = Plain(Loc.GetString(key, args));
        part.Findings.Add(new WolfmedLookObservation(glyph.Icon, glyph.Colour, Label(key, label, text, args), text));
    }

    private string Label(string key, string? label, string text, (string, object)[] args)
    {
        if (label != null && Loc.TryGetString(label, out var declared, args))
            return Plain(declared);

        return Loc.TryGetString(key + LabelSuffix, out var shortForm, args) ? Plain(shortForm) : text;
    }

    /// <summary>The glyph declared for a finding class, or the neutral one if the profile is missing it.</summary>
    private static WolfmedLookGlyph Glyph(WolfmedLookProfilePrototype profile, string cls) =>
        profile.Glyph(cls) ?? new WolfmedLookGlyph { Icon = "other", Colour = WolfmedLookPalette.Neutral };

    /// <summary>The row marker's colour: the worst finding on the part, in the order the profile declares.</summary>
    private static string Accent(WolfmedLookProfilePrototype profile, WolfmedLookPart part)
    {
        foreach (var key in profile.AccentPriority)
        {
            foreach (var finding in part.Findings)
            {
                if (finding.Colour == key)
                    return key;
            }
        }

        return part.Findings.Count > 0 ? part.Findings[0].Colour : WolfmedLookPalette.Neutral;
    }

    /// <summary>Findings travel as plain text: the row's colour comes from the palette, not from markup.</summary>
    private static string Plain(string markup) => FormattedMessage.RemoveMarkupPermissive(markup);

    /// <summary>Onyx's own pain bands, kept so the self line reads exactly as it did before.</summary>
    private string? GetPainLevel(EntityUid part)
    {
        var value = _pain.GetPain(part);
        return value >= 50 ? "agony"
            : value >= 30 ? "terrible"
            : value >= 15 ? "strong"
            : value > 0 ? "light"
            : null;
    }

    /// <summary>What every worn item hides, resolved once per examine.</summary>
    private List<(HashSet<BodyPartType> Parts, HashSet<BodyPartSymmetry>? Sides)> GetCoverage(
        EntityUid examined,
        WolfmedLookProfilePrototype profile)
    {
        var coverage = new List<(HashSet<BodyPartType> Parts, HashSet<BodyPartSymmetry>? Sides)>();
        if (!_inventory.TryGetContainerSlotEnumerator(examined, out var slots, SlotFlags.WITHOUT_POCKET))
            return coverage;

        while (slots.NextItem(out var item, out var slot))
        {
            // Locational armour already says what it protects; ordinary clothing falls back to its slot.
            if (TryComp(item, out ArmorComponent? armor) && armor.Coverage is { Count: > 0 } parts)
            {
                coverage.Add((parts, armor.CoverageSymmetry));
                continue;
            }

            if (GetSlotCoverage(profile, slot.SlotFlags) is { } fallback)
                coverage.Add((fallback, null));
        }

        return coverage;
    }

    private static HashSet<BodyPartType>? GetSlotCoverage(WolfmedLookProfilePrototype profile, SlotFlags flags)
    {
        foreach (var entry in profile.Coverage)
        {
            foreach (var candidate in entry.Slots)
            {
                if ((flags & candidate) != 0)
                    return entry.Parts;
            }
        }

        return null;
    }

    private static bool IsCovered(
        List<(HashSet<BodyPartType> Parts, HashSet<BodyPartSymmetry>? Sides)> coverage,
        BodyPartType type,
        BodyPartSymmetry symmetry)
    {
        foreach (var (parts, sides) in coverage)
        {
            if (parts.Contains(type) && (sides is not { Count: > 0 } || sides.Contains(symmetry)))
                return true;
        }

        return false;
    }

    /// <summary>The part's name in the lower case a sentence wants, falling back to the entity's own.</summary>
    public string PartName(EntityUid part, BodyPartComponent component)
    {
        if (_body.GetTargetBodyPart(component) is not { } target)
            return Name(part);

        var key = "wolfmed-look-part-name-" + PartKey(target);
        var name = Loc.GetString(key);
        return name == key ? Name(part) : name;
    }

    private static string PartKey(TargetBodyPart part) => part.ToString()
        .Replace("Left", "left-")
        .Replace("Right", "right-")
        .ToLowerInvariant();

    /// <summary>Head to foot, the order somebody's eye travels down a body.</summary>
    private static int PartOrder(BodyPartType type) => type switch
    {
        BodyPartType.Head => 0,
        BodyPartType.Torso => 1,
        BodyPartType.Arm => 2,
        BodyPartType.Hand => 3,
        BodyPartType.Leg => 4,
        BodyPartType.Foot => 5,
        BodyPartType.Tail => 6,
        _ => 7,
    };
}
