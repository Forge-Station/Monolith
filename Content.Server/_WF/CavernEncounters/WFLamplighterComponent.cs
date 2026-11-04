using Robust.Shared.Audio;

namespace Content.Server._WF.CavernEncounters;

/// <summary>
/// The Lamplighter: a stalker that can't move or strike while a carried light shines on it, and that puts out the
/// lights of whoever it follows, one at a time.
/// </summary>
[RegisterComponent]
public sealed partial class WFLamplighterComponent : Component
{
    /// <summary>Whether a light is on it now.</summary>
    [ViewVariables]
    public bool Lit;

    /// <summary>Tiles within which it puts a light out.</summary>
    [DataField]
    public float DouseRange = 7f;

    /// <summary>Time between lights it puts out.</summary>
    [DataField]
    public TimeSpan DouseInterval = TimeSpan.FromSeconds(9);

    /// <summary>When it may put the next light out.</summary>
    [ViewVariables]
    public TimeSpan NextDouse;

    /// <summary>Played where a light dies.</summary>
    [DataField]
    public SoundSpecifier? DouseSound;
}
