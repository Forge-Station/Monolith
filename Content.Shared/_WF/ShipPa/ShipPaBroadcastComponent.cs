using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.ShipPa;

/// <summary>
/// The ship's PA timeline, replicated with the grid. Speakers describe coverage; clients render at most
/// two local sources, irrespective of how many speakers or listeners the ship has.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipPaBroadcastComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<ShipPaBroadcast> Broadcasts = new();
}

[Serializable, NetSerializable]
public enum ShipPaBroadcastKind : byte
{
    Announcement,
    Alarm,
    Track,
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class ShipPaBroadcast
{
    [DataField] public int Id;
    [DataField] public string Key = string.Empty;
    [DataField] public string Path = string.Empty;
    [DataField] public TimeSpan Start;
    [DataField] public float Length;
    [DataField] public bool Loop;
    [DataField] public AudioParams Params = AudioParams.Default;
    [DataField] public int Priority;
    [DataField] public ShipPaBroadcastKind Kind;
    [DataField] public string? Caption;
    [DataField] public Color Color = Color.White;
    [DataField] public TimeSpan RetainUntil;

    /// <summary>Includes scheduled broadcasts that clients are preparing to play.</summary>
    public bool IsActive(TimeSpan now) => now < Start || IsPlaying(now);

    public bool IsPlaying(TimeSpan now) => now >= Start && (Loop || now < Start + TimeSpan.FromSeconds(Length));

    public float Position(TimeSpan now)
    {
        var elapsed = Math.Max(0f, (float) (now - Start).TotalSeconds);
        return Loop && Length > 0f ? elapsed % Length : Math.Min(elapsed, Length);
    }
}

/// <summary>Shared, deterministic listener policy, independent of audio hardware.</summary>
public static class ShipPaPlaybackPolicy
{
    public const int MaxSources = 2;
    public const int DefaultAlarmPriority = 20;
    public const int AdvisoryPriority = 12;
    public const int AdvisoryCalloutPriority = 15;
    public const int AnnouncementPriority = 30;
    public const int ImminentPriority = 40;

    // Give replication and listener selection time to prepare the source before the first sample.
    public const float StartLeadSeconds = 0.5f;
    public const float FadeSeconds = 0.25f;

    // Require a meaningful improvement rather than oscillating between adjacent speakers.
    public static bool ShouldSwitch(float currentScore, float candidateScore) => candidateScore < currentScore * 0.8f;

    public static float Score(float distance, float range, float occlusion) =>
        distance / Math.Max(1f, range) + Math.Max(0f, occlusion) * 0.25f;
}
