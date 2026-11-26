using System;
using System.Collections.Generic;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

namespace Content.Client._WF.Audio;

/// <summary>Caps how many network sounds start at once; a large impact can send hundreds of clips in one state.</summary>
public sealed partial class WFAudioBudgetSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public const int MaxStreams = 96;
    private readonly HashSet<EntityUid> _admitted = new();
    private readonly Queue<TimeSpan> _retiring = new();
    public int Suppressed { get; private set; }
    public int Reserved => _admitted.Count + _retiring.Count;

    public override void Initialize()
    {
        SubscribeLocalEvent<AudioComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<AudioComponent, ComponentRemove>(OnRemove);
    }

    private void OnInit(EntityUid uid, AudioComponent component, ComponentInit args)
    {
        while (_retiring.TryPeek(out var until) && until <= _timing.RealTime)
            _retiring.Dequeue();

        // Local streams are already allocated, so they are always admitted but still count.
        if (IsClientSide(uid) || Reserved < MaxStreams)
        {
            _admitted.Add(uid);
            return;
        }

        // Loaded makes the engine skip startup and keep its silent source; never touch the networked entity.
#pragma warning disable RA0002 // Deliberate content-side allocation guard; no engine changes.
        component.Loaded = true;
#pragma warning restore RA0002
        Suppressed++;
    }

    private void OnRemove(EntityUid uid, AudioComponent component, ComponentRemove args)
    {
        if (_admitted.Remove(uid))
            // OpenAL disposal is deferred to the next audio frame, so hold the freed slot briefly.
            _retiring.Enqueue(_timing.RealTime + TimeSpan.FromSeconds(0.25));
    }

    public override void Shutdown()
    {
        _admitted.Clear();
        _retiring.Clear();
        base.Shutdown();
    }
}
