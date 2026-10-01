namespace Content.Shared._WF.Wolfmed.Life;

/// <summary>
/// The analyzer's line after a successful shock (M1a, plan §7.1). One place for the wording, so the panel
/// and the tests read the same text.
/// </summary>
public static class WolfmedPostShockText
{
    /// <summary>
    /// "Transfuse ≈ N u within S s to stop the heart stopping again; ≈ M u to L% to stop the brain injury",
    /// or only the second half once the grace has run out.
    /// </summary>
    public static string Format(float bloodFraction, float units, float safeUnits, float graceSeconds,
        float safeLine)
    {
        // Playtest 3: strings through the vitals block's rounding, never floats.
        var percent = WolfmedVitalsText.Whole(bloodFraction * 100f);
        var safe = WolfmedVitalsText.Units(safeUnits);
        var line = WolfmedVitalsText.Whole(safeLine);

        if (graceSeconds <= 0f)
        {
            return Loc.GetString("wolfmed-analyzer-post-shock-late",
                ("percent", percent), ("safe", safe), ("line", line));
        }

        return Loc.GetString("wolfmed-analyzer-post-shock",
            ("percent", percent),
            ("units", WolfmedVitalsText.Units(units)),
            ("seconds", WolfmedVitalsText.Units(graceSeconds)),
            ("safe", safe),
            ("line", line));
    }
}
