#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WF.WolfWeather;
using Content.Shared._WF.WolfWeather;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.WolfWeather;

/// <summary>A player on a stormy map hears the thunder of a strike and of a far flash.</summary>
[TestFixture]
public sealed class ThunderSoundTest
{
    private const string OpenTile = "FloorPlanetGrass";
    private const string Storm = "WFThunderstorm";

    /// <summary>
    /// Volumes are power decibels (-8 is a gain of 0.16), so thunder at -8 sank under the rain loops at 0.8: it played
    /// and nobody heard it. Every storm's thunder has to come through the weather it comes with.
    /// </summary>
    [Test]
    public async Task EveryThunderIsLoudEnoughToBeHeard()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var proto = pair.Server.ResolveDependency<IPrototypeManager>();
            var sounds = 0;
            foreach (var weather in proto.EnumeratePrototypes<WolfWeatherPrototype>())
            {
                if (weather.Lightning is not { } lightning)
                    continue;

                if (lightning.FarSound is { } far)
                {
                    sounds++;
                    Assert.That(SharedAudioSystem.VolumeToGain(far.Params.Volume), Is.GreaterThanOrEqualTo(0.6f),
                        $"{weather.ID}: far thunder is too quiet to be heard over the rain.");
                }

                if (lightning.NearSound is { } near)
                {
                    sounds++;
                    var distance = lightning.StrikeRange.Y;
                    var reference = near.Params.ReferenceDistance;
                    var gain = reference / (reference + near.Params.RolloffFactor * MathF.Max(distance - reference, 0f));
                    Assert.That(near.Params.MaxDistance, Is.GreaterThanOrEqualTo(distance),
                        $"{weather.ID}: a strike's thunder is cut off before the farthest strike.");
                    Assert.That(gain, Is.GreaterThanOrEqualTo(0.6f),
                        $"{weather.ID}: a strike {distance} tiles away is too quiet to be heard.");
                }
            }

            Assert.That(sounds, Is.GreaterThan(0), "No storm has any thunder.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StrikePlaysItsThunderToThePlayer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var (server, client) = (pair.Server, pair.Client);
        var map = await pair.CreateTestMap();
        EntityUid mob = default;
        WolfWeatherLightning lightning = default!;

        await server.WaitPost(() =>
        {
            Lay(pair, map.Grid);
            mob = server.EntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
            server.System<WolfWeatherSystem>().SetWeather(map.MapUid, Storm, null);
            lightning = server.ProtoMan.Index<WolfWeatherPrototype>(Storm).Lightning!;
        });
        await pair.RunTicksSync(5);

        var before = Thunders(client.EntMan).Count;
        await server.WaitPost(() => server.System<WolfLightningSystem>().Strike(
            new EntityCoordinates(map.Grid, 5.5f, 0.5f), lightning, Filter.BroadcastMap(map.MapId)));
        await pair.RunTicksSync(5);

        var heard = Thunders(client.EntMan);
        Assert.That(heard.Count, Is.GreaterThan(before), "The strike played no thunder to the player.");

        await server.WaitPost(() => server.EntMan.DeleteEntity(mob));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FarFlashPlaysItsThunderToThePlayer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var (server, client) = (pair.Server, pair.Client);
        var map = await pair.CreateTestMap();
        EntityUid mob = default;

        await server.WaitPost(() =>
        {
            Lay(pair, map.Grid);
            mob = server.EntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
            server.System<WolfWeatherSystem>().SetWeather(map.MapUid, Storm, null);
        });
        await pair.RunTicksSync(5);

        var before = Thunders(client.EntMan).Count;
        await server.WaitPost(() => server.EntMan.EntityNetManager.SendSystemNetworkMessage(new WolfLightningEvent
        {
            Map = server.EntMan.GetNetEntity(map.MapUid),
            Weather = Storm,
            Strike = false,
        }));

        // The thunder trails the flash by up to four real seconds.
        for (var i = 0; i < 20 && Thunders(client.EntMan).Count <= before; i++)
        {
            await Task.Delay(250);
            await pair.RunTicksSync(5);
        }

        Assert.That(Thunders(client.EntMan).Count, Is.GreaterThan(before), "The far flash played no thunder.");

        await server.WaitPost(() => server.EntMan.DeleteEntity(mob));
        await pair.CleanReturnAsync();
    }

    private static List<Entity<AudioComponent>> Thunders(IEntityManager em)
    {
        return em.AllEntities<AudioComponent>()
            .Where(e => e.Comp.FileName?.Contains("/WolfWeather/Thunder/") == true)
            .ToList();
    }

    private static void Lay(TestPair pair, Entity<Robust.Shared.Map.Components.MapGridComponent> grid)
    {
        var maps = pair.Server.System<SharedMapSystem>();
        var tile = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()[OpenTile].TileId);
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = -12; x <= 12; x++)
        {
            for (var y = -12; y <= 12; y++)
                tiles.Add((new Vector2i(x, y), tile));
        }

        maps.SetTiles(grid.Owner, grid.Comp, tiles);
    }
}
