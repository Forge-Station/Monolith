using Robust.Shared.Audio;

namespace Content.Server._WF.CavernEncounters;

/// <summary>
/// A sound an encounter makes now and then while a player is near enough to hear it, so it can be found by ear: a
/// camp's fire, raiders' voices, crawlers' clicking, the Echo's whispers. A dead mob stays quiet.
/// </summary>
[RegisterComponent]
public sealed partial class WFCavernTelegraphComponent : Component
{
    /// <summary>What it plays.</summary>
    [DataField(required: true)]
    public SoundSpecifier Sound = default!;

    /// <summary>Tiles the sound carries, and how near a player must be for it to play.</summary>
    [DataField]
    public float Range = 20f;

    /// <summary>The shortest time between sounds.</summary>
    [DataField]
    public TimeSpan MinInterval = TimeSpan.FromSeconds(8);

    /// <summary>The longest time between sounds.</summary>
    [DataField]
    public TimeSpan MaxInterval = TimeSpan.FromSeconds(16);

    /// <summary>Volume in decibels the sound plays at.</summary>
    [DataField]
    public float Volume;

    /// <summary>When it may next play.</summary>
    [ViewVariables]
    public TimeSpan NextPlay;

    /// <summary>When it last played, for tests and admins.</summary>
    [ViewVariables]
    public TimeSpan? LastPlayed;
}
