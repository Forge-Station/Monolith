#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.PlanetPois;
using Content.Shared._WF.Ghost;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Ghost;

/// <summary>Planetary sites are one warp each for every ghost, and the warp goes down to the site, not its orbit signal.</summary>
[TestFixture]
public sealed class GhostPlanetPoiTest
{
    [Test]
    public async Task ASiteIsListedForGhostsAndWarpsToTheGround()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var ground = await pair.CreateTestMap();
        var orbit = await pair.CreateTestMap();

        EntityUid ghost = default, signal = default;
        await server.WaitPost(() =>
        {
            ghost = entMan.SpawnEntity("MobObserver", orbit.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, ghost);
            signal = entMan.SpawnEntity("WFPoiSignal", orbit.GridCoords);
            var comp = entMan.EnsureComponent<WFPoiSignalComponent>(signal);
            comp.Ground = ground.MapUid;
            comp.Origin = new Vector2i(3, 4);
            comp.Name = "wf-poi-carcinoma-fountain-of-life";
        });
        await pair.RunTicksSync(10);

        List<GhostOrbitTarget>? received = null;
        // Pooled clients keep their systems, so the handler comes off again below.
        Action<GhostOrbitTargetsEvent> listen = ev => received = ev.Targets;
        var orbitUi = client.System<Content.Client._WF.Ghost.GhostOrbitSystem>();
        await client.WaitPost(() =>
        {
            orbitUi.TargetsReceived += listen;
            client.EntMan.EntityNetManager!.SendSystemNetworkMessage(new GhostOrbitRequestEvent());
        });
        await pair.RunTicksSync(10);
        await client.WaitPost(() => orbitUi.TargetsReceived -= listen);

        var netSignal = entMan.GetNetEntity(signal);
        Assert.That(received, Is.Not.Null, "The server never answered the ghost.");
        var site = received!.SingleOrDefault(target => target.Entity == netSignal);
        Assert.That(site.Category, Is.EqualTo(GhostOrbitCategory.PlanetPoi), "A non-admin ghost should see the site under Planetary POIs.");
        Assert.That(site.Name, Is.EqualTo("Fountain of Life"));

        await client.WaitPost(() => client.EntMan.EntityNetManager!.SendSystemNetworkMessage(new GhostOrbitWarpEvent(netSignal)));
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(ghost);
            Assert.That(xform.MapUid, Is.EqualTo(ground.MapUid), "The warp should take the ghost down to the site.");
            Assert.That(entMan.System<SharedTransformSystem>().GetWorldPosition(xform), Is.EqualTo(new Vector2(3.5f, 4.5f)).Using<Vector2>((a, b) => Vector2.Distance(a, b) < 0.01f));
        });

        await pair.CleanReturnAsync();
    }
}
