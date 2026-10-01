using System.Globalization;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Consciousness;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Life;

/// <summary>The rung the analyzer names at the top of its vitals block (M1a, plan §5.5).</summary>
[Serializable, NetSerializable]
public enum WolfmedVitalsState : byte
{
    Up = 0,
    Downed = 1,

    /// <summary>Critical for a fixed time: a pain faint.</summary>
    Faint = 2,
    Unconscious = 3,
    Arrest = 4,

    /// <summary>A machine that has stopped: no power, no pump, or no hydraulic pressure.</summary>
    Shutdown = 5,
    Dead = 6,

    /// <summary>M4 (plan §3.11): a machine's core past its heat line. Dying, like arrest.</summary>
    ThermalShutdown = 7,
}

/// <summary>Which way the blood (or oil) is going, from the bleed against regeneration.</summary>
[Serializable, NetSerializable]
public enum WolfmedBloodTrend : byte
{
    Steady = 0,
    Rising = 1,
    Falling = 2,

    /// <summary>Losing at least <c>wolfmed.analyzer_blood_fast</c> units a second.</summary>
    FallingFast = 3,
}

/// <summary>What the paddles would do right now, in the analyzer's words. Never "will work": a shock is a roll.</summary>
[Serializable, NetSerializable]
public enum WolfmedDefibVerdict : byte
{
    /// <summary>Not shown: the patient is conscious, or the body is not the defibrillator's to judge.</summary>
    Hidden = 0,
    Indicated = 1,
    PulsePresent = 2,
    NoHeart = 3,
    NoBlood = 4,
    BrainDead = 5,
    NoBrain = 6,
    Rotten = 7,

    /// <summary>Other content's <c>UnrevivableComponent</c>; <see cref="WolfmedVitalsReport.VerdictReason"/> names it.</summary>
    Unrevivable = 8,

    /// <summary>M5: the core is still under the cold arrest line; rewarm first.</summary>
    TooCold = 9,
}

/// <summary>M5 (plan §3.8): the toxin load against its lines.</summary>
[Serializable, NetSerializable]
public enum WolfmedToxinBand : byte
{
    Low = 0,

    /// <summary>At or past the Downed line.</summary>
    High = 1,

    /// <summary>At or past the coma line: the brain is draining.</summary>
    Coma = 2,
}

/// <summary>M5 (plan §3.8, OD13): what the liver is doing for toxin clearance.</summary>
[Serializable, NetSerializable]
public enum WolfmedLiverState : byte
{
    None = 0,
    Working = 1,
    Impaired = 2,
    Failed = 3,
}

/// <summary>M5 (plan §3.9): the marrow route's stage.</summary>
[Serializable, NetSerializable]
public enum WolfmedRadiationBand : byte
{
    Low = 0,

    /// <summary>Past wolfmed.rad_marrow_stop: no blood regenerates.</summary>
    Suppressed = 1,

    /// <summary>Past wolfmed.rad_marrow_bleed: blood is lost as well.</summary>
    Failing = 2,
}

/// <summary>
/// The analyzer's vitals block (M1a, plan §5.5): state and cause, breathing, circulation and the defib verdict.
/// Built on the server from the networked vitals and the life system; <see cref="WolfmedVitalsText"/> words it.
/// </summary>
[Serializable, NetSerializable]
public sealed class WolfmedVitalsReport
{
    public WolfmedVitalsState State;
    public WolfmedCause Cause;
    public WolfmedCauseSource Source;
    public WolfmedCauseFlags Blockers;

    /// <summary>A chassis: machine words, no pulse, no breathing, oil instead of blood.</summary>
    public bool Mechanical;

    /// <summary>Dead with the brain organ at 0 HP: catastrophic brain injury, not permanent loss.</summary>
    public bool BrainDestroyed;

    public WolfmedBreathing Breathing;
    public WolfmedBreathingSource BreathingSource;

    /// <summary>Sedation 0 to 1, for the depressed breathing line.</summary>
    public float Sedation;

    /// <summary>Mechanical: the coolant pump is present and working.</summary>
    public bool PumpRunning;

    public WolfmedBloodBand BloodBand;

    /// <summary>Blood (or oil) volume fraction, or -1 for a body with no bloodstream.</summary>
    public float Blood = -1f;

    public WolfmedBloodTrend Trend;

    /// <summary>Units to <see cref="Line"/>, 0 when already above it.</summary>
    public float UnitsToLine;

    /// <summary>The brain-safe blood line as a percentage (wolfmed.brain_blood_start): under it the brain drains.</summary>
    public float Line;

    public WolfmedDefibVerdict Verdict;

    /// <summary>The unrevivable reason's locale key, for <see cref="WolfmedDefibVerdict.Unrevivable"/>.</summary>
    public string? VerdictReason;

    /// <summary>For a blood refusal: units to the post-shock target plus the bleed over the grace.</summary>
    public float VerdictUnits;

    /// <summary>For a blood refusal: units to the brain-safe line, and that line as a percentage.</summary>
    public float VerdictSafeUnits;

    public float VerdictSafeLine;

    /// <summary>M1b: burn fluid loss in units a second, 0 when nothing weeps.</summary>
    public float BurnFluid;

    /// <summary>M1b: at or over <c>wolfmed.analyzer_burn_fast</c>.</summary>
    public bool BurnFluidFast;

    /// <summary>Playtest 2: whole seconds left in a pain faint, -1 when none runs.</summary>
    public int FaintSeconds = -1;

    /// <summary>M3 (plan §8): every organ with Wolfmed data that is impaired or failed, in medical reading order.</summary>
    public List<WolfmedOrganReading> Organs = new();

    /// <summary>M2 (plan §5.5): every process making the patient worse right now, each named with its first aid.</summary>
    public WolfmedRoutes Routes;

    /// <summary>INFECTION: sepsis past wolfmed.septic_shock_at. The sepsis banner and aids say "septic shock".</summary>
    public bool SepticShock;

    /// <summary>M2: why the heart last stopped, as an Arrest* source; None outside wolfmed.arrest_cause_memory_seconds.</summary>
    public WolfmedCauseSource RestartCause;

    /// <summary>M2: that cause is still there.</summary>
    public bool RestartPresent;

    /// <summary>M2: for blood still present, the units to the post-shock target plus the bleed over the grace; -1 none.</summary>
    public float RestartUnits = -1f;

    /// <summary>M2: units to the brain-safe line, the grace left in seconds, and that line as a percentage.</summary>
    public float RestartSafeUnits;

    public float RestartGraceSeconds;
    public float RestartSafeLine;

    /// <summary>M2 (plan §7.2): a dead chassis's restart button, in words. Hidden for anything else.</summary>
    public WolfmedRestartVerdict Restart;

    /// <summary>M4 (OD16): a species built with no heart. Its arrest reads as circulatory collapse.</summary>
    public bool Heartless;

    /// <summary>M4 (plan §3.11): a machine's core and chassis temperatures in kelvin; -1 when not shown.</summary>
    public float CoreTemperature = -1f;

    public float ChassisTemperature = -1f;

    /// <summary>M5 (plan §3.8): the toxin load, -1 when there is none.</summary>
    public float Toxin = -1f;

    public WolfmedToxinBand ToxinBand;
    public WolfmedLiverState Liver;

    /// <summary>M5 (plan §3.9): systemic radiation, -1 when there is none.</summary>
    public float Radiation = -1f;

    public WolfmedRadiationBand RadiationBand;

    /// <summary>M5: blood the failing marrow costs, units a second.</summary>
    public float MarrowLoss;

    /// <summary>M5 (plan §3.10): the core temperature in kelvin while it is cold or hot enough to count, else -1.</summary>
    public float Core = -1f;

    /// <summary>M5: <see cref="Core"/> is on the cold side.</summary>
    public bool CoreCold;

    /// <summary>M5: for a cold refusal, the core and the line it has to be rewarmed past, in kelvin.</summary>
    public float VerdictCore;

    public float VerdictRewarm;
}

/// <summary>M2 (plan §7.2): what a dead chassis's restart button would do, in the analyzer's words.</summary>
[Serializable, NetSerializable]
public enum WolfmedRestartVerdict : byte
{
    Hidden = 0,
    Ready = 1,
    NoHead = 2,
    NoCore = 3,
    CoreDestroyed = 4,
    NoPump = 5,
    NoPower = 6,

    /// <summary>Rot or other content's unrevivable reason.</summary>
    Refused = 7,
}

/// <summary>M3: one organ the analyzer names, by its body slot, and how it is doing.</summary>
[Serializable, NetSerializable]
public readonly record struct WolfmedOrganReading(string Slot, WolfmedOrganBand Band);

/// <summary>
/// The words for <see cref="WolfmedVitalsReport"/>. One place, so the analyzer panel, the pod and the tests
/// read the same lines.
/// </summary>
/// <remarks>
/// Playtest 3: three lines where there were up to nine. The state; one line of what is not normal; what to do
/// first. The after-restart memory and the two verdicts follow only when they apply.
/// </remarks>
public static class WolfmedVitalsText
{
    /// <summary>Every line the block shows, top to bottom.</summary>
    public static List<string> Lines(WolfmedVitalsReport report)
    {
        var lines = new List<string> { StateLine(report), VitalsLine(report) };
        if (DoFirstLine(report) is { } doFirst)
            lines.Add(doFirst);

        if (RestartLine(report) is { } restart)
            lines.Add(restart);

        if (VerdictLine(report) is { } verdict)
            lines.Add(verdict);

        if (RestartVerdictLine(report) is { } button)
            lines.Add(button);

        return lines;
    }

    /// <summary>
    /// Playtest 3: every number the block prints goes through here. Invariant culture, at most one decimal, no
    /// trailing zero: 1.3, not 1.2999999523162842; 2, not 2.0. Fluent gets the string, never the float.
    /// </summary>
    public static string Number(float value)
    {
        var rounded = MathF.Round(value, 1);
        if (rounded == 0f)
            rounded = 0f; // no "-0"

        return rounded.ToString("0.#", CultureInfo.InvariantCulture);
    }

    /// <summary>Units to give: whole, rounded up.</summary>
    public static string Units(float value) => Number(MathF.Ceiling(value));

    /// <summary>A whole reading: a percentage, kelvin, a dose.</summary>
    public static string Whole(float value) => Number(MathF.Round(value));

    /// <summary>
    /// Playtest 3, line 2: "Breathing laboured · Pulse weak, rapid · Blood 43% ↓ · Lungs impaired · Burn fluid loss
    /// 1.3 u/s". Only what is not normal, each once; "Vitals normal" when nothing is.
    /// </summary>
    public static string VitalsLine(WolfmedVitalsReport report)
    {
        var items = report.Mechanical ? MachineItems(report) : FleshItems(report);
        return items.Count == 0
            ? Loc.GetString("wolfmed-vitals-normal")
            : string.Join(Loc.GetString("wolfmed-vitals-separator"), items);
    }

    private static List<string> FleshItems(WolfmedVitalsReport report)
    {
        var items = new List<string>();
        if (BreathingItem(report) is { } breathing)
            items.Add(breathing);

        if (report.BloodBand != WolfmedBloodBand.Normal)
            items.Add(Loc.GetString($"wolfmed-vitals-pulse-{report.BloodBand.ToString().ToLowerInvariant()}"));

        if (report.Blood >= 0f && (report.BloodBand != WolfmedBloodBand.Normal || Falling(report.Trend)))
            items.Add(WithTrend("wolfmed-vitals-item-blood", report));

        AddOrgans(report, items);

        if (report.BurnFluid > 0f)
            items.Add(Loc.GetString("wolfmed-vitals-item-burn-fluid", ("rate", Number(report.BurnFluid))));

        // M5 (plan §3.8-3.10). An impaired or failed liver is already one of the organs; a missing one is not.
        if (report.Toxin > 0f)
        {
            items.Add(Loc.GetString("wolfmed-vitals-item-toxins", ("load", Whole(report.Toxin)),
                ("band", Loc.GetString($"wolfmed-vitals-toxin-band-{report.ToxinBand.ToString().ToLowerInvariant()}"))));

            if (report.Liver == WolfmedLiverState.None)
                items.Add(Loc.GetString("wolfmed-vitals-item-liver-missing"));
        }

        if (report.Radiation > 0f)
        {
            items.Add(Loc.GetString($"wolfmed-vitals-radiation-{report.RadiationBand.ToString().ToLowerInvariant()}",
                ("dose", Whole(report.Radiation))));
        }

        if (report.Core >= 0f)
        {
            items.Add(Loc.GetString(report.CoreCold ? "wolfmed-vitals-core-cold" : "wolfmed-vitals-core-hot",
                ("kelvin", Whole(report.Core))));
        }

        return items;
    }

    /// <summary>A chassis: cooling, oil, the organs (core, pump), and the core and chassis heat (M4).</summary>
    private static List<string> MachineItems(WolfmedVitalsReport report)
    {
        var items = new List<string>();
        if (!report.PumpRunning)
            items.Add(Loc.GetString("wolfmed-vitals-cooling-offline"));

        if (report.Blood >= 0f && (report.BloodBand != WolfmedBloodBand.Normal || Falling(report.Trend) ||
                                   report.UnitsToLine > 0f))
            items.Add(WithTrend("wolfmed-vitals-item-oil", report));

        AddOrgans(report, items);

        if (report.CoreTemperature >= 0f)
        {
            items.Add(Loc.GetString("wolfmed-vitals-temperature", ("core", Whole(report.CoreTemperature)),
                ("chassis", Whole(report.ChassisTemperature))));
        }

        return items;
    }

    private static bool Falling(WolfmedBloodTrend trend) =>
        trend is WolfmedBloodTrend.Falling or WolfmedBloodTrend.FallingFast;

    /// <summary>"Blood 43% ↓": the arrow is the locale's; steady has none.</summary>
    private static string WithTrend(string key, WolfmedVitalsReport report) =>
        Loc.GetString(key, ("percent", Whole(MathF.Max(0f, report.Blood) * 100f)),
            ("trend", Loc.GetString($"wolfmed-vitals-trend-{report.Trend.ToString().ToLowerInvariant()}"))).TrimEnd();

    /// <summary>Only when the chest is not working normally: "Breathing laboured", "Not breathing: no air".</summary>
    private static string? BreathingItem(WolfmedVitalsReport report)
    {
        return report.Breathing switch
        {
            WolfmedBreathing.Depressed => Loc.GetString("wolfmed-vitals-breathing-depressed",
                ("percent", Whole(report.Sedation * 100f))),
            WolfmedBreathing.Gasping => Loc.GetString("wolfmed-vitals-breathing-gasping"),
            WolfmedBreathing.Laboured => Loc.GetString("wolfmed-vitals-breathing-laboured"), // M3
            // An arrest (or M4's circulatory collapse) is on the state line already: plain "Not breathing".
            WolfmedBreathing.None => Loc.GetString(
                $"wolfmed-vitals-breathing-none-{report.BreathingSource.ToString().ToLowerInvariant()}"),
            _ => null,
        };
    }

    /// <summary>M3 (plan §8): "Lungs impaired", "Heart failed", in the organ tab's reading order. No effects here.</summary>
    private static void AddOrgans(WolfmedVitalsReport report, List<string> items)
    {
        foreach (var (slot, band) in report.Organs)
        {
            var key = slot.ToLowerInvariant();
            var name = Loc.TryGetString($"wolfmed-vitals-organ-{key}", out var named) ? named : slot;
            items.Add(Loc.GetString($"wolfmed-vitals-organ-band-{band.ToString().ToLowerInvariant()}", ("organ", name)));
        }
    }

    /// <summary>The "Do first:" line listing each running route's first aid, or null when it isn't shown.</summary>
    // E.g. "Do first: transfuse ≈ 20 u; dress the burns and give fluids; lung surgery", in the routes' order; the
    // transfusion (or a chassis's refill) carries its units. "Do first: nothing; stable" for a patient down with
    // nothing running. Not shown on the dead, nor on somebody up with nothing to do.
    public static string? DoFirstLine(WolfmedVitalsReport report)
    {
        if (report.State == WolfmedVitalsState.Dead)
            return null;

        var aids = new List<string>();
        var refill = report.UnitsToLine > 0f && report.Blood >= 0f;
        for (var bit = 0; bit < 16; bit++)
        {
            var route = (WolfmedRoutes) (1 << bit);
            // Playtest 4 (SEPSIS): the organ-damage aid says antibiotics too, so the plain sepsis aid gives way to it.
            if (route == WolfmedRoutes.Sepsis && (report.Routes & WolfmedRoutes.SepsisOrgans) != 0)
                continue;

            var circulation = route == WolfmedRoutes.Circulation;
            if ((report.Routes & route) == 0 && !(circulation && refill))
                continue;

            var aid = circulation && refill
                ? Loc.GetString(report.Mechanical ? "wolfmed-vitals-aid-refill" : "wolfmed-vitals-aid-transfuse",
                    ("units", Units(report.UnitsToLine)))
                : Aid(route, report.Mechanical, report.SepticShock);

            if (!aids.Contains(aid))
                aids.Add(aid);
        }

        if (aids.Count == 0)
            return report.State == WolfmedVitalsState.Up ? null : Loc.GetString("wolfmed-vitals-do-first-none");

        return Loc.GetString("wolfmed-vitals-do-first", ("aids", string.Join("; ", aids)));
    }

    /// <summary>
    /// A route's first aid, a few words: "lung surgery", "antibiotics". A chassis's own words where it has them;
    /// INFECTION: the sepsis routes' septic shock words while <paramref name="shock"/>.
    /// </summary>
    public static string Aid(WolfmedRoutes route, bool mechanical, bool shock = false)
    {
        var key = $"wolfmed-vitals-aid-{route.ToString().ToLowerInvariant()}";
        if (shock && Loc.TryGetString(key + "-shock", out var shocked))
            return shocked;

        return mechanical && Loc.TryGetString(key + "-mechanical", out var machine) ? machine : Loc.GetString(key);
    }

    /// <summary>
    /// M2: "After a restart: arrest cause blood. Still present: yes. Transfuse ≈ 30 u within 45 s ...". Kept for
    /// wolfmed.arrest_cause_memory_seconds after the heart started again.
    /// </summary>
    public static string? RestartLine(WolfmedVitalsReport report)
    {
        if (report.RestartCause == WolfmedCauseSource.None)
            return null;

        var cause = SourceName(report.RestartCause);
        if (!report.RestartPresent || report.RestartUnits < 0f)
        {
            return Loc.GetString("wolfmed-vitals-restart", ("cause", cause),
                ("present", Loc.GetString(report.RestartPresent ? "wolfmed-vitals-yes" : "wolfmed-vitals-no")));
        }

        return report.RestartGraceSeconds > 0f
            ? Loc.GetString("wolfmed-vitals-restart-transfuse", ("cause", cause),
                ("units", Units(report.RestartUnits)), ("seconds", Units(report.RestartGraceSeconds)),
                ("safe", Units(report.RestartSafeUnits)), ("line", Whole(report.RestartSafeLine)))
            : Loc.GetString("wolfmed-vitals-restart-transfuse-late", ("cause", cause),
                ("safe", Units(report.RestartSafeUnits)), ("line", Whole(report.RestartSafeLine)));
    }

    /// <summary>M2 (plan §7.2): "Restart: ready" or "Restart: refused: core destroyed, core repair first". Dead chassis only.</summary>
    public static string? RestartVerdictLine(WolfmedVitalsReport report) =>
        report.Restart == WolfmedRestartVerdict.Hidden
            ? null
            : Loc.GetString($"wolfmed-vitals-restart-verdict-{report.Restart.ToString().ToLowerInvariant()}");

    /// <summary>"DOWNED: blood loss", "FAINTED: pain", "CARDIAC ARREST: blood", "SHUTDOWN: no power".</summary>
    public static string StateLine(WolfmedVitalsReport report)
    {
        var state = report.State switch
        {
            // M4 (OD16): a heartless species' arrest.
            WolfmedVitalsState.Arrest when report.Heartless => Loc.GetString("wolfmed-vitals-state-collapse",
                ("cause", CauseName(report.Cause, report.Source, report.Mechanical))),
            WolfmedVitalsState.Up => Loc.GetString(report.Mechanical
                ? "wolfmed-vitals-state-up-mechanical"
                : "wolfmed-vitals-state-up"),
            WolfmedVitalsState.Dead => Loc.GetString(report.Mechanical ? "wolfmed-vitals-state-dead-mechanical"
                : report.BrainDestroyed ? "wolfmed-vitals-state-dead-brain"
                : "wolfmed-vitals-state-dead"),
            _ => Loc.GetString($"wolfmed-vitals-state-{report.State.ToString().ToLowerInvariant()}",
                ("cause", CauseName(report.Cause, report.Source, report.Mechanical))),
        };

        if (report.Blockers == WolfmedCauseFlags.None || report.State is WolfmedVitalsState.Up or WolfmedVitalsState.Dead)
        {
            // Playtest 2: a faint that nothing else holds says when it ends; a blocked one names the blocker instead.
            return report.State == WolfmedVitalsState.Faint && report.FaintSeconds >= 0
                ? Loc.GetString("wolfmed-vitals-state-faint-timed",
                    ("cause", CauseName(report.Cause, report.Source, report.Mechanical)),
                    ("seconds", Number(report.FaintSeconds)))
                : state;
        }

        // An arrest already names what stopped the heart; the same cause again as a blocker says nothing new.
        var shown = report.Blockers & ~ArrestSourceFlag(report);
        if (shown == WolfmedCauseFlags.None)
            return state;

        var blockers = new List<string>();
        foreach (var cause in WolfmedCauses.Each(shown))
            blockers.Add(CauseName(cause, WolfmedCauseSource.None, report.Mechanical));

        return Loc.GetString("wolfmed-vitals-blockers", ("state", state), ("blockers", string.Join(", ", blockers)));
    }

    private static WolfmedCauseFlags ArrestSourceFlag(WolfmedVitalsReport report) =>
        report.Cause != WolfmedCause.Arrest ? WolfmedCauseFlags.None : report.Source switch
        {
            WolfmedCauseSource.ArrestBlood => WolfmedCauseFlags.Blood,
            WolfmedCauseSource.ArrestOxygen => WolfmedCauseFlags.Hypoxia,
            WolfmedCauseSource.ArrestCold => WolfmedCauseFlags.Cold, // M5
            WolfmedCauseSource.ArrestToxin => WolfmedCauseFlags.Toxin,
            WolfmedCauseSource.ArrestHeat => WolfmedCauseFlags.Heat,
            _ => WolfmedCauseFlags.None,
        };

    /// <summary>
    /// The medic's short name for a cause. Arrest and shutdown name only what is behind them ("blood",
    /// "no power"); hypoxia adds its source.
    /// </summary>
    public static string CauseName(WolfmedCause cause, WolfmedCauseSource source, bool mechanical)
    {
        if (cause is WolfmedCause.Arrest or WolfmedCause.Shutdown && source != WolfmedCauseSource.None)
            return SourceName(source);

        var key = $"wolfmed-vitals-cause-{cause.ToString().ToLowerInvariant()}";
        var name = mechanical && Loc.TryGetString(key + "-mechanical", out var machine)
            ? machine
            : Loc.GetString(key);
        return cause == WolfmedCause.Hypoxia && source != WolfmedCauseSource.None
            ? Loc.GetString("wolfmed-vitals-cause-with-source", ("cause", name), ("source", SourceName(source)))
            : name;
    }

    private static string SourceName(WolfmedCauseSource source) =>
        Loc.GetString($"wolfmed-vitals-source-{source.ToString().ToLowerInvariant()}");

    /// <summary>"Defib: shock indicated", or the refusal with what to do first. Null while hidden.</summary>
    public static string? VerdictLine(WolfmedVitalsReport report)
    {
        return report.Verdict switch
        {
            WolfmedDefibVerdict.Hidden => null,
            WolfmedDefibVerdict.NoBlood => Loc.GetString("wolfmed-vitals-verdict-noblood",
                ("percent", Whole(MathF.Max(0f, report.Blood) * 100f)),
                ("units", Units(report.VerdictUnits)),
                ("safe", Units(report.VerdictSafeUnits)),
                ("line", Whole(report.VerdictSafeLine))),
            WolfmedDefibVerdict.TooCold => Loc.GetString("wolfmed-vitals-verdict-toocold",
                ("kelvin", Whole(report.VerdictCore)),
                ("line", Units(report.VerdictRewarm))),
            WolfmedDefibVerdict.Unrevivable => Loc.GetString("wolfmed-vitals-verdict-unrevivable",
                ("reason", report.VerdictReason is { } reason && Loc.TryGetString(reason, out var text)
                    ? text
                    : report.VerdictReason ?? string.Empty)),
            _ => Loc.GetString($"wolfmed-vitals-verdict-{report.Verdict.ToString().ToLowerInvariant()}"),
        };
    }
}
