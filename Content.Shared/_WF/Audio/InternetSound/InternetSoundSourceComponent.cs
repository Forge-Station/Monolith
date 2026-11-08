using Robust.Shared.GameStates;

namespace Content.Shared._WF.Audio.InternetSound;

/// <summary>
/// An item that plays internet sounds out of itself, such as a boombox. The server sets the track; each
/// nearby client plays its own copy from the item once the audio has arrived.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class InternetSoundSourceComponent : Component
{
    /// <summary>Resource path of the playing track, or null when quiet.</summary>
    [DataField, AutoNetworkedField]
    public string? Path;

    /// <summary>Internet sound id of the playing track.</summary>
    [DataField, AutoNetworkedField]
    public int TrackId;

    /// <summary>When the track starts, so late arrivals join it partway in.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan Start;

    /// <summary>Track length in seconds.</summary>
    [DataField, AutoNetworkedField]
    public float Length;

    /// <summary>How far away it can be heard.</summary>
    [DataField, AutoNetworkedField]
    public float Range = 12f;

    [DataField, AutoNetworkedField]
    public float Volume;

    /// <summary>Title of the playing track, for examine.</summary>
    [DataField, AutoNetworkedField]
    public string? Title;

    public bool IsPlaying(TimeSpan now) => Path != null && now < Start + TimeSpan.FromSeconds(Length);
}

/// <summary>Raised on the source when a track starts or stops coming out of it.</summary>
[ByRefEvent]
public readonly record struct InternetSoundSourceChangedEvent(bool Playing);
