using Robust.Shared.Audio;

namespace Content.Server._WF.CavernEncounters;

/// <summary>
/// The Echo: it learns what people say near it and says it back from the dark, in their own names, while someone is
/// in earshot but not close; whoever follows the voice and comes close is attacked.
/// </summary>
[RegisterComponent]
public sealed partial class WFCavernEchoComponent : Component
{
    /// <summary>What it has heard, by whom: the speaker's name and the line.</summary>
    [ViewVariables]
    public List<(string Name, string Line)> Lines = new();

    /// <summary>The most lines it keeps; the oldest goes first.</summary>
    [DataField]
    public int MaxLines = 8;

    /// <summary>Someone closer than this hears no echo: they are attacked instead.</summary>
    [DataField]
    public float LungeRange = 3f;

    /// <summary>It only speaks while someone is within this many tiles.</summary>
    [DataField]
    public float SpeakRange = 24f;

    /// <summary>The shortest wait between lines.</summary>
    [DataField]
    public TimeSpan MinWait = TimeSpan.FromSeconds(15);

    /// <summary>The longest wait between lines.</summary>
    [DataField]
    public TimeSpan MaxWait = TimeSpan.FromSeconds(35);

    /// <summary>When it may next speak.</summary>
    [ViewVariables]
    public TimeSpan NextLine;

    /// <summary>The last line it said.</summary>
    [ViewVariables]
    public string? LastSpoken;

    /// <summary>Played when it goes for someone.</summary>
    [DataField]
    public SoundSpecifier? LungeSound;

    /// <summary>Whom it has gone for.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Prey = new();
}
