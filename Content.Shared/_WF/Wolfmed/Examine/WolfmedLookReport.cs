namespace Content.Shared._WF.Wolfmed.Examine;

/// <summary>
/// LOOK2: one thing an examiner can see on a body part. The short <see cref="Label"/> goes on the row, the
/// full sentence <see cref="Text"/> goes in its tooltip, and the glyph and colour tie the examine to the
/// health analyzer's own pictograms.
/// </summary>
public sealed class WolfmedLookObservation
{
    /// <summary>State in analyzer_icons.rsi.</summary>
    public readonly string Icon;

    /// <summary>Palette key from <see cref="WolfmedLookPalette"/>.</summary>
    public readonly string Colour;

    /// <summary>Two to four words, plain text.</summary>
    public readonly string Label;

    /// <summary>The whole observation as a sentence, plain text.</summary>
    public readonly string Text;

    public WolfmedLookObservation(string icon, string colour, string label, string text)
    {
        Icon = icon;
        Colour = colour;
        Label = label;
        Text = text;
    }
}

/// <summary>LOOK2: one body part with something to show, and everything showing on it.</summary>
public sealed class WolfmedLookPart
{
    /// <summary>The part's own name, lower case, for the row's name column.</summary>
    public string Name = string.Empty;

    /// <summary>Palette key for the row's left marker: the worst finding on the part.</summary>
    public string Accent = WolfmedLookPalette.Neutral;

    /// <summary>
    /// The part as one line of markup: "Their left arm: deep cut, stitched, bleeding freely." What the chat
    /// copy and anything else that renders the examine message as text gets instead of the row.
    /// </summary>
    public string Line = string.Empty;

    public readonly List<WolfmedLookObservation> Findings = new();
}

/// <summary>
/// LOOK2: a whole visual inspection. <see cref="Parts"/> are head to foot and only ever the parts with
/// something to show; <see cref="Notes"/> are the lines that belong to the patient rather than to a part.
/// </summary>
public sealed class WolfmedLookReport
{
    /// <summary>Markup line above the rows.</summary>
    public string Title = string.Empty;

    public readonly List<WolfmedLookPart> Parts = new();

    /// <summary>Markup lines below the rows: sepsis, the clothing notice, the distance notice.</summary>
    public readonly List<string> Notes = new();
}

/// <summary>
/// LOOK2: the finding classes the inspection produces itself, as opposed to the ones a wound prototype
/// describes. Each one names an entry in the look profile's <c>classes</c> table, which is where its glyph
/// and colour live, and each one is also the prefix of its locale keys.
/// </summary>
public static class WolfmedLookClasses
{
    public const string Bleed = "bleed";
    public const string Soak = "soak";
    public const string Treatment = "treatment";
    public const string Splint = "splint";
    public const string Tourniquet = "tourniquet";
    public const string Infection = "infection";
    public const string Scars = "scars";
    public const string Numb = "numb";
    public const string Pain = "pain";

    /// <summary>Every class the system can ask the profile for. The data test walks this list.</summary>
    public static readonly string[] All =
    [
        Bleed, Soak, Treatment, Splint, Tourniquet, Infection, Scars, Numb, Pain,
    ];
}

/// <summary>
/// LOOK2: every colour key a finding may carry. The client holds the actual colours (the examine and the
/// analyzer share one palette); this list is what the data is allowed to name, so a typo in look.yml is a
/// test failure rather than a grey chip.
/// </summary>
public static class WolfmedLookPalette
{
    /// <summary>A part with nothing worse than an old scar on it.</summary>
    public const string Neutral = "neutral";

    private static readonly HashSet<string> Keys = new()
    {
        // The nine wound categories, named exactly as WolfmedWoundCategories.IconState names them.
        "cut", "puncture", "ballistic", "blunt", "burn", "internal", "infection", "mechanical", "other",
        // Part-level conditions.
        "bleeding", "internal_bleeding", "fracture", "embedded", "necrosis", "overheating", "scar",
        "pain", "impaired", "clotting",
        Neutral,
    };

    public static IReadOnlyCollection<string> All => Keys;

    public static bool Knows(string key) => Keys.Contains(key);
}
