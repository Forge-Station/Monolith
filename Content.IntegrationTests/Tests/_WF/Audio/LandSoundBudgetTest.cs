#nullable enable
using System.Numerics;
using Content.Shared.Sound;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Audio;

/// <summary>The landing-sound budget counts sounds, not landings.</summary>
[TestFixture]
[TestOf(typeof(SharedEmitSoundSystem))]
public sealed class LandSoundBudgetTest
{
    /// <summary>Any number of silent landings leaves the budget whole, so the next landing that makes a sound is heard.</summary>
    [Test]
    public async Task SilentLandingsSpendNothing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var before = 0;
        var after = 0;

        await server.WaitPost(() =>
        {
            var item = entMan.SpawnEntity("Crowbar", map.GridCoords);

            for (var i = 0; i < 40; i++)
            {
                var silent = new LandEvent(null, false);
                entMan.EventBus.RaiseLocalEvent(item, ref silent);
            }

            before = entMan.Count<AudioComponent>();
            var loud = new LandEvent(null, true);
            entMan.EventBus.RaiseLocalEvent(item, ref loud);
            after = entMan.Count<AudioComponent>();
        });

        Assert.That(after, Is.EqualTo(before + 1), "A landing made no sound after silent landings spent the budget.");

        await pair.CleanReturnAsync();
    }

    /// <summary>A grid that has spent its landing sounds goes quiet; a landing on another grid is still heard.</summary>
    [Test]
    public async Task OneGridsBurstLeavesAnotherItsSounds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var loud = 0;
        var muted = 0;
        var elsewhere = 0;

        await server.WaitPost(() =>
        {
            var other = server.ResolveDependency<IMapManager>().CreateGridEntity(map.MapId);
            server.System<SharedTransformSystem>().SetLocalPosition(other.Owner, new Vector2(40f, 0f));
            server.System<SharedMapSystem>().SetTile(other.Owner, other.Comp, Vector2i.Zero, map.Tile.Tile);

            var here = entMan.SpawnEntity("Crowbar", map.GridCoords);
            var there = entMan.SpawnEntity("Crowbar", new EntityCoordinates(other.Owner, new Vector2(0.5f, 0.5f)));

            int Land(EntityUid item)
            {
                var before = entMan.Count<AudioComponent>();
                var landing = new LandEvent(null, true);
                entMan.EventBus.RaiseLocalEvent(item, ref landing);
                return entMan.Count<AudioComponent>() - before;
            }

            for (var i = 0; i < 40; i++)
            {
                loud += Land(here);
            }

            muted = Land(here);
            elsewhere = Land(there);
        });

        Assert.Multiple(() =>
        {
            Assert.That(loud, Is.EqualTo(8), "A burst of landings on one grid was not held to its budget.");
            Assert.That(muted, Is.Zero, "Precondition: the first grid still had budget left.");
            Assert.That(elsewhere, Is.EqualTo(1), "A burst on one grid muted a landing on another.");
        });

        await pair.CleanReturnAsync();
    }
}
