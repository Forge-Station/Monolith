#nullable enable
using System.Linq;
using Content.Shared._WF.Xenonids.Construction.AcidPillar;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared._WF.Xenonids.Spray;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>A hive's acid pillar sprays and hurts hostile mobs in range, humanoid or not.</summary>
[TestFixture]
public sealed class XenoAcidPillarTest
{
    [TestCase("MobHuman")]
    [TestCase("MobFrog")]
    public async Task PillarHurtsHostileMob(string targetProto)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid pillar = default, target = default;
        await server.WaitPost(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            for (var x = -1; x <= 5; x++)
            for (var y = -1; y <= 1; y++)
                maps.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);

            var hives = entities.System<SharedXenoHiveSystem>();
            var hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            var weeds = entities.SpawnEntity("XenoHiveWeeds", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            hives.SetHive(weeds, hive);
            pillar = entities.SpawnEntity("HiveAcidPillarXeno", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            hives.SetHive(pillar, hive);
            target = entities.SpawnEntity(targetProto, new EntityCoordinates(map.Grid, 3.5f, 0.5f));
        });

        var sprayed = false;
        var damage = FixedPoint2.Zero;
        for (var i = 0; i < 30 && damage <= FixedPoint2.Zero; i++)
        {
            await pair.RunSeconds(0.1f);
            await server.WaitPost(() =>
            {
                sprayed |= entities.EntityQuery<XenoAcidSplatterComponent>().Any();
                damage = entities.GetComponent<DamageableComponent>(target).TotalDamage;
            });
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(sprayed, "The pillar should spray acid at the target.");
            Assert.That(damage, Is.GreaterThan(FixedPoint2.Zero), $"The acid should hurt the {targetProto}.");

            var sound = entities.GetComponent<AcidPillarComponent>(pillar).Sound;
            Assert.That(sound, Is.TypeOf<SoundPathSpecifier>(), "The pillar should have a firing sound.");
            var path = ((SoundPathSpecifier) sound!).Path;
            Assert.That(server.ResolveDependency<IResourceManager>().ContentFileExists(path), $"{path} should exist.");
        });

        await pair.CleanReturnAsync();
    }
}
