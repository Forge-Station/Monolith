using Robust.Shared.ContentPack;
using Robust.Shared.Replays;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Markdown.Mapping;

namespace Content.Shared._WF.Audio.InternetSound;

/// <summary>Preserves downloaded bytes in both server and client recordings, including mid-song starts.</summary>
public sealed partial class InternetSoundReplaySystem : EntitySystem
{
    [Dependency] private IResourceManager _resourceManager = default!;
    [Dependency] private IReplayRecordingManager _recording = default!;
    private InternetSoundResources _resources = default!;

    public override void Initialize()
    {
        base.Initialize();
        _resources = InternetSoundResources.For(_resourceManager);
        _resources.Stored += OnStored;
        _recording.RecordingStarted += OnRecordingStarted;
    }

    public override void Shutdown()
    {
        _resources.Stored -= OnStored;
        _recording.RecordingStarted -= OnRecordingStarted;
        base.Shutdown();
    }

    private void OnStored(int id, byte[] audio)
    {
        _recording.RecordReplayMessage(new InternetSoundReplayAsset(id, audio));
    }

    private void OnRecordingStarted(MappingDataNode metadata, List<object> messages)
    {
        foreach (var asset in _resources.ReplayAssets())
            messages.Add(asset);
    }
}

/// <summary>Replay-only payload; never fetch a remote URL while playing a recording.</summary>
[Serializable, NetSerializable]
public sealed record InternetSoundReplayAsset(int Id, byte[] Audio);
