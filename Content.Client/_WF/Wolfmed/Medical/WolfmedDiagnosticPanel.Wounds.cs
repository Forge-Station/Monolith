using System.Linq;
using System.Numerics;
using System.Text;
using Content.Shared._Onyx.Medical;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared._WF.Wolfmed.Reagents; // CONSC
using Content.Shared._WF.Wolfmed.Life; // M1a
using Content.Shared.FixedPoint;
using Content.Shared.MedicalScanner;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.Wolfmed.Medical;

/// <summary>The wounds tab: a card per body part with its condition chips and its wounds by category.</summary>
// The targeted part's card is marked and scrolled to, its header moves the target, and every row, chip and banner
// carries treatment advice on hover and a procedure window on click.
public sealed partial class WolfmedDiagnosticPanel
{
    private const float IconSize = 18f;
    private const float ChipIconSize = 14f;

    /// <summary>The category chip the medic clicked, or null for "show every part".</summary>
    private WolfmedWoundCategory? _categoryFilter;

    /// <summary>Whose findings are on screen, so the filter can be dropped when the patient changes.</summary>
    private NetEntity? _woundTarget;

    /// <summary>UI3: the local player's targeted body part, mirrored from the analyzer window.</summary>
    private TargetBodyPart? _targetedPart;

    /// <summary>UI3: the card to bring into view once layout has placed it.</summary>
    private Control? _scrollTo;

    /// <summary>
    /// UI3: set for the one redraw that follows a deliberate selection. Without it every scan tick would
    /// drag the list back to the targeted card while the medic is reading another one.
    /// </summary>
    private bool _scrollToTargeted;

    /// <summary>UI3: the procedure window, kept across repopulates. One per panel, not one per click.</summary>
    private WolfmedTreatmentWindow? _treatmentWindow;

    /// <summary>UI3: raised when the medic picks a part from a card header. The window moves the target.</summary>
    public event Action<TargetBodyPart>? OnPartSelected;

    /// <summary>
    /// FIX1: what the cards were last built from, with every continuously varying number left out. The
    /// analyzer rescans about once a second and pain moves on nearly every tick, so rebuilding the
    /// controls per payload destroyed whatever the cursor was hovering and closed its tooltip.
    /// </summary>
    private string? _woundSignature;

    /// <summary>FIX1: the sepsis banner's text, which carries a percentage that moves between rebuilds.</summary>
    private RichTextLabel? _sepsisLabel;
    private RichTextLabel? _arrestLabel; // BRAIN
    private RichTextLabel? _postShockLabel; // M1a
    private RichTextLabel? _vitalsLabel; // M1a: the vitals block
    private RichTextLabel? _brainLabel; // BRAIN

    /// <summary>CONSC: the two body-level banners whose numbers drift between rebuilds.</summary>
    private RichTextLabel? _painReliefLabel;

    private RichTextLabel? _sedationLabel;

    /// <summary>FIX1: the pain/scar line of each card, the only per-card text with a varying number in it.</summary>
    private readonly Dictionary<TargetBodyPart, Label> _painLabels = new();

    /// <summary>UI3: called by the window whenever the local player's body-part target moves.</summary>
    public void SetTargetedPart(TargetBodyPart? part, bool scrollIntoView)
    {
        if (_targetedPart == part && !scrollIntoView)
            return;

        _targetedPart = part;
        _scrollToTargeted = scrollIntoView;
        RefreshWoundFilter();
        _scrollToTargeted = false;
    }

    /// <summary>Drains the pending scroll once the card it points at has been laid out.</summary>
    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_scrollTo is not { } card)
            return;

        _scrollTo = null;
        if (card.Parent == null)
            return;

        var offset = WoundsTab.GetScrollValue().Y;
        Control? walk = card;
        while (walk != null && walk != WoundsTab)
        {
            offset += walk.Position.Y;
            walk = walk.Parent;
        }

        if (walk != null)
            WoundsTab.VScrollTarget = MathF.Max(0f, offset - 8f);
    }

    /// <summary>
    /// FIX1: rebuilds the cards only when the payload's shape changed, and otherwise just moves the
    /// numbers on the labels it kept. The treatment window is re-evaluated either way, so UI4's live
    /// update still sees every scan.
    /// </summary>
    private void DrawWoundDiagnostics(HealthAnalyzerScannedUserMessage msg)
    {
        // Populate runs on every scan update; the filter has to survive that, but not a new patient.
        if (_woundTarget != msg.TargetEntity)
        {
            _woundTarget = msg.TargetEntity;
            _categoryFilter = null;
            _woundSignature = null;
            // UI4: the open procedure belongs to the patient that just left, not to this one.
            _treatmentWindow?.Close();
        }

        var signature = BuildSignature(msg);
        // A deliberate selection has to redraw even when nothing moved, because the scroll is armed on the
        // card object the rebuild produces.
        if (!_scrollToTargeted && signature == _woundSignature)
        {
            UpdateVaryingText(msg);
            RefreshTreatment();
            return;
        }

        RebuildWoundDiagnostics(msg);
        // Recomputed, not reused: the strip drops a filter whose category has healed away, which is part
        // of the signature.
        _woundSignature = BuildSignature(msg);
        RefreshTreatment();
    }

    /// <summary>Throws every wound control away and builds them again from this payload.</summary>
    private void RebuildWoundDiagnostics(HealthAnalyzerScannedUserMessage msg)
    {
        _sepsisLabel = null;
        _arrestLabel = null; // BRAIN
        _brainLabel = null; // BRAIN
        _postShockLabel = null; // M1a
        _vitalsLabel = null; // M1a
        _painLabels.Clear();
        WoundAlertsContainer.RemoveAllChildren();
        WoundCategoryStrip.RemoveAllChildren();
        WoundFindingsContainer.RemoveAllChildren();
        WoundStateLabel.Visible = true;

        VitalDamageRow.Visible = msg.VitalDamage != null;
        if (msg.VitalDamage is { } vital)
            VitalDamageLabel.Text = vital.ToString();

        if (msg.ScanMode != true)
        {
            WoundStateLabel.SetMessage(Loc.GetString("health-analyzer-wound-diagnostics-inactive"));
            return;
        }

        // M1a: the vitals block heads the panel (plan §5.5): state and cause, breathing, circulation, defib.
        if (msg.WoundDiagnostics?.Vitals is { } vitalsBlock)
        {
            _vitalsLabel = CreateBannerRow("warning", VitalsColour(vitalsBlock), VitalsText(vitalsBlock),
                out var vitalsRow);
            WoundAlertsContainer.AddChild(vitalsRow);
        }

        // BRAIN: the two findings that outrank everything else on the body.
        if (msg.WoundDiagnostics is { BrainDead: true } deadBrain)
            WoundAlertsContainer.AddChild(CreateAlertRow(
                "warning",
                WolfmedWoundStyle.Necrosis,
                // M2 (OD10): a positronic core (no oxygenation clock, -1) names core repair and the restart.
                Loc.GetString(deadBrain.Oxygenation < 0f ? "health-analyzer-wound-brain-dead-core" : "health-analyzer-wound-brain-dead"),
                "brain-death",
                // A positronic core is not a brain: its banner and window are titled "Core failure".
                label: deadBrain.Oxygenation < 0f ? "core-failure" : null,
                mechanical: deadBrain.Oxygenation < 0f));
        else if (msg.WoundDiagnostics is { CardiacArrest: true } stopped)
        {
            WoundAlertsContainer.AddChild(CreateAlertRow(
                "warning",
                WolfmedWoundStyle.Bleeding,
                ArrestText(stopped),
                "cardiac-arrest",
                out var arrest));
            _arrestLabel = arrest;
        }

        if (msg.WoundDiagnostics is { Shutdown: true })
            WoundAlertsContainer.AddChild(CreateAlertRow(
                "warning",
                WolfmedWoundStyle.Necrosis,
                Loc.GetString("health-analyzer-wound-shutdown"),
                "cardiac-arrest",
                // A chassis shutdown shares cardiac arrest's treatment but not its name.
                label: "shutdown",
                mechanical: true));

        // M1a: a restarted heart with the blood still low. The units that keep it going, and the units that
        // stop the brain injury, kept apart (plan §7.1).
        // M2: the vitals block's "After a restart" line carries the same numbers; the banner only shows without it.
        if (msg.WoundDiagnostics is { PostShockUnits: >= 0f } revived && revived.Vitals is not { RestartUnits: >= 0f })
        {
            WoundAlertsContainer.AddChild(CreateAlertRow(
                "blood_low",
                WolfmedWoundStyle.Bleeding,
                PostShockText(msg.BloodLevel, revived),
                "blood-low",
                out var postShock));
            _postShockLabel = postShock;
        }

        if (msg.WoundDiagnostics is { BrainActivity: >= 0f } vitals)
        {
            // BRAIN: a brain that has lost tissue is a finding, not a quiet status line. Under a quarter
            // left there is not much of the patient in there any more.
            var damaged = vitals.BrainActivity < BrainDamageFinding;
            _brainLabel = CreateBannerRow(damaged ? "warning" : "reagent",
                damaged ? WolfmedWoundStyle.Necrosis : WolfmedWoundStyle.Infection,
                BrainText(vitals), out var brainRow);
            WoundAlertsContainer.AddChild(brainRow);
        }

        if (IsDangerousBloodLevel(msg.BloodLevel))
            WoundAlertsContainer.AddChild(CreateAlertRow(
                "blood_low",
                WolfmedWoundStyle.Bleeding,
                Loc.GetString("health-analyzer-wound-blood-level-dangerous"),
                "blood-low"));

        if (msg.WoundDiagnostics == null)
        {
            WoundStateLabel.SetMessage(Loc.GetString("health-analyzer-wound-diagnostics-unavailable"));
            return;
        }

        WoundStateLabel.Visible = false;

        // W5: systemic, so it is banner-level rather than an entry against any one part.
        if (msg.WoundDiagnostics.Sepsis > 0f)
        {
            WoundAlertsContainer.AddChild(CreateAlertRow(
                "sepsis",
                WolfmedWoundStyle.Necrosis,
                SepsisText(msg.WoundDiagnostics),
                "sepsis",
                out var sepsis,
                // INFECTION: the late stage is titled for itself; its procedure is still sepsis's.
                label: msg.WoundDiagnostics.Vitals is { SepticShock: true } ? "septic-shock" : null));
            _sepsisLabel = sepsis;
        }

        // CONSC: what is masking the patient's pain, and how far the sedation has gone. Both are
        // body-level, so they sit with the sepsis banner rather than against any one part.
        if (msg.WoundDiagnostics.PainRelief != WolfmedPainReliefTier.None)
        {
            _painReliefLabel = CreateBannerRow("reagent", WolfmedWoundStyle.Infection,
                PainReliefText(msg.WoundDiagnostics), out var reliefRow);
            WoundAlertsContainer.AddChild(reliefRow);
        }

        if (msg.WoundDiagnostics.Sedation > 0f)
        {
            _sedationLabel = CreateBannerRow("warning", WolfmedWoundStyle.Necrosis,
                SedationText(msg.WoundDiagnostics.Sedation), out var sedationRow);
            WoundAlertsContainer.AddChild(sedationRow);
        }

        BuildCategoryStrip(msg.WoundDiagnostics);

        var cards = 0;
        foreach (var part in SharedTargetingSystem.GetValidParts())
        {
            if (!msg.WoundDiagnostics.Parts.TryGetValue(part, out var diagnostic))
                continue;

            if (_categoryFilter is { } filter &&
                !diagnostic.VisibleWounds.Any(wound => wound.Category == filter))
                continue;

            var card = CreatePartCard(part, diagnostic);
            WoundFindingsContainer.AddChild(card);
            if (_scrollToTargeted && part == _targetedPart)
                _scrollTo = card;
            cards++;
        }

        if (cards > 0)
            return;

        var empty = _categoryFilter is { } shown
            ? Loc.GetString("health-analyzer-wound-no-findings-filtered",
                ("category", Loc.GetString(WolfmedWoundCategories.NameKey(shown))))
            : Loc.GetString("health-analyzer-wound-no-findings");

        WoundFindingsContainer.AddChild(new Label
        {
            Text = empty,
            StyleClasses = { "LabelSubText" },
            Margin = new Thickness(2, 2, 0, 0),
        });
    }

    /// <summary>
    /// FIX1: everything about a payload that decides which controls exist and what shape they take. Pain,
    /// sepsis percent, blood level and bleed rates are in here only as the discrete tests the cards make
    /// of them, never as their values, so the ordinary tick-to-tick drift does not rebuild anything.
    /// </summary>
    private string BuildSignature(HealthAnalyzerScannedUserMessage msg)
    {
        var text = new StringBuilder();
        text.Append(_categoryFilter?.ToString() ?? "-").Append('|')
            .Append(_targetedPart?.ToString() ?? "-").Append('|')
            .Append(msg.ScanMode == true ? '1' : '0')
            .Append(msg.VitalDamage != null ? '1' : '0')
            .Append(IsDangerousBloodLevel(msg.BloodLevel) ? '1' : '0');

        if (msg.WoundDiagnostics is not { } diagnostics)
            return text.Append("|none").ToString();

        text.Append(diagnostics.Sepsis > 0f ? "|sep" : "|-")
            .Append(diagnostics.Vitals is { SepticShock: true } ? 'S' : '-'); // INFECTION: the banner's title
        // BRAIN: the states, never the numbers that drift with them.
        text.Append(diagnostics.CardiacArrest ? 'a' : '-')
            .Append(diagnostics.BrainDead ? 'b' : '-')
            .Append(diagnostics.Shutdown ? 's' : '-')
            .Append(diagnostics.PostShockUnits >= 0f ? 'r' : '-') // M1a
            .Append(diagnostics.Vitals is { RestartUnits: >= 0f } ? 'R' : '-') // M2
            .Append(diagnostics.Vitals is { } shownVitals ? (char) ('A' + (int) shownVitals.State) : '-') // M1a
            .Append(diagnostics.BrainActivity >= 0f ? 'v' : '-')
            .Append(diagnostics.BrainActivity < BrainDamageCritical ? 'c'
                : diagnostics.BrainActivity < BrainDamageFinding ? 'd' : '-');
        // CONSC: only the presence of each banner, never the numbers on it.
        text.Append((int) diagnostics.PainRelief).Append(diagnostics.Sedation > 0f ? '1' : '0');

        foreach (var part in SharedTargetingSystem.GetValidParts())
        {
            if (!diagnostics.Parts.TryGetValue(part, out var diagnostic))
                continue;

            text.Append('|').Append((int) part).Append(':')
                .Append((int) diagnostic.Fracture).Append((int) diagnostic.FractureTreatment)
                .Append((int) diagnostic.BleedingTreatment).Append((int) diagnostic.ClottingPhase)
                .Append((int) diagnostic.Functionality).Append((int) diagnostic.Infection)
                .Append((int) diagnostic.PartInfection) // INFECTION
                .Append((int) diagnostic.Treatments).Append(',')
                .Append(diagnostic.EmbeddedObjects).Append(',')
                .Append(diagnostic.BleedingRate > 0f ? '1' : '0')
                .Append(diagnostic.InternalBleedingRate > 0f ? '1' : '0')
                .Append(diagnostic.Necrotic ? '1' : '0')
                .Append(diagnostic.NecrosisRisk ? '1' : '0')
                .Append(diagnostic.Mechanical ? '1' : '0')
                .Append(diagnostic.Overheating ? '1' : '0')
                .Append(diagnostic.Pain > FixedPoint2.Zero ? '1' : '0')
                .Append(diagnostic.ScarCount > 0 ? '1' : '0');

            foreach (var wound in diagnostic.VisibleWounds)
            {
                text.Append(';').Append(wound.Prototype).Append('/')
                    .Append(wound.Name.ToString()).Append('/')
                    .Append(wound.StageName?.ToString() ?? "-").Append('/')
                    .Append(wound.Count).Append('/').Append((int) wound.Category);
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// FIX1: moves the numbers that change on their own onto the labels the last rebuild left behind. The
    /// tooltips are rebuilt with them; none of the advice strings carries one of these numbers, so in
    /// practice this is the sepsis banner, the vital damage figure and one line per card.
    /// </summary>
    private void UpdateVaryingText(HealthAnalyzerScannedUserMessage msg)
    {
        if (msg.VitalDamage is { } vital)
            VitalDamageLabel.Text = vital.ToString();

        if (msg.WoundDiagnostics is not { } diagnostics)
            return;

        if (_sepsisLabel is { } sepsis && diagnostics.Sepsis > 0f)
            sepsis.SetMessage(FormattedMessage.FromMarkupPermissive(SepsisText(diagnostics)));

        // BRAIN: the countdown and the activity percentage both move every tick.
        if (_arrestLabel is { } arrest && diagnostics.CardiacArrest)
            arrest.SetMessage(FormattedMessage.FromMarkupPermissive(ArrestText(diagnostics)));

        // M1a: every vitals line can move between scans (cause, blood %, trend, verdict).
        if (_vitalsLabel is { } vitalsLabel && diagnostics.Vitals is { } vitals)
            vitalsLabel.SetMessage(FormattedMessage.FromMarkupPermissive(VitalsText(vitals)));

        if (_postShockLabel is { } postShock && diagnostics.PostShockUnits >= 0f) // M1a
            postShock.SetMessage(FormattedMessage.FromMarkupPermissive(PostShockText(msg.BloodLevel, diagnostics)));

        if (_brainLabel is { } brainLabel && diagnostics.BrainActivity >= 0f)
            brainLabel.SetMessage(FormattedMessage.FromMarkupPermissive(BrainText(diagnostics)));

        // CONSC: the time left and the sedation percent both drift every tick.
        if (_painReliefLabel is { } painRelief && diagnostics.PainRelief != WolfmedPainReliefTier.None)
            painRelief.SetMessage(FormattedMessage.FromMarkupPermissive(PainReliefText(diagnostics)));

        if (_sedationLabel is { } sedation && diagnostics.Sedation > 0f)
            sedation.SetMessage(FormattedMessage.FromMarkupPermissive(SedationText(diagnostics.Sedation)));

        foreach (var (part, label) in _painLabels)
        {
            if (diagnostics.Parts.TryGetValue(part, out var diagnostic) && FooterText(diagnostic) is { } text)
                label.Text = text;
        }
    }

    /// <summary>FIX1: dropped when the panel is cleared, so a stale label is never written to.</summary>
    private void ResetWoundControls()
    {
        _woundSignature = null;
        _sepsisLabel = null;
        _arrestLabel = null; // BRAIN
        _brainLabel = null; // BRAIN
        _postShockLabel = null; // M1a
        _vitalsLabel = null; // M1a
        _painReliefLabel = null; // CONSC
        _sedationLabel = null; // CONSC
        _painLabels.Clear();
    }

    /// <summary>M1a: the vitals block, one line each, worded by the shared text so the tests read the same.</summary>
    private static string VitalsText(WolfmedVitalsReport vitals) =>
        FormattedMessage.EscapeText(string.Join("\n", WolfmedVitalsText.Lines(vitals)));

    /// <summary>M1a: the block's edge colour follows how much trouble the patient is in.</summary>
    private static Color VitalsColour(WolfmedVitalsReport vitals) => vitals.State switch
    {
        WolfmedVitalsState.Up => WolfmedWoundStyle.Clotting,
        WolfmedVitalsState.Downed => WolfmedWoundStyle.Pain,
        WolfmedVitalsState.Faint => WolfmedWoundStyle.Pain,
        _ => WolfmedWoundStyle.Necrosis,
    };

    /// <summary>M1a: the post-shock transfusion line.</summary>
    private static string PostShockText(float bloodLevel, HealthAnalyzerWoundDiagnostics diagnostics) =>
        WolfmedPostShockText.Format(bloodLevel, diagnostics.PostShockUnits, diagnostics.PostShockSafeUnits,
            diagnostics.PostShockGraceSeconds, diagnostics.PostShockSafeLine);

    /// <summary>BRAIN: no pulse, and how long the brain has left if nothing changes.</summary>
    private static string ArrestText(HealthAnalyzerWoundDiagnostics diagnostics)
    {
        if (diagnostics.BrainDeathSeconds < 0f)
            return Loc.GetString("health-analyzer-wound-cardiac-arrest");

        var total = (int) MathF.Round(diagnostics.BrainDeathSeconds);
        return Loc.GetString("health-analyzer-wound-cardiac-arrest-timed",
            ("minutes", total / 60), ("seconds", (total % 60).ToString("00")));
    }

    /// <summary>Brain tissue left under which the analyzer calls it damage rather than a reading.</summary>
    private const float BrainDamageFinding = 0.6f;

    /// <summary>And under which it stops being polite about it.</summary>
    private const float BrainDamageCritical = 0.25f;

    private static string BrainText(HealthAnalyzerWoundDiagnostics diagnostics)
    {
        var line = diagnostics.BrainActivity switch
        {
            < BrainDamageCritical => "health-analyzer-wound-brain-damage-critical",
            < BrainDamageFinding => "health-analyzer-wound-brain-damage",
            _ => "health-analyzer-wound-brain-activity",
        };

        // A positronic brain runs no oxygenation clock, and the server sends -1 for it.
        if (diagnostics.Oxygenation < 0f)
            line += "-core";

        var text = Loc.GetString(line,
            ("activity", (int) MathF.Round(diagnostics.BrainActivity * 100f)),
            ("oxygen", (int) MathF.Round(MathF.Max(0f, diagnostics.Oxygenation) * 100f)));

        // Playtest 5: the trauma a repaired brain carries used to be invisible here, so its stutter and blur on a
        // patient whose organs all read fine looked like a permanent bug.
        if (diagnostics.BrainTraumaSeconds > 0f)
            text += " " + Loc.GetString("health-analyzer-wound-brain-trauma",
                ("minutes", (int) MathF.Ceiling(diagnostics.BrainTraumaSeconds / 60f)));

        return text;
    }

    // Playtest 4 (SEPSIS): the banner says so once the sepsis is damaging the organs, which the vitals routes carry.
    // INFECTION: past wolfmed.septic_shock_at it reads "SEPTIC SHOCK".
    private static string SepsisText(HealthAnalyzerWoundDiagnostics diagnostics)
    {
        var stem = diagnostics.Vitals is { SepticShock: true }
            ? "health-analyzer-wound-septic-shock"
            : "health-analyzer-wound-sepsis";
        var organs = diagnostics.Vitals is { } vitals && (vitals.Routes & WolfmedRoutes.SepsisOrgans) != 0;
        return Loc.GetString(organs ? stem + "-organs" : stem, ("percent", (int) MathF.Round(diagnostics.Sepsis)));
    }

    /// <summary>CONSC: the tier, the time left, and the reminder that none of it treats anything.</summary>
    private static string PainReliefText(HealthAnalyzerWoundDiagnostics diagnostics) =>
        Loc.GetString("health-analyzer-wound-pain-relief",
            ("tier", Loc.GetString(
                $"wolfmed-pain-relief-tier-{diagnostics.PainRelief.ToString().ToLowerInvariant()}")),
            ("seconds", (int) MathF.Round(diagnostics.PainReliefSeconds)));

    private static string SedationText(float sedation) =>
        Loc.GetString("health-analyzer-wound-sedation", ("percent", (int) MathF.Round(sedation * 100f)));

    /// <summary>
    /// CONSC: the alert row without the click-through. A painkiller is not a finding with a procedure, so
    /// there is nothing for the treatment window to open.
    /// </summary>
    private RichTextLabel CreateBannerRow(string icon, Color colour, string text, out Control row)
    {
        var panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = WolfmedWoundStyle.AlertBackground,
                BorderColor = colour,
                BorderThickness = new Thickness(3, 0, 0, 0),
            },
            Margin = new Thickness(0, 0, 0, 3),
            HorizontalExpand = true,
        };

        var content = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            HorizontalExpand = true,
            Margin = new Thickness(6, 3, 4, 3),
        };
        content.AddChild(Icon(icon, colour, IconSize));

        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(FormattedMessage.FromMarkupPermissive(text));
        content.AddChild(label);

        panel.AddChild(content);
        row = panel;
        return label;
    }

    /// <summary>One chip per category present on the patient. Clicking one filters the cards below.</summary>
    private void BuildCategoryStrip(HealthAnalyzerWoundDiagnostics diagnostics)
    {
        var counts = new Dictionary<WolfmedWoundCategory, int>();
        foreach (var diagnostic in diagnostics.Parts.Values)
        {
            foreach (var wound in diagnostic.VisibleWounds)
                counts[wound.Category] = counts.GetValueOrDefault(wound.Category) + wound.Count;
        }

        // A filter whose category has healed away would hide every card with no way back.
        if (_categoryFilter is { } filter && !counts.ContainsKey(filter))
            _categoryFilter = null;

        foreach (var category in WolfmedWoundCategories.All)
        {
            if (!counts.TryGetValue(category, out var count))
                continue;

            WoundCategoryStrip.AddChild(CreateCategoryChip(category, count));
        }
    }

    private Control CreateCategoryChip(WolfmedWoundCategory category, int count)
    {
        var name = Loc.GetString(WolfmedWoundCategories.NameKey(category));
        var colour = WolfmedWoundStyle.Category(category);
        var selected = _categoryFilter == category;

        var button = new Button
        {
            StyleClasses = { "OpenBoth" },
            // UI3: the count, then what the category means and what closes it.
            ToolTip = Tooltip(
                Loc.GetString("health-analyzer-wound-category-chip", ("category", name), ("count", count)),
                Advice(WolfmedTreatmentAdvice.CategoryShortKey(category), false)),
            Margin = new Thickness(0, 0, 3, 3),
            MinHeight = 22,
        };
        button.OnPressed += _ =>
        {
            _categoryFilter = selected ? null : category;
            RefreshWoundFilter();
        };

        var row = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 3,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(4, 0),
        };
        row.AddChild(Icon(WolfmedWoundCategories.IconState(category), colour, ChipIconSize));
        row.AddChild(new Label
        {
            Text = count.ToString(),
            StyleClasses = { "LabelSubText" },
            FontColorOverride = selected ? Color.White : colour,
            VerticalAlignment = VAlignment.Center,
        });
        button.AddChild(row);
        button.Modulate = selected ? Color.White : new Color(0.8f, 0.8f, 0.8f);
        return button;
    }

    /// <summary>
    /// Redraws the cards from the message the panel is already holding. Cheaper than waiting for the next
    /// scan tick, which is up to a second away and would make the chip feel dead.
    /// </summary>
    private void RefreshWoundFilter()
    {
        if (_lastMessage is { } message)
            DrawWoundDiagnostics(message);
    }

    private Control CreateAlertRow(string icon, Color colour, string text, string condition,
        string? label = null, bool mechanical = false) =>
        CreateAlertRow(icon, colour, text, condition, out _, label, mechanical);

    /// <summary>A banner row; <paramref name="label"/> names it when the condition's own name does not fit.</summary>
    private Control CreateAlertRow(string icon, Color colour, string text, string condition,
        out RichTextLabel body, string? label = null, bool mechanical = false)
    {
        var panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = WolfmedWoundStyle.AlertBackground,
                BorderColor = colour,
                BorderThickness = new Thickness(3, 0, 0, 0),
            },
            Margin = new Thickness(0, 0, 0, 3),
            HorizontalExpand = true,
        };

        var row = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            HorizontalExpand = true,
            Margin = new Thickness(6, 3, 4, 3),
        };
        row.AddChild(Icon(icon, colour, IconSize));

        body = new RichTextLabel { HorizontalExpand = true };
        body.SetMessage(FormattedMessage.FromMarkupPermissive(text));
        row.AddChild(body);

        panel.AddChild(row);
        // UI3: a banner is a finding like any other, so it opens the same procedure window. The tooltip uses
        // the plain title rather than the banner text, which carries colour markup a tooltip cannot render.
        var title = Loc.GetString($"health-analyzer-wound-banner-{label ?? condition}");
        return Clickable(panel, null, title, condition, mechanical, title);
    }

    private Control CreatePartCard(TargetBodyPart part, HealthAnalyzerWoundDiagnostic diagnostic)
    {
        // UI3: the targeted part gets a full border in its accent colour rather than the left bar alone.
        var targeted = part == _targetedPart;
        var accent = WolfmedWoundStyle.Accent(diagnostic);
        var card = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = targeted ? WolfmedWoundStyle.CardTargeted : WolfmedWoundStyle.CardBackground,
                BorderColor = targeted ? WolfmedWoundStyle.TargetedBorder : accent,
                BorderThickness = targeted ? new Thickness(3, 1, 1, 1) : new Thickness(3, 0, 0, 0),
            },
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalExpand = true,
        };

        var body = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new Thickness(6, 3, 4, 4),
        };

        body.AddChild(CreateCardHeader(part, diagnostic, targeted));

        foreach (var row in CreateWoundRows(part, diagnostic))
            body.AddChild(row);

        if (CreateFooter(diagnostic, out var painLabel) is { } footer)
        {
            body.AddChild(footer);
            if (painLabel != null)
                _painLabels[part] = painLabel;
        }

        card.AddChild(body);
        return card;
    }

    /// <summary>Part name on the left, the part's conditions as icon chips on the right.</summary>
    private Control CreateCardHeader(TargetBodyPart part, HealthAnalyzerWoundDiagnostic diagnostic, bool targeted)
    {
        var header = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            HorizontalExpand = true,
        };

        // UI3: the part name is the second way to aim; it does exactly what the doll does.
        var name = new ContainerButton
        {
            ToolTip = Loc.GetString("health-analyzer-wound-target-part-hint"),
            VerticalAlignment = VAlignment.Center,
        };
        name.AddChild(new Label
        {
            Text = Loc.GetString($"targeting-part-{PartKey(part)}"),
            StyleClasses = { "LabelHeading" },
            VerticalAlignment = VAlignment.Center,
        });
        name.OnPressed += _ => OnPartSelected?.Invoke(part);
        header.AddChild(name);

        if (targeted)
        {
            header.AddChild(new Label
            {
                Text = Loc.GetString("health-analyzer-wound-targeted-tag"),
                StyleClasses = { "LabelSubText" },
                FontColorOverride = WolfmedWoundStyle.TargetedBorder,
                VerticalAlignment = VAlignment.Center,
            });
        }

        header.AddChild(new Control { HorizontalExpand = true });

        foreach (var chip in CreateConditionChips(part, diagnostic))
            header.AddChild(chip);

        return header;
    }

    /// <summary>
    /// The part-level findings, in the order the old text line printed them. Wording is unchanged, so the
    /// W6 mechanical switch still picks the chassis variants.
    /// </summary>
    private List<Control> CreateConditionChips(TargetBodyPart part, HealthAnalyzerWoundDiagnostic diagnostic)
    {
        // W6: a chassis does not bleed or fracture, it leaks and deforms.
        var mech = diagnostic.Mechanical;
        var mechanical = mech ? "-mechanical" : string.Empty;
        var frame = mech ? "-frame" : string.Empty;
        var chips = new List<Control>();

        if (diagnostic.Fracture != FractureGrade.None)
        {
            var grade = Loc.GetString($"fracture-grade-{diagnostic.Fracture.ToString().ToLowerInvariant()}");
            var text = diagnostic.FractureTreatment == FractureTreatment.None
                ? Loc.GetString($"health-analyzer-wound-fracture-short{frame}", ("grade", grade))
                : Loc.GetString($"health-analyzer-wound-fracture-treated-short{frame}",
                    ("grade", grade),
                    ("treatment", Loc.GetString(
                        $"health-analyzer-wound-fracture-treatment-{diagnostic.FractureTreatment.ToString().ToLowerInvariant()}")));
            chips.Add(CreateChip(part, "fracture", WolfmedWoundStyle.Fracture, grade, text, "fracture", mech));
        }

        if (diagnostic.BleedingRate > 0f)
        {
            chips.Add(CreateChip(part, 
                "bleeding",
                WolfmedWoundStyle.Bleeding,
                null,
                Loc.GetString($"health-analyzer-wound-bleeding-short{mechanical}"),
                "bleeding",
                mech));
        }

        if (diagnostic.InternalBleedingRate > 0f)
        {
            chips.Add(CreateChip(part, 
                "internal_bleeding",
                WolfmedWoundStyle.InternalBleeding,
                null,
                Loc.GetString("health-analyzer-wound-internal-bleeding-short"),
                "internal-bleeding",
                mech));
        }

        if (diagnostic.ClottingPhase is HealthAnalyzerClottingPhase.InProgress
            or HealthAnalyzerClottingPhase.Complete
            or HealthAnalyzerClottingPhase.Mixed)
        {
            chips.Add(CreateChip(part, 
                "clotting",
                WolfmedWoundStyle.Clotting,
                null,
                Loc.GetString(
                    $"health-analyzer-wound-clotting-{diagnostic.ClottingPhase.ToString().ToLowerInvariant()}{mechanical}"),
                "clotting",
                mech));
        }

        // W1: before the scars, because it is the finding that decides what the medic does next.
        if (diagnostic.EmbeddedObjects > 0)
        {
            chips.Add(CreateChip(part, 
                "embedded",
                WolfmedWoundStyle.Embedded,
                diagnostic.EmbeddedObjects.ToString(),
                Loc.GetString("health-analyzer-wound-embedded-short", ("count", diagnostic.EmbeddedObjects)),
                "embedded",
                mech));
        }

        // W5: dead tissue outranks everything else on the part; nothing but amputation clears it.
        if (diagnostic.Necrotic)
        {
            chips.Add(CreateChip(part, "necrosis", WolfmedWoundStyle.Necrosis, null,
                Loc.GetString("health-analyzer-wound-necrotic-short"), "necrosis", mech));
        }
        else if (diagnostic.NecrosisRisk)
        {
            chips.Add(CreateChip(part, "necrosis", WolfmedWoundStyle.Pain, null,
                Loc.GetString("health-analyzer-wound-necrosis-risk-short"), "necrosis-risk", mech));
        }

        if (diagnostic.Infection != WolfmedInfectionStage.None)
        {
            var text = Loc.GetString(
                $"health-analyzer-wound-infection-{diagnostic.Infection.ToString().ToLowerInvariant()}");
            chips.Add(CreateChip(part, "infection", WolfmedWoundStyle.Infection,
                diagnostic.Infection.ToString().ToLowerInvariant(), text,
                WolfmedTreatmentAdvice.InfectionCondition(diagnostic.Infection), mech));
        }

        // INFECTION: the part's own infection, travelling towards the torso; it needs no wound on this part.
        if (diagnostic.PartInfection != WolfmedInfectionStage.None)
        {
            var stage = diagnostic.PartInfection.ToString().ToLowerInvariant();
            chips.Add(CreateChip(part, "infection", WolfmedWoundStyle.Infection,
                Loc.GetString($"health-analyzer-part-infection-{stage}-short"),
                Loc.GetString($"health-analyzer-part-infection-{stage}"),
                WolfmedTreatmentAdvice.PartInfectionCondition(diagnostic.PartInfection), mech));
        }

        // W6: a hot part reads as hot even once the wound itself has cooled past its first stage.
        if (diagnostic.Overheating)
        {
            chips.Add(CreateChip(part, "overheating", WolfmedWoundStyle.Overheating, null,
                Loc.GetString("health-analyzer-wound-overheating-short"), "overheating", mech));
        }

        if (diagnostic.Functionality != BodyPartFunctionalityState.Functional)
        {
            chips.Add(CreateChip(part, "impaired", WolfmedWoundStyle.Impaired, null,
                Loc.GetString(
                    $"health-analyzer-wound-functionality-{diagnostic.Functionality.ToString().ToLowerInvariant()}"),
                WolfmedTreatmentAdvice.FunctionalityCondition(diagnostic.Functionality),
                mech));
        }

        return chips;
    }

    /// <summary>Visible wounds, one block per category, in the order the server sorted them.</summary>
    private List<Control> CreateWoundRows(TargetBodyPart part, HealthAnalyzerWoundDiagnostic diagnostic)
    {
        var rows = new List<Control>();
        foreach (var wound in diagnostic.VisibleWounds)
        {
            var colour = WolfmedWoundStyle.Category(wound.Category);
            var name = Loc.GetString(wound.Name);
            var row = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                SeparationOverride = 5,
                HorizontalExpand = true,
                Margin = new Thickness(1, 2, 0, 0),
            };

            row.AddChild(Icon(WolfmedWoundCategories.IconState(wound.Category), colour, IconSize));
            row.AddChild(new Label
            {
                Text = name,
                VerticalAlignment = VAlignment.Center,
            });

            if (wound.StageName is { } stage)
            {
                row.AddChild(new Label
                {
                    Text = Loc.GetString(stage),
                    FontColorOverride = WolfmedWoundStyle.StageText,
                    VerticalAlignment = VAlignment.Center,
                });
            }

            if (wound.Count > 1)
            {
                row.AddChild(new Label
                {
                    Text = Loc.GetString("health-analyzer-wound-count", ("count", wound.Count)),
                    FontColorOverride = colour,
                    VerticalAlignment = VAlignment.Center,
                });
            }

            // UI3: the row is the click target for that wound's procedure; the category name stays on hover.
            var category = Loc.GetString(WolfmedWoundCategories.NameKey(wound.Category));
            if (string.IsNullOrEmpty(wound.Prototype))
            {
                row.ToolTip = category;
                rows.Add(row);
                continue;
            }

            rows.Add(Clickable(
                row,
                new WolfmedTreatmentSubject(part, wound.Prototype, string.Empty, diagnostic.Mechanical),
                Capitalize(name),
                WolfmedTreatmentAdvice.ShortKey(wound.Prototype),
                category));
        }

        return rows;
    }

    /// <summary>
    /// FIX1: the footer's wording, or null when the card has no footer. Pulled out of the builder because
    /// the pain figure moves on nearly every scan and is written straight back onto the kept label.
    /// </summary>
    private static string? FooterText(HealthAnalyzerWoundDiagnostic diagnostic)
    {
        // W7: a chassis reports the same figure, but it is not pain.
        var mechanical = diagnostic.Mechanical ? "-mechanical" : string.Empty;
        var parts = new List<string>();

        if (diagnostic.Pain > FixedPoint2.Zero)
            parts.Add(Loc.GetString($"health-analyzer-wound-pain-short{mechanical}", ("pain", diagnostic.Pain)));

        if (diagnostic.ScarCount > 0)
            parts.Add(Loc.GetString("health-analyzer-wound-scars-short", ("count", diagnostic.ScarCount)));

        return parts.Count == 0 ? null : string.Join("   ", parts);
    }

    /// <summary>Pain and scars: real findings, but never the reason a medic looks at the card.</summary>
    private Control? CreateFooter(HealthAnalyzerWoundDiagnostic diagnostic, out Label? text)
    {
        text = null;
        if (FooterText(diagnostic) is not { } wording)
            return null;

        var row = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 5,
            HorizontalExpand = true,
            Margin = new Thickness(1, 3, 0, 0),
        };
        row.AddChild(Icon(diagnostic.ScarCount > 0 && diagnostic.Pain <= FixedPoint2.Zero ? "scar" : "pain",
            WolfmedWoundStyle.Scar,
            ChipIconSize));
        text = new Label
        {
            Text = wording,
            StyleClasses = { "LabelSubText" },
            VerticalAlignment = VAlignment.Center,
        };
        row.AddChild(text);
        return row;
    }

    /// <summary>A pill with a tinted pictogram, an optional short value, and the full wording on hover.</summary>
    private Control CreateChip(
        TargetBodyPart part,
        string icon,
        Color colour,
        string? text,
        string headline,
        string condition,
        bool mechanical)
    {
        var panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = WolfmedWoundStyle.ChipBackground },
            Margin = new Thickness(0, 0, 2, 0),
            VerticalAlignment = VAlignment.Center,
        };

        var row = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 2,
            Margin = new Thickness(3, 2),
        };
        row.AddChild(Icon(icon, colour, ChipIconSize));

        if (!string.IsNullOrEmpty(text))
        {
            row.AddChild(new Label
            {
                Text = text,
                StyleClasses = { "LabelSubText" },
                FontColorOverride = colour,
                VerticalAlignment = VAlignment.Center,
            });
        }

        panel.AddChild(row);
        // UI3: the chip's own wording, then what to do about it, then the procedure on click.
        return Clickable(panel, part, Capitalize(headline), condition, mechanical, headline);
    }

    /// <summary>Wraps a condition's control so it hovers with advice and opens the procedure on click.</summary>
    private Control Clickable(
        Control content,
        TargetBodyPart? part,
        string title,
        string condition,
        bool mechanical,
        string headline) =>
        Clickable(
            content,
            new WolfmedTreatmentSubject(part, string.Empty, condition, mechanical),
            title,
            WolfmedTreatmentAdvice.ConditionShortKey(condition),
            headline);

    private Control Clickable(
        Control content,
        WolfmedTreatmentSubject subject,
        string title,
        string shortKey,
        string headline)
    {
        var advice = Advice(shortKey, subject.Mechanical);
        var button = new ContainerButton
        {
            HorizontalExpand = content.HorizontalExpand,
            VerticalAlignment = content.VerticalAlignment,
            ToolTip = Tooltip(headline, advice),
        };
        button.AddChild(content);
        button.OnPressed += _ => OpenTreatment(subject, title, advice);
        return button;
    }

    /// <summary>The <c>-mechanical</c> variant where the data has one, else the plain key, else nothing.</summary>
    private static string Advice(string key, bool mechanical)
    {
        if (mechanical &&
            Loc.TryGetString(key + WolfmedTreatmentAdvice.MechanicalSuffix, out var chassis))
            return chassis;

        return Loc.TryGetString(key, out var advice) ? advice : string.Empty;
    }

    private static string Tooltip(string headline, string advice) =>
        string.IsNullOrEmpty(advice) ? headline : headline + "\n" + advice;

    /// <summary>
    /// UI4: opens the procedure for one finding. The title names the finding and the part it is on, so a
    /// medic with two windows' worth of clicks behind them can tell which arm they are reading about.
    /// </summary>
    private void OpenTreatment(WolfmedTreatmentSubject subject, string title, string summary)
    {
        if (Procedure(subject) is not { } procedure)
            return;

        if (subject.Part is { } part)
            title = Loc.GetString("wolfmed-treatment-title",
                ("finding", title),
                ("part", Loc.GetString($"targeting-part-{PartKey(part)}")));

        _treatmentWindow ??= new WolfmedTreatmentWindow(_prototypes, _spriteSystem, _icons);
        _treatmentWindow.Show(subject, title, summary, procedure, BuildProcedureState(subject));
    }

    /// <summary>
    /// UI4: re-evaluates the open window against the scan that was just drawn. It keeps its rows, its scroll
    /// and the focus it had; only the ticks and the greying move.
    /// </summary>
    private void RefreshTreatment()
    {
        if (_treatmentWindow is { IsOpen: true } window)
            window.SetState(BuildProcedureState(window.Subject));
    }

    /// <summary>The chassis procedure where the data has one, else the plain one, else nothing.</summary>
    private WolfmedTreatmentProcedurePrototype? Procedure(WolfmedTreatmentSubject subject)
    {
        if (subject.Mechanical &&
            _prototypes.TryIndex<WolfmedTreatmentProcedurePrototype>(ProcedureId(subject, true), out var chassis))
            return chassis;

        return _prototypes.TryIndex<WolfmedTreatmentProcedurePrototype>(ProcedureId(subject, false), out var plain)
            ? plain
            : null;
    }

    private static string ProcedureId(WolfmedTreatmentSubject subject, bool mechanical) =>
        string.IsNullOrEmpty(subject.Wound)
            ? WolfmedTreatmentAdvice.ConditionProcedureId(subject.Condition, mechanical)
            : WolfmedTreatmentAdvice.ProcedureId(subject.Wound, mechanical);

    /// <summary>Everything the step checks read, pulled out of the scan the panel is holding.</summary>
    private WolfmedProcedureState BuildProcedureState(WolfmedTreatmentSubject subject)
    {
        if (_lastMessage is not { } msg)
            return new WolfmedProcedureState(null);

        HealthAnalyzerWoundDiagnostic? part = null;
        if (subject.Part is { } target &&
            msg.WoundDiagnostics != null &&
            msg.WoundDiagnostics.Parts.TryGetValue(target, out var found))
            part = found;

        return new WolfmedProcedureState(
            part,
            subject.Part != null && subject.Part == _targetedPart,
            SubjectPresent(subject, part, msg),
            msg.WoundDiagnostics?.Sepsis ?? 0f,
            msg.BloodLevel,
            msg.WoundDiagnostics?.CardiacArrest ?? false, // BRAIN
            msg.WoundDiagnostics?.BrainActivity ?? -1f); // BRAIN
    }

    /// <summary>Whether the finding the window was opened for is still being reported.</summary>
    private static bool SubjectPresent(
        WolfmedTreatmentSubject subject,
        HealthAnalyzerWoundDiagnostic? part,
        HealthAnalyzerScannedUserMessage msg)
    {
        if (!string.IsNullOrEmpty(subject.Wound))
            return part is { } wounded &&
                   wounded.VisibleWounds.Any(wound => wound.Prototype == subject.Wound);

        // The two body-level banners have no part card behind them.
        if (subject.Condition == "sepsis")
            return (msg.WoundDiagnostics?.Sepsis ?? 0f) > 0f;

        if (subject.Condition == "blood-low")
            return IsDangerousBloodLevel(msg.BloodLevel);

        // BRAIN: both are body-level too.
        if (subject.Condition == "cardiac-arrest")
            return msg.WoundDiagnostics is { CardiacArrest: true } or { Shutdown: true };

        if (subject.Condition == "brain-death")
            return msg.WoundDiagnostics is { BrainDead: true };

        return part is { } diagnostic && ConditionPresent(subject.Condition, diagnostic);
    }

    /// <summary>
    /// The same tests <see cref="CreateConditionChips"/> draws its chips from. A condition the panel would
    /// no longer chip is one the window can call resolved.
    /// </summary>
    private static bool ConditionPresent(string condition, HealthAnalyzerWoundDiagnostic diagnostic) =>
        condition switch
        {
            "bleeding" => diagnostic.BleedingRate > 0f,
            "internal-bleeding" => diagnostic.InternalBleedingRate > 0f,
            "fracture" => diagnostic.Fracture != FractureGrade.None,
            "clotting" => diagnostic.ClottingPhase is HealthAnalyzerClottingPhase.InProgress
                or HealthAnalyzerClottingPhase.Complete
                or HealthAnalyzerClottingPhase.Mixed,
            "embedded" => diagnostic.EmbeddedObjects > 0,
            "necrosis" => diagnostic.Necrotic,
            "necrosis-risk" => diagnostic.NecrosisRisk && !diagnostic.Necrotic,
            "overheating" => diagnostic.Overheating,
            _ => condition == WolfmedTreatmentAdvice.InfectionCondition(diagnostic.Infection) ||
                 diagnostic.PartInfection != WolfmedInfectionStage.None && // INFECTION
                 condition == WolfmedTreatmentAdvice.PartInfectionCondition(diagnostic.PartInfection) ||
                 condition == WolfmedTreatmentAdvice.FunctionalityCondition(diagnostic.Functionality),
        };

    private TextureRect Icon(string state, Color colour, float size) => new()
    {
        Texture = _icons.Get(state),
        SetSize = new Vector2(size, size),
        Stretch = TextureRect.StretchMode.KeepAspectCentered,
        Modulate = colour,
        VerticalAlignment = VAlignment.Center,
    };

    internal static bool IsDangerousBloodLevel(float level) => !float.IsNaN(level) && level < DangerousBloodLevel;

    private static string PartKey(TargetBodyPart part) => part.ToString()
        .Replace("Left", "left-")
        .Replace("Right", "right-")
        .ToLowerInvariant();
}
