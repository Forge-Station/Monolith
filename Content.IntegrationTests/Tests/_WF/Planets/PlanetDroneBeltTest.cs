#nullable enable
using System.Collections.Generic;
using Content.Server.Worldgen;
using Content.Server.Worldgen.Prototypes;
using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Content.Shared._WF.Planets;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>Kyphrus's orbit wells stay clear of Monolith's drone belt, so a retune of either fails here.</summary>
[TestFixture]
public sealed class PlanetDroneBeltTest
{
    /// <summary>The star system the round-start rule builds.</summary>
    private const string SystemProto = "SystemKyphrus";

    /// <summary>The innermost distance ring of Monolith's worldgen that spawns drones.</summary>
    private const string BeltProto = "MonoWorldgenVeryFarT1";

    /// <summary>Clearance past a well's rim, about a shuttle console's worldgen load radius.</summary>
    private const float Margin = 1000f;

    /// <summary>Every world's orbit well, plus the margin, ends inside the belt's inner edge.</summary>
    [Test]
    public async Task OrbitWellsClearTheDroneBelt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var belt = proto.Index<BiomePrototype>(BeltProto);
            Assert.That(belt.DistanceRange, Is.Not.Null, $"{BeltProto} is no longer a distance ring.");

            // A chunk joins the ring by its centre, and its debris can sit up to half a chunk diagonal further in.
            var edge = belt.DistanceRange!.Value.X - WorldGen.ChunkSize * MathF.Sqrt(2f) / 2f;

            var surfaces = new Dictionary<ProtoId<PlanetTypePrototype>, WFPlanetSurfacePrototype>();

            foreach (var surface in proto.EnumeratePrototypes<WFPlanetSurfacePrototype>())
            {
                surfaces.TryAdd(surface.PlanetType, surface);
            }

            var system = proto.Index<StarSystemPrototype>(SystemProto);
            Assert.That(system.Planets, Is.Not.Empty, $"{SystemProto} has no worlds.");

            using (Assert.EnterMultipleScope())
            {
                foreach (var world in system.Planets)
                {
                    var well = surfaces.TryGetValue(world.Planet, out var surface) ? surface.OrbitRange : 0f;

                    Assert.That(world.Distance + well + Margin, Is.LessThan(edge),
                        $"{world.Planet} at {world.Distance} m with a {well} m orbit well comes within {Margin} m of the drone belt at {edge:F0} m.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }
}
