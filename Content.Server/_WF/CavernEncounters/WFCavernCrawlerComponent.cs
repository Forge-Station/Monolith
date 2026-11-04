using Robust.Shared.Audio;

namespace Content.Server._WF.CavernEncounters;

/// <summary>
/// A blind crawler that hunts by sound: it ignores whoever walks and goes for whoever runs within earshot, along with
/// the rest of its nest. A quarry that slows to a walk is lost after a few seconds.
/// </summary>
[RegisterComponent, Access(typeof(WFCavernCrawlerSystem))]
public sealed partial class WFCavernCrawlerComponent : Component
{
    /// <summary>Tiles within which it hears a runner.</summary>
    [DataField]
    public float HearingRange = 10f;

    /// <summary>Tiles within which the rest of its nest hear it go and join in.</summary>
    [DataField]
    public float NestRange = 8f;

    /// <summary>How long after it last heard its quarry run it gives up on them.</summary>
    [DataField]
    public TimeSpan ForgetAfter = TimeSpan.FromSeconds(4);

    /// <summary>Played when it starts after someone.</summary>
    [DataField]
    public SoundSpecifier? AlertSound;

    /// <summary>Whom it is after, and when it last heard each of them.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Quarry = new();
}
