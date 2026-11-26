using Content.Server._WF.Planets.Bounds;
using Content.Shared._WF.Planets;
using Robust.Shared.Timing;

namespace Content.Server._WF.Planets;

/// <summary>
/// Sends every client how far the forming worlds have got, a few times a second while any are forming and once
/// empty when they are done, for the lobby's world-forming bar and the in-round one.
/// </summary>
public sealed class WFWorldFormingSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WFPlanetNetworkSystem _networks = default!;
    [Dependency] private WFPlanetPreloadSystem _preload = default!;
    [Dependency] private WFPlanetRoundHoldSystem _hold = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(0.25);

    private TimeSpan _next;
    private bool _sentAny;

    /// <summary>The latest state sent.</summary>
    public WFWorldFormingEvent Last { get; private set; } = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;
        var state = Measure();

        // Nothing forming: one empty state clears the bars, then silence.
        if (state.Worlds.Count == 0 && !state.RoundHeld)
        {
            if (!_sentAny)
                return;

            _sentAny = false;
        }
        else
        {
            _sentAny = true;
        }

        Last = state;
        RaiseNetworkEvent(state);
    }

    /// <summary>The forming worlds as they stand.</summary>
    public WFWorldFormingEvent Measure()
    {
        var state = new WFWorldFormingEvent { RoundHeld = _hold.Holding };
        var roundStart = new HashSet<EntityUid>(_networks.PrebuiltGrounds());
        var total = 0f;

        foreach (var (ground, stage, progress) in _preload.Forming())
        {
            if (TerminatingOrDeleted(ground))
                continue;

            var entry = new WFWorldFormingEntry
            {
                Ground = GetNetEntity(ground),
                Name = WorldName(ground),
                Stage = stage,
                Progress = progress,
                RoundStart = roundStart.Contains(ground),
            };

            if (TryComp<WFPlanetLayerComponent>(ground, out var layer)
                && TryGetEntity(layer.Network, out var network)
                && TryComp<WFPlanetNetworkComponent>(network, out var comp))
            {
                foreach (var map in comp.Layers)
                {
                    entry.Maps.Add(GetNetEntity(map));
                }

                if (!comp.Layers.Contains(comp.OrbitMap))
                    entry.Maps.Add(GetNetEntity(comp.OrbitMap));
            }
            else
            {
                entry.Maps.Add(entry.Ground);
            }

            state.Worlds.Add(entry);
            total += progress;
        }

        state.Overall = state.Worlds.Count == 0 ? 1f : total / state.Worlds.Count;
        return state;
    }

    /// <summary>The world's own name, as the weather report gives it; the ground's map name otherwise.</summary>
    private string WorldName(EntityUid ground)
    {
        if (TryComp<WFPlanetLayerComponent>(ground, out var layer)
            && TryGetEntity(layer.Network, out var network)
            && TryComp<WFPlanetWeatherComponent>(network, out var weather)
            && !string.IsNullOrEmpty(weather.PlanetName))
            return weather.PlanetName;

        return MetaData(ground).EntityName;
    }
}
