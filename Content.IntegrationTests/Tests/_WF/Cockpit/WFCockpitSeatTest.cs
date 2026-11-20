using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Cockpit;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Cockpit;

/// <summary>Exercises seat inheritance through actual buckle operations without admitting beds or arbitrary straps.</summary>
[TestFixture]
public sealed class WFCockpitSeatTest
{
    [Test]
    public async Task OrdinarySeatFamiliesRetainBuckleAndHelmRequirements()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var actor = em.SpawnEntity("MobHuman", map.GridCoords);
            var helm = em.SpawnEntity("ComputerShuttle", map.GridCoords);
            var otherHelm = em.SpawnEntity("ComputerShuttle", map.GridCoords);
            var buckle = em.System<SharedBuckleSystem>();
            var cockpit = em.System<SharedWFCockpitSystem>();
            em.EnsureComponent<Content.Shared.Shuttles.Components.PilotComponent>(actor);
            em.System<ShuttleConsoleSystem>().AddPilot(helm, actor, em.GetComponent<ShuttleConsoleComponent>(helm));
            Assert.That(cockpit.CanEnter(actor, helm), Is.False, "Operating the helm without being buckled is insufficient.");
            foreach (var prototype in new[] { "Chair", "Stool", "SteelBench", "ChairOfficeLight", "ChairFolding", "ChairWoodFancyBlack", "ChairPilotSeat" })
            {
                var seat = em.SpawnEntity(prototype, map.GridCoords);
                Assert.That(em.HasComponent<WFCockpitSeatComponent>(seat), Is.True, prototype);
                Assert.That(buckle.TryBuckle(actor, actor, seat), Is.True, prototype);
                Assert.That(cockpit.CanEnter(actor, helm), Is.True, $"{prototype} must support the optional cockpit.");
                Assert.That(cockpit.CanEnter(actor, otherHelm), Is.False, "A chair cannot grant an unrelated piloting session.");
                var strap = em.GetComponent<StrapComponent>(seat);
                SetStrapEnabled(strap, false);
                Assert.That(em.GetComponent<BuckleComponent>(actor).BuckledTo, Is.EqualTo(seat), "Only the strap flag changes in this check.");
                Assert.That(cockpit.CanEnter(actor, helm), Is.False, $"{prototype} must not admit a buckled pilot while its strap is disabled.");
                SetStrapEnabled(strap, true);
                Assert.That(cockpit.CanEnter(actor, helm), Is.True, $"{prototype} must grant eligibility again once its strap is enabled.");
                buckle.StrapSetEnabled(seat, false);
                Assert.That(cockpit.CanEnter(actor, helm), Is.False, "Disabling the seat must revoke cockpit eligibility.");
                Assert.That(em.GetComponent<BuckleComponent>(actor).BuckledTo, Is.Null);
                em.DeleteEntity(seat);
            }
            foreach (var prototype in new[] { "Bed", "DogBed", "RollerBed" })
            {
                var bed = em.SpawnEntity(prototype, map.GridCoords);
                Assert.That(buckle.TryBuckle(actor, actor, bed), Is.True, prototype);
                Assert.That(cockpit.CanEnter(actor, helm), Is.False, $"{prototype} has straps but is not a seat.");
                buckle.Unbuckle((actor, em.GetComponent<BuckleComponent>(actor)), actor);
                em.DeleteEntity(bed);
            }
            var folded = em.SpawnEntity("ChairFoldingSpawnFolded", map.GridCoords);
            Assert.That(buckle.TryBuckle(actor, actor, folded), Is.False, "A folded seat has no usable buckle.");
            Assert.That(cockpit.CanEnter(actor, helm), Is.False);
            em.DeleteEntity(folded);
            var chair = em.SpawnEntity("Chair", map.GridCoords);
            Assert.That(buckle.TryBuckle(actor, actor, chair), Is.True);
            Assert.That(cockpit.CanEnter(actor, helm), Is.True, "The final chair must grant eligibility before anything is taken away.");
            buckle.Unbuckle((actor, em.GetComponent<BuckleComponent>(actor)), actor);
            Assert.That(cockpit.CanEnter(actor, helm), Is.False, "Unbuckling must always end cockpit eligibility.");
            Assert.That(buckle.TryBuckle(actor, actor, chair), Is.True);
            Assert.That(cockpit.CanEnter(actor, helm), Is.True, "Buckling again must restore eligibility before the helm moves.");
            var transform = em.System<SharedTransformSystem>();
            transform.SetCoordinates(helm, new EntityCoordinates(map.MapUid, new Vector2(100, 100)));
            Assert.That(cockpit.CanEnter(actor, helm), Is.False, "The occupied seat must remain on the helm's grid.");
            foreach (var entity in new[] { actor, helm, otherHelm, chair })
                em.DeleteEntity(entity);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Flips the strap flag alone; the component limits writes to the buckle system, which would also unbuckle.</summary>
    private static void SetStrapEnabled(StrapComponent strap, bool enabled) =>
        typeof(StrapComponent).GetField("Enabled")!.SetValue(strap, enabled);
}
