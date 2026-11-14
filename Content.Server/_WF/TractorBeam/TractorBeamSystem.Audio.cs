using Content.Shared._WF.TractorBeam;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;

namespace Content.Server._WF.TractorBeam;

public sealed partial class TractorBeamSystem
{
    [Dependency] private SharedAudioSystem _beamAudio = default!;
    [Dependency] private IRobustRandom _beamRandom = default!;

    private readonly Dictionary<EntityUid, BeamAudioState> _beamAudioStates = new();
    private readonly Dictionary<EntityUid, HullAudioState> _hullAudioStates = new();
    // Shared by dishes: several tractors straining the same hull cannot play a chorus of creaks each tick.
    private readonly Dictionary<EntityUid, TimeSpan> _nextHullCreak = new();
    private readonly List<EntityUid> _expiredCreaks = new();
    private TimeSpan _nextAudioCleanup;

    private sealed class BeamAudioState(EntityUid source, EntityUid target)
    {
        public readonly EntityUid Source = source;
        public readonly EntityUid Target = target;
        public EntityUid? SourceCreak;
        public EntityUid? TargetCreak;
        public TimeSpan? CreakAt;
    }

    private sealed class HullAudioState
    {
        public int Users;
        public EntityUid? Engage;
        public EntityUid? Loop;
    }

    private void UpdateBeamAudio(EntityUid uid, TractorBeamEmitterComponent beam)
    {
        if (!beam.Active || beam.SourceGrid is not { } source || beam.Target is not { } target)
        {
            StopBeamAudio(uid, beam);
            return;
        }

        if (_beamAudioStates.TryGetValue(uid, out var current) && (current.Source != source || current.Target != target))
            StopBeamAudio(uid, beam);

        if (!_beamAudioStates.TryGetValue(uid, out var state))
        {
            state = new BeamAudioState(source, target);
            _beamAudioStates.Add(uid, state);
            AcquireHullAudio(source, beam);
            AcquireHullAudio(target, beam);
        }

        CleanupCreakCooldowns();
        if (beam.CreakSounds.Length == 0 || !float.IsFinite(beam.RequiredForce) ||
            beam.RequiredForce <= MathF.Max(0.001f, beam.MaxForce * MathF.Max(0, beam.CreakForceFraction)))
        {
            state.CreakAt = null;
            return;
        }

        var initialMinimum = MathF.Max(0, beam.CreakInitialMinimumDelay);
        state.CreakAt ??= _timing.CurTime + TimeSpan.FromSeconds(_beamRandom.NextFloat(initialMinimum,
            MathF.Max(initialMinimum, beam.CreakInitialMaximumDelay)));
        if (_timing.CurTime < state.CreakAt.Value ||
            (_nextHullCreak.TryGetValue(source, out var sourceNext) && _timing.CurTime < sourceNext) ||
            (_nextHullCreak.TryGetValue(target, out var targetNext) && _timing.CurTime < targetNext))
            return;

        var creak = beam.CreakSounds[_beamRandom.Next(beam.CreakSounds.Length)];
        var resolved = _beamAudio.ResolveSound(creak);
        state.SourceCreak = PlayHullAudio(resolved, source, creak.Params);
        state.TargetCreak = PlayHullAudio(resolved, target, creak.Params);
        var minimum = MathF.Max(0.5f, beam.CreakMinimumInterval);
        var maximum = MathF.Max(minimum, beam.CreakMaximumInterval);
        // Wait until the sample finishes, then leave another randomized quiet interval.
        var interval = _beamRandom.NextFloat(minimum, maximum) + _beamAudio.GetAudioLength(resolved).TotalSeconds;
        _nextHullCreak[source] = _nextHullCreak[target] = _timing.CurTime + TimeSpan.FromSeconds(interval);
        state.CreakAt = _nextHullCreak[source];
    }

    private EntityUid? PlayHullAudio(ResolvedSoundSpecifier? sound, EntityUid grid, AudioParams? parameters)
    {
        if (sound == null || TerminatingOrDeleted(grid))
            return null;
        // Match FTL's grid-audio behavior, but use the enclosing hull radius so even the corners
        // of very large or off-center grids remain audible. SetGridAudio uses only one half-extent.
        var radius = MathF.Max(1, GridRadius(grid));
        var audio = _beamAudio.PlayPvs(sound, grid, (parameters ?? AudioParams.Default)
            .WithReferenceDistance(radius).WithMaxDistance(radius + SharedAudioSystem.DefaultSoundRange));
        if (audio is { } stream)
        {
            stream.Component.Flags |= AudioFlags.GridAudio | AudioFlags.NoOcclusion;
            _beamPvs.AddGlobalOverride(stream.Entity);
            Dirty(stream.Entity, stream.Component);
        }
        return audio?.Entity;
    }

    private void StopBeamAudio(EntityUid uid, TractorBeamEmitterComponent beam)
    {
        if (!_beamAudioStates.Remove(uid, out var state))
            return;

        _beamAudio.Stop(state.SourceCreak);
        _beamAudio.Stop(state.TargetCreak);
        ReleaseHullAudio(state.Source, beam);
        ReleaseHullAudio(state.Target, beam);
    }

    private void AcquireHullAudio(EntityUid grid, TractorBeamEmitterComponent beam)
    {
        if (!_hullAudioStates.TryGetValue(grid, out var state))
        {
            state = new HullAudioState();
            _hullAudioStates.Add(grid, state);
            var engage = beam.EngageSound == null ? null : _beamAudio.ResolveSound(beam.EngageSound);
            state.Engage = PlayHullAudio(engage, grid, beam.EngageSound?.Params);
            // The sustained beam begins immediately alongside engagement, once per hull.
            var loop = beam.LoopSound == null ? null : _beamAudio.ResolveSound(beam.LoopSound);
            state.Loop = PlayHullAudio(loop, grid, beam.LoopSound?.Params.WithLoop(true));
        }
        state.Users++;
    }

    private void ReleaseHullAudio(EntityUid grid, TractorBeamEmitterComponent beam)
    {
        if (!_hullAudioStates.TryGetValue(grid, out var state) || --state.Users > 0)
            return;
        _hullAudioStates.Remove(grid);
        _beamAudio.Stop(state.Engage);
        _beamAudio.Stop(state.Loop);
        var disengage = beam.DisengageSound == null ? null : _beamAudio.ResolveSound(beam.DisengageSound);
        PlayHullAudio(disengage, grid, beam.DisengageSound?.Params);
    }

    private void CleanupCreakCooldowns()
    {
        if (_timing.CurTime < _nextAudioCleanup)
            return;
        _nextAudioCleanup = _timing.CurTime + TimeSpan.FromSeconds(15);
        _expiredCreaks.Clear();
        foreach (var (grid, next) in _nextHullCreak)
        {
            if (TerminatingOrDeleted(grid) || next <= _timing.CurTime)
                _expiredCreaks.Add(grid);
        }
        foreach (var grid in _expiredCreaks)
            _nextHullCreak.Remove(grid);
    }
}
