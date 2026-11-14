using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Gifts;
using Content.Server._WF.Remade.Monolith;
using Content.Server._WF.Remade.Structures;
using Content.Shared._WF.Remade.Dread;
using Content.Shared._WF.Remade.Signals;
using Robust.Shared.Audio.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.Remade.Sector;

/// <summary>
/// Wipes the Remade from the round: every stone, shard, Remade, growth, corruption tile, Signal and stone sound, and tells every
/// client to drop the bed and the score. Clients are also told when the last stone goes by any means.
/// </summary>
public sealed class WFRemadeClearSystem : EntitySystem
{
    private const string RemadeAudio = "/Audio/_WF/Remade/";

    /// <summary>Tells every client to drop the bed and the score once a terminating stone was the last one.</summary>
    public void LastStoneGone(EntityUid stone)
    {
        var query = EntityQueryEnumerator<WFMonolithComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (uid != stone && !TerminatingOrDeleted(uid))
                return;
        }

        RaiseNetworkEvent(new WFRemadeClearEvent(), Filter.Broadcast());
    }

    /// <summary>Deletes everything of the Remade in the round, at once, so call it between ticks. Returns how many entities went.</summary>
    public int Clear()
    {
        var doomed = new HashSet<EntityUid>();
        Collect<WFMonolithComponent>(doomed);
        Collect<WFMonolithShardComponent>(doomed);
        Collect<WFRemadeComponent>(doomed);
        Collect<WFGrowthComponent>(doomed);
        Collect<WFCorruptionComponent>(doomed);
        Collect<WFSignalComponent>(doomed);

        var sounds = EntityQueryEnumerator<AudioComponent>();
        while (sounds.MoveNext(out var uid, out var audio))
        {
            // Read into a local first: the access analyzer counts a call on the field as more than a read.
            var file = audio.FileName;
            if (file.StartsWith(RemadeAudio, StringComparison.Ordinal))
                doomed.Add(uid);
        }

        // At once, not queued: the spreader runs once more before a queue is flushed and would grow tiles nobody collected.
        foreach (var uid in doomed)
        {
            if (!TerminatingOrDeleted(uid))
                Del(uid);
        }

        var stressed = EntityQueryEnumerator<WFRemadeStressComponent>();
        while (stressed.MoveNext(out var uid, out _))
        {
            RemCompDeferred<WFRemadeStressComponent>(uid);
        }

        RaiseNetworkEvent(new WFRemadeClearEvent(), Filter.Broadcast());
        return doomed.Count;
    }

    private void Collect<T>(HashSet<EntityUid> into) where T : IComponent
    {
        var query = EntityQueryEnumerator<T>();
        while (query.MoveNext(out var uid, out _))
        {
            into.Add(uid);
        }
    }
}
