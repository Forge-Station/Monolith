#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.GroundCover;
using Content.Server.Decals;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.GroundCover;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Decals;
using Content.Shared._WF.Planets;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.GroundCover;

/// <summary>Rain that washes decals and tints puddles, weather only on a surface, and no cover on a vessel.</summary>
[TestFixture]
[TestOf(typeof(WFGroundCoverSystem))]
public sealed class GroundCoverRound12Test
{
    private static readonly Vector2i Roofed = new(2, 2);
    private static readonly Vector2i Open = new(1, 1);

    [Test]
    public async Task RainWashesCleanableDecalsOnOpenGroundOnly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        uint openClean = 0, openPaint = 0, roofClean = 0;

        await server.WaitPost(() =>
        {
            GroundCoverTest.LayOpenGround(pair, map.Grid);
            var decals = server.System<DecalSystem>();
            var coords = (Vector2i tile) => new EntityCoordinates(map.Grid, new Vector2(tile.X + 0.5f, tile.Y + 0.5f));
            Assert.That(decals.TryAddDecal("Dirt", coords(Open), out openClean, cleanable: true));
            Assert.That(decals.TryAddDecal("Dirt", coords(Open), out openPaint, cleanable: false));
            Assert.That(decals.TryAddDecal("Dirt", coords(Roofed), out roofClean, cleanable: true));
            server.System<SharedWolfWeatherSystem>().SetWeather(map.MapUid, "WFRainHeavy", null, 0);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(16));
        await server.WaitAssertion(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            var ground = entMan.GetComponent<WFGroundWeatherComponent>(map.MapUid);
            cover.StepMap((map.MapUid, ground), 120f);

            var chunk = WFSnowCover.ChunkOf(Open);
            for (var i = 0; i < 100; i++)
            {
                cover.WashDecals(map.Grid, chunk);
            }

            var grid = entMan.GetComponent<DecalGridComponent>(map.Grid);
            var left = new HashSet<uint>();
            foreach (var chunkData in grid.ChunkCollection.ChunkCollection.Values)
            {
                foreach (var id in chunkData.Decals.Keys)
                    left.Add(id);
            }

            Assert.That(left, Does.Not.Contain(openClean), "Rain left a cleanable decal on open ground.");
            Assert.That(left, Does.Contain(openPaint), "Rain washed a decal that isn't cleanable (street paint).");
            Assert.That(left, Does.Contain(roofClean), "Rain washed a decal under a roof.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BloodRainTintsPuddlesAndPlainRainStaysWater()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitPost(() => server.System<SharedWolfWeatherSystem>().SetWeather(map.MapUid, "WFRainHeavy", null, 0));
        await server.WaitRunTicks(pair.SecondsToTicks(16));
        await server.WaitAssertion(() =>
        {
            var ground = entMan.GetComponent<WFGroundWeatherComponent>(map.MapUid);
            server.System<WFGroundCoverSystem>().StepMap((map.MapUid, ground), 120f);
            Assert.That(ground.Tint.R, Is.EqualTo(WFGroundWeatherComponent.Water.R).Within(0.04f), "Plain rain isn't water coloured.");
            Assert.That(ground.Tint.B, Is.EqualTo(WFGroundWeatherComponent.Water.B).Within(0.04f));
            server.System<SharedWolfWeatherSystem>().SetWeather(map.MapUid, "WFBloodRainHeavy", null, 0);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(16));
        await server.WaitAssertion(() =>
        {
            var ground = entMan.GetComponent<WFGroundWeatherComponent>(map.MapUid);
            var cover = server.System<WFGroundCoverSystem>();
            for (var i = 0; i < 10; i++)
            {
                cover.StepMap((map.MapUid, ground), 10f);
            }

            Assert.That(ground.Tint.R, Is.GreaterThan(0.45f), "Blood rain left the puddles without red.");
            Assert.That(ground.Tint.B, Is.LessThan(0.2f), "Blood rain left the puddles blue.");

            // The red stays while the puddles dry.
            server.System<SharedWolfWeatherSystem>().SetWeather(map.MapUid, null, null, 0);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(16));
        await server.WaitAssertion(() =>
        {
            var ground = entMan.GetComponent<WFGroundWeatherComponent>(map.MapUid);
            server.System<WFGroundCoverSystem>().StepMap((map.MapUid, ground), 30f);
            Assert.That(ground.Tint.B, Is.LessThan(0.2f), "The tint faded back to water as soon as the rain stopped.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WeatherFallsOnASurfaceOnly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var weather = server.System<SharedWolfWeatherSystem>();
            Assert.That(weather.IsSurface(map.MapUid), Is.True, "A map outside any network isn't a surface.");

            // A map with another linked above it is a level below a surface.
            var z = entMan.EnsureComponent<CEZMapComponent>(map.MapUid);
            z.MapAbove = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            Assert.That(weather.IsSurface(map.MapUid), Is.False, "A level below another is a surface.");

            weather.SetWeather(map.MapUid, "WFRainHeavy", null, 0);
            Assert.That(entMan.HasComponent<WolfWeatherMapComponent>(map.MapUid), Is.False, "Rain was set on a level below a surface.");

            z.MapAbove = null;
            Assert.That(weather.IsSurface(map.MapUid), Is.True, "The top level of a network isn't a surface.");
            weather.SetWeather(map.MapUid, "WFRainHeavy", null, 0);
            Assert.That(entMan.HasComponent<WolfWeatherMapComponent>(map.MapUid), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AVesselNeverHoldsSnow()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            GroundCoverTest.LayOpenGround(pair, map.Grid);
            var cover = server.System<WFGroundCoverSystem>();
            Assert.That(cover.CanHoldCover(map.Grid), Is.True, "Plain ground can't hold snow.");

            // On a planet layer a grid that isn't the ground, as a landed vessel isn't, never holds snow.
            entMan.EnsureComponent<WFPlanetLayerComponent>(map.MapUid);
            Assert.That(cover.CanHoldCover(map.Grid), Is.False, "A vessel can hold snow.");
            cover.SetDepth(map.Grid, Open, 120);
            Assert.That(cover.GetDepth(map.Grid, Open), Is.Zero, "Snow was laid on a vessel.");
        });

        await pair.CleanReturnAsync();
    }
}
