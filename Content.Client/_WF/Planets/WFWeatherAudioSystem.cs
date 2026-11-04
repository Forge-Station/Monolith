using Content.Client.Weather;
using Content.Shared.CCVar;
using Content.Shared.Weather;
using Robust.Client.Player;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._WF.Planets;

/// <summary>
/// Keeps weather loops in hand: stopped while the local player has no body, as in the lobby, where the weather
/// system leaves them playing, and scaled by the ambience slider.
/// </summary>
public sealed class WFWeatherAudioSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    /// <summary>
    /// Each stream's own volume and the scaled one it was left at: a volume still at the scaled one is the weather
    /// system's last one, scaled already.
    /// </summary>
    private readonly Dictionary<EntityUid, (float Own, float Scaled)> _applied = new();
    private readonly HashSet<EntityUid> _seen = new();
    private readonly List<EntityUid> _stale = new();
    private float _sliderVolume;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;
        UpdatesAfter.Add(typeof(WeatherSystem));
        Subs.CVar(_cfg, CCVars.AmbienceVolume, OnAmbienceVolumeChanged, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        var bodiless = _player.LocalEntity == null;
        _seen.Clear();

        var query = AllEntityQuery<WeatherComponent>();
        while (query.MoveNext(out var comp))
        {
            foreach (var weather in comp.Weather.Values)
            {
                if (weather.Stream is not { } stream || !TryComp<AudioComponent>(stream, out var audio))
                    continue;

                if (bodiless)
                {
                    // Silenced and deleted rather than stopped: the stop is refused while states apply.
                    _audio.SetGain(stream, 0f, audio);
                    QueueDel(stream);
                    weather.Stream = null;
                    continue;
                }

                _seen.Add(stream);

                // The weather system sets the volume afresh each tick it runs the weather; scale only that.
                var volume = audio.Params.Volume;
                var own = _applied.TryGetValue(stream, out var applied) && volume.Equals(applied.Scaled) ? applied.Own : volume;
                var scaled = own + _sliderVolume;
                if (!scaled.Equals(volume))
                    _audio.SetVolume(stream, scaled, audio);
                _applied[stream] = (own, scaled);
            }
        }

        _stale.Clear();
        foreach (var stream in _applied.Keys)
        {
            if (!_seen.Contains(stream))
                _stale.Add(stream);
        }

        foreach (var stream in _stale)
        {
            _applied.Remove(stream);
        }
    }

    /// <summary>The slider's offset from its default, so weather at the default setting is as loud as before.</summary>
    private void OnAmbienceVolumeChanged(float gain)
    {
        var volume = SharedAudioSystem.GainToVolume(gain / CCVars.AmbienceVolume.DefaultValue);
        _sliderVolume = float.IsFinite(volume) ? volume : float.NegativeInfinity;
    }
}
