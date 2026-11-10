using System.Linq;
using Content.Shared._WF.Audio.InternetSound;
using Content.Shared._WF.ShipPa;
using Robust.Client.Replays.Playback;
using Robust.Client.ResourceManagement;
using Robust.Shared.Replays;
using Robust.Shared.Serialization.Markdown.Mapping;

namespace Content.Client._WF.Audio.InternetSound;

public sealed partial class InternetSoundSystem
{
    [Dependency] private IReplayPlaybackManager _replay = default!;

    // References to bytes already owned by ReplayData; no second copy of the recording's audio.
    private readonly Dictionary<string, InternetSoundReplayAsset> _replayAssets = new();
    private readonly HashSet<int> _replayMounted = new();
    private readonly HashSet<int> _replayNeeded = new();
    // Ticks [0, _replayScanned) have been searched for assets.
    private int _replayScanned;

    private void InitializeReplay()
    {
        _replay.ReplayPlaybackStarted += OnReplayStarted;
        _replay.BeforeSetTick += ResetReplayPosition;
        _replay.AfterSetTick += UpdateReplayResources;
        _replay.ReplayPlaybackStopped += OnReplayStopped;
        _replay.HandleReplayMessage += OnReplayMessage;
    }

    private void ShutdownReplay()
    {
        _replay.ReplayPlaybackStarted -= OnReplayStarted;
        _replay.BeforeSetTick -= ResetReplayPosition;
        _replay.AfterSetTick -= UpdateReplayResources;
        _replay.ReplayPlaybackStopped -= OnReplayStopped;
        _replay.HandleReplayMessage -= OnReplayMessage;
        OnReplayStopped();
    }

    private void OnReplayStarted(MappingDataNode metadata, List<object> initial)
    {
        OnReplayStopped();
        foreach (var asset in initial.OfType<InternetSoundReplayAsset>())
            _replayAssets[InternetSoundResources.PathFor(asset.Id)] = asset;
        UpdateReplayResources();
    }

    private bool OnReplayMessage(object message, bool skipEffects)
    {
        if (message is InternetSoundReplayAsset asset)
            _replayAssets[InternetSoundResources.PathFor(asset.Id)] = asset;
        return false;
    }

    /// <summary>Loads assets from ticks skipped by a forward seek; an asset is recorded before its broadcast.</summary>
    private bool ScanReplayAssets(ReplayData replay)
    {
        var last = Math.Min(replay.CurrentIndex, replay.Count - 1);
        if (last < _replayScanned)
            return false;
        for (var i = _replayScanned; i <= last; i++)
        {
            foreach (var asset in replay.GetMessages(i).Messages.OfType<InternetSoundReplayAsset>())
                _replayAssets[InternetSoundResources.PathFor(asset.Id)] = asset;
        }
        _replayScanned = last + 1;
        return true;
    }

    private void ResetReplayPosition()
    {
        // Destroy sources before freeing their native buffers; seeking backwards may reuse these ids.
        StopGlobal(0);
        _pa.ResetReplayPosition();
        foreach (var id in _replayMounted.ToArray())
        {
            if (IsPlaying(id))
                continue;
            Free(id);
            _replayMounted.Remove(id);
        }
        _releasing.Clear();
        _stopped.Clear();
    }

    private void OnReplayStopped()
    {
        ResetReplayPosition();
        _replayAssets.Clear();
        _replayNeeded.Clear();
        _replayScanned = 0;
    }

    private void UpdateReplayResources()
    {
        if (_replay.Replay is not { } replay)
            return;
        _replayNeeded.Clear();
        var query = EntityQueryEnumerator<ShipPaBroadcastComponent>();
        while (query.MoveNext(out var state))
        {
            foreach (var broadcast in state.Broadcasts)
            {
                if (!broadcast.IsActive(_timing.CurTime))
                    continue;
                if (!_replayAssets.TryGetValue(broadcast.Path, out var asset) &&
                    (!ScanReplayAssets(replay) || !_replayAssets.TryGetValue(broadcast.Path, out asset)))
                    continue;
                _replayNeeded.Add(asset.Id);
                if (!_replayMounted.Add(asset.Id))
                    continue;
                _resources.Store(asset.Id, asset.Audio);
                // Replace a silent cache entry left by release or a previous replay position.
                _resourceCache.ReloadResource<AudioResource>(broadcast.Path);
            }
        }
        foreach (var id in _replayMounted.ToArray())
        {
            if (_replayNeeded.Contains(id) || IsPlaying(id))
                continue;
            Free(id);
            _replayMounted.Remove(id);
        }
    }
}
