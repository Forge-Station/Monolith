using System.Linq;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Alert;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Consciousness;

/// <summary>Playtest 3: what a card row is, so the client can style it and put the blockers' icons on theirs.</summary>
public enum WolfmedCardRowKind : byte
{
    Title,

    /// <summary>What wakes you, or what has to change first. Under the countdown row.</summary>
    Help,

    /// <summary>"Also holding you down: …", drawn after the blockers' icons.</summary>
    Blockers,
    Symptom,

    /// <summary>A state the patient can feel, in words: "Blood very low". Never a number.</summary>
    State,

    /// <summary>"Someone is giving you CPR.", "A medic is examining you."</summary>
    Rescue,
}

/// <summary>One text row of the card.</summary>
public readonly record struct WolfmedCardRow(WolfmedCardRowKind Kind, string Text);

/// <summary>
/// Playtest 3: the card's countdown row. The big line ("COMING ROUND IN 22 S", or "COMING ROUND: ∞" with no timer)
/// and how full its bar is: 1 at the faint's start, 0 at its end, 0 when nothing times the wake.
/// </summary>
public readonly record struct WolfmedCardCountdown(string Text, float Fraction, bool Timed);

/// <summary>Builds the unconscious screen's explanation card as lines of text from the cause prototype.</summary>
// Lines: the cause; what else holds you down; what is happening, as a symptom; what will wake you, conditional while
// something else holds you; while Dying, the brain's time as a coarse bar; the rescue line when somebody is doing CPR
// or reading you with an analyzer. Shared so the client draws it and the tests read the same text.
/// <remarks>
/// Playtest 3: a countdown row under the title while a timed faint runs (∞ otherwise; the brain bar takes the row
/// while Dying), the cause's and the blockers' alert icons, an accent colour from the cause prototype, and the states
/// the patient can feel. The only number is the countdown's seconds.
/// </remarks>
public static class WolfmedExplanationCard
{
    /// <summary>The accent stripe for a cause whose prototype names no <c>cardColour</c>.</summary>
    public static readonly Color DefaultColour = Color.FromHex("#b8704e");

    /// <summary>Every row of the card, top to bottom: title, help, blockers, symptom, felt states, rescue.</summary>
    public static List<WolfmedCardRow> Rows(IPrototypeManager prototypes, WolfmedConsciousnessComponent consciousness,
        WolfmedCardComponent? card, bool mechanical = false)
    {
        var rows = new List<WolfmedCardRow>();
        var proto = Cause(prototypes, consciousness.Cause, consciousness.Heartless); // M4: circulatory collapse

        rows.Add(new WolfmedCardRow(WolfmedCardRowKind.Title, Title(proto, consciousness)));

        var blocked = consciousness.Blockers != WolfmedCauseFlags.None;
        var help = blocked && proto?.HelpOutBlocked is { } outBlocked ? outBlocked : proto?.HelpOut;
        if (help is { } helpKey)
            rows.Add(new WolfmedCardRow(WolfmedCardRowKind.Help, Loc.GetString(helpKey)));

        if (blocked)
        {
            var names = WolfmedCauses.Each(consciousness.Blockers)
                .Select(cause => Cause(prototypes, cause) is { } blocker
                    ? Loc.GetString(blocker.BlockerName)
                    : Loc.GetString("wolfmed-condition-cause-unknown"));
            rows.Add(new WolfmedCardRow(WolfmedCardRowKind.Blockers,
                Loc.GetString("wolfmed-card-blockers", ("blockers", string.Join(", ", names)))));
        }

        if (proto?.Symptom is { } symptom)
            rows.Add(new WolfmedCardRow(WolfmedCardRowKind.Symptom, Loc.GetString(symptom)));

        // Playtest 3: what the analyzer would say about the breath and the blood, in words the patient can feel.
        if (!mechanical)
        {
            if (consciousness.Breathing != WolfmedBreathing.Normal)
            {
                rows.Add(new WolfmedCardRow(WolfmedCardRowKind.State,
                    Loc.GetString($"wolfmed-card-breathing-{consciousness.Breathing.ToString().ToLowerInvariant()}")));
            }

            if (consciousness.BloodBand is WolfmedBloodBand.Low or WolfmedBloodBand.Weak or WolfmedBloodBand.Critical)
            {
                rows.Add(new WolfmedCardRow(WolfmedCardRowKind.State,
                    Loc.GetString($"wolfmed-card-blood-{consciousness.BloodBand.ToString().ToLowerInvariant()}")));
            }
        }

        if (card is { Cpr: true })
            rows.Add(new WolfmedCardRow(WolfmedCardRowKind.Rescue, Loc.GetString("wolfmed-card-cpr")));

        if (card is { Examined: true })
            rows.Add(new WolfmedCardRow(WolfmedCardRowKind.Rescue, Loc.GetString("wolfmed-card-examined")));

        return rows;
    }

    /// <summary>Every text line of the card, top to bottom. The countdown and the bar are not rows: see below.</summary>
    public static List<string> Lines(IPrototypeManager prototypes, WolfmedConsciousnessComponent consciousness,
        WolfmedCardComponent? card, bool mechanical = false) =>
        Rows(prototypes, consciousness, card, mechanical).Select(row => row.Text).ToList();

    /// <summary>
    /// Playtest 3: the countdown row at <paramref name="now"/>. Seconds are the remainder rounded up and never
    /// negative; the bar is the remainder over the whole faint. With no timer, ∞ and an empty bar. Null while Dying:
    /// the brain bar (<see cref="Bar"/>) has the row.
    /// </summary>
    public static WolfmedCardCountdown? Countdown(IPrototypeManager prototypes,
        WolfmedConsciousnessComponent consciousness, WolfmedCardComponent? card, TimeSpan now)
    {
        if (Cause(prototypes, consciousness.Cause, consciousness.Heartless) is { Dying: true })
            return null;

        if (card is not { WakeStart: { } start, WakeEnd: { } end })
            return new WolfmedCardCountdown(Loc.GetString("wolfmed-card-countdown-none"), 0f, false);

        var left = Math.Max(0d, (end - now).TotalSeconds);
        var length = (end - start).TotalSeconds;
        var fraction = length > 0d ? (float) Math.Clamp(left / length, 0d, 1d) : 0f;
        var seconds = WolfmedVitalsText.Number((float) Math.Ceiling(left));
        return new WolfmedCardCountdown(Loc.GetString("wolfmed-card-countdown", ("seconds", seconds)), fraction, true);
    }

    /// <summary>
    /// While Dying: tenths of the rescue window the brain still has (0 to 10), and the label for it. Null otherwise.
    /// </summary>
    public static (int Tenths, string Label)? Bar(IPrototypeManager prototypes, WolfmedConsciousnessComponent consciousness,
        WolfmedCardComponent? card)
    {
        if (card is not { Reserve: >= 0 } ||
            Cause(prototypes, consciousness.Cause, consciousness.Heartless) is not { Dying: true })
            return null;

        return (Math.Clamp((int) card.Reserve, 0, 10), Loc.GetString("wolfmed-card-bar"));
    }

    /// <summary>Playtest 3: the accent stripe, from the cause prototype's <c>cardColour</c>.</summary>
    public static Color Colour(IPrototypeManager prototypes, WolfmedConsciousnessComponent consciousness) =>
        Cause(prototypes, consciousness.Cause, consciousness.Heartless)?.CardColour ?? DefaultColour;

    /// <summary>Playtest 3: the alert whose icon sits beside the title: the cause's own, as the alerts bar shows it.</summary>
    public static ProtoId<AlertPrototype>? CauseAlert(IPrototypeManager prototypes,
        WolfmedConsciousnessComponent consciousness) =>
        Alert(Cause(prototypes, consciousness.Cause, consciousness.Heartless));

    /// <summary>Playtest 3: one alert per blocker that has one, in the order the blockers line names them.</summary>
    public static List<ProtoId<AlertPrototype>> BlockerAlerts(IPrototypeManager prototypes,
        WolfmedConsciousnessComponent consciousness)
    {
        var alerts = new List<ProtoId<AlertPrototype>>();
        foreach (var cause in WolfmedCauses.Each(consciousness.Blockers))
        {
            // Playtest 3: a blocker's own Downed icon (the oxygen, blood or pain one), not the shared critical icon.
            if (BlockerAlert(Cause(prototypes, cause)) is { } alert && !alerts.Contains(alert))
                alerts.Add(alert);
        }

        return alerts;
    }

    /// <summary>The card shows while the body is out, so the Critical alert first; a Downed-only cause has only its own.</summary>
    private static ProtoId<AlertPrototype>? Alert(WolfmedConsciousnessCausePrototype? proto) =>
        proto?.AlertOut ?? proto?.AlertDowned;

    /// <summary>A blocker is what holds you down, so its Downed alert's icon says what it is.</summary>
    private static ProtoId<AlertPrototype>? BlockerAlert(WolfmedConsciousnessCausePrototype? proto) =>
        proto?.AlertDowned ?? proto?.AlertOut;

    private static WolfmedConsciousnessCausePrototype? Cause(IPrototypeManager prototypes, WolfmedCause cause,
        bool heartless = false) =>
        cause != WolfmedCause.None &&
        prototypes.TryIndex<WolfmedConsciousnessCausePrototype>(WolfmedCauses.PrototypeId(cause, heartless), out var proto)
            ? proto
            : null;

    /// <summary>"Unconscious: blood loss", "Passed out: pain", "Cardiac arrest: blood loss": the Critical title.</summary>
    private static string Title(WolfmedConsciousnessCausePrototype? proto,
        WolfmedConsciousnessComponent consciousness)
    {
        if (proto == null)
            return Loc.GetString("wolfmed-condition-title-out", ("cause", Loc.GetString("wolfmed-condition-cause-unknown")));

        var name = Loc.GetString(proto.BlockerName);
        var source = proto.Sources.TryGetValue(consciousness.CauseSource, out var sourceName)
            ? Loc.GetString(sourceName)
            : Loc.GetString("wolfmed-condition-source-unknown");

        if (proto.TitleOut is { } title)
            return Loc.GetString(title, ("cause", name), ("source", source));

        var named = proto.Sources.TryGetValue(consciousness.CauseSource, out var sub)
            ? Loc.GetString("wolfmed-condition-cause-with-source", ("cause", name), ("source", Loc.GetString(sub)))
            : name;
        return Loc.GetString("wolfmed-condition-title-out", ("cause", named));
    }
}
