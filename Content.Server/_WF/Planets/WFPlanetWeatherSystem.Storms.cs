using System.Numerics;
using Content.Server.Electrocution;
using Content.Shared._WF.Planets;
using Content.Shared.Mobs.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Planets;

/// <summary>Phased storms (telegraph, main, end) and lightning strikes near players on the surface.</summary>
public sealed partial class WFPlanetWeatherSystem
{
    [Dependency] private ElectrocutionSystem _electrocution = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    /// <summary>Lightning bolt entity spawned at a strike.</summary>
    public static readonly EntProtoId Thunderbolt = "WFThunderbolt";

    /// <summary>Thunder at the strike, sent to the whole map and left to fall off with distance.</summary>
    public static readonly SoundSpecifier ThunderSound = new SoundCollectionSpecifier("WFThunder",
        AudioParams.Default.WithMaxDistance(120f).WithRolloffFactor(0.4f).WithVariation(0.05f));

    // Seconds between bolts in a thunderstorm's main phase.
    private const float ThunderMinSeconds = 6f;
    private const float ThunderMaxSeconds = 22f;

    // Tiles from a player a bolt lands.
    private const float ThunderMinRange = 4f;
    private const float ThunderMaxRange = 20f;

    private const int ThunderShockDamage = 25;
    private static readonly TimeSpan ThunderShockTime = TimeSpan.FromSeconds(3);

    private readonly List<EntityUid> _thunderTargets = new();
    private readonly HashSet<Entity<MobStateComponent>> _struck = new();

    /// <summary>Moves a world's storm on one phase: clear, telegraph, main, end, clear.</summary>
    private void AdvanceStorm(WFPlanetWeatherComponent state, WFPlanetWeatherPrototype profile)
    {
        var storm = state.Storm >= 0 && state.Storm < profile.Storms.Count ? profile.Storms[state.Storm] : null;

        switch (state.Phase)
        {
            case WFStormPhase.None when profile.Storms.Count > 0:
                state.Storm = _random.Next(profile.Storms.Count);
                storm = profile.Storms[state.Storm];

                if (storm.Telegraph is { } telegraph)
                {
                    Enter(state, WFStormPhase.Telegraph, telegraph, storm.TelegraphSeconds);
                    return;
                }

                EnterMain(state, storm, profile);
                return;

            case WFStormPhase.Telegraph when storm != null:
                EnterMain(state, storm, profile);
                return;

            case WFStormPhase.Main when storm?.End is { } end:
                Enter(state, WFStormPhase.End, end, storm.EndSeconds);
                return;

            default:
                Enter(state, WFStormPhase.None, null, _random.NextFloat(profile.ClearMinSeconds, profile.ClearMaxSeconds));
                return;
        }
    }

    private void EnterMain(WFPlanetWeatherComponent state, WFPlanetStorm storm, WFPlanetWeatherPrototype profile)
    {
        Enter(state, WFStormPhase.Main, storm.Main, _random.NextFloat(profile.WeatherMinSeconds, profile.WeatherMaxSeconds));
        state.NextThunder = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(ThunderMinSeconds, ThunderMaxSeconds));
    }

    private void Enter(WFPlanetWeatherComponent state, WFStormPhase phase, ProtoId<Content.Shared.Weather.WeatherPrototype>? weather, float seconds)
    {
        state.Phase = phase;
        state.Current = weather;
        state.NextChange = _timing.CurTime + TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Strikes a bolt when one is due.</summary>
    private void UpdateThunder(WFPlanetNetworkComponent network, WFPlanetWeatherComponent state, WFPlanetWeatherPrototype profile)
    {
        if (state.Phase != WFStormPhase.Main
            || state.Storm < 0 || state.Storm >= profile.Storms.Count
            || !profile.Storms[state.Storm].Thunder
            || _timing.CurTime < state.NextThunder)
        {
            return;
        }

        state.NextThunder = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(ThunderMinSeconds, ThunderMaxSeconds));
        Strike(network.GroundMap);
    }

    /// <summary>One bolt on weather-exposed ground near a random visitor; whoever stands on that tile is shocked.</summary>
    public bool Strike(EntityUid ground)
    {
        if (!TryComp<MapGridComponent>(ground, out var grid))
            return false;

        _thunderTargets.Clear();

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is { } attached && Transform(attached).MapUid == ground)
                _thunderTargets.Add(attached);
        }

        if (_thunderTargets.Count == 0)
            return false;

        var origin = _transform.GetWorldPosition(_random.Pick(_thunderTargets));

        // A few tries: roofed tiles take no lightning, as they take no rain.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var offset = _random.NextAngle().ToVec() * _random.NextFloat(ThunderMinRange, ThunderMaxRange);
            var tile = _maps.GetTileRef(ground, grid, _maps.WorldToTile(ground, grid, origin + offset));

            if (tile.Tile.IsEmpty || !_weather.CanWeatherAffect(ground, grid, tile))
                continue;

            var coords = _maps.GridTileToLocal(ground, grid, tile.GridIndices);
            Spawn(Thunderbolt, coords);
            _audio.PlayStatic(ThunderSound, Filter.BroadcastMap(Transform(ground).MapID), coords, true);

            _struck.Clear();
            _lookup.GetEntitiesInRange(coords, 0.7f, _struck);

            foreach (var mob in _struck)
            {
                _electrocution.TryDoElectrocution(mob, null, ThunderShockDamage, ThunderShockTime, true, ignoreInsulation: true);
            }

            return true;
        }

        return false;
    }
}
