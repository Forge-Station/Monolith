using Content.Shared._WF.WolfWeather;
using Content.Shared.Light.Components;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Client._WF.WolfWeather;

/// <summary>
/// Draws a bolt somewhere in view for a far flash, so the player sees what flashed. A strike near a player spawns its
/// own bolt on the server.
/// </summary>
public sealed class WolfThunderboltSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WolfWeatherSystem _wolfWeather = default!;

    /// <summary>The client-only bolt a far flash draws.</summary>
    public static readonly EntProtoId FarBolt = "WFThunderboltFar";

    /// <summary>Tiles from the player a far bolt lands, min and max: inside the default view.</summary>
    private const float MinRange = 3f;
    private const float MaxRange = 7f;

    private const int Attempts = 8;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WolfLightningEvent>(OnLightning);
    }

    private void OnLightning(WolfLightningEvent ev)
    {
        if (ev.Strike || !_proto.TryIndex(ev.Weather, out var weather) || weather.Lightning?.Bolt == null)
            return;

        TrySpawnFarBolt(GetEntity(ev.Map));
    }

    /// <summary>Spawns a far flash's bolt on open ground in the local player's view; null when none is found.</summary>
    public EntityUid? TrySpawnFarBolt(EntityUid map)
    {
        if (_player.LocalEntity is not { } player)
            return null;

        var xform = Transform(player);
        if (xform.MapUid != map || xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return null;

        TryComp<RoofComponent>(gridUid, out var roof);
        var origin = _transform.GetWorldPosition(xform);
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var offset = _random.NextVector2(MinRange, MaxRange);
            var tile = _map.GetTileRef(gridUid, grid, _map.WorldToTile(gridUid, grid, origin + offset));
            if (tile.Tile.IsEmpty || !_wolfWeather.IsExposed((gridUid, grid, roof), tile))
                continue;

            return Spawn(FarBolt, _map.GridTileToLocal(gridUid, grid, tile.GridIndices));
        }

        return null;
    }
}
