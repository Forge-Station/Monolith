#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.SectorControl;

/// <summary>Secret stations, by name or by IFF, are left out of the station list every sector driver reads.</summary>
[TestOf(typeof(WFEncounterSchedulerSystem))]
public sealed class WFSectorHiddenStationTest : WFSectorTestBase
{
    /// <summary>A station named for the hidden list, or one whose IFF hides it or its label, is not a station to the drivers or the open-space checks; a plain one is.</summary>
    [Test]
    public async Task SecretStationsAreNotListedOrNamed()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var scheduler = Server.System<WFEncounterSchedulerSystem>();
            var shuttles = Server.System<SharedShuttleSystem>();
            var map = MapData.MapId;

            var plain = SpawnStation(new Vector2(-12000f, -9000f), "Test Outpost");
            var byName = SpawnStation(new Vector2(-30000f, -9000f), "PDV Helios Annex");
            var secret = new[] { IFFFlags.Hide, IFFFlags.HideLabel, IFFFlags.HideLabelAlways }
                .Select((flag, i) =>
                {
                    var grid = SpawnStation(new Vector2(30000f + 9000f * i, 20000f), $"Test Secret {flag}");
                    shuttles.AddIFFFlag(grid, flag);
                    return grid;
                }).ToList();

            Assert.That(scheduler.IsHidden(plain), Is.False);
            Assert.That(scheduler.IsHidden(byName), Is.True, "Matches the hidden list.");
            Assert.That(secret.Select(scheduler.IsHidden), Is.All.True, "Hidden by IFF.");

            var stations = territory.Stations(map).Select(station => station.Owner).ToList();
            Assert.That(stations, Does.Contain(plain));
            Assert.That(stations, Does.Not.Contain(byName));
            foreach (var grid in secret)
            {
                Assert.That(stations, Does.Not.Contain(grid));
                Assert.That(territory.StationsIn(map, territory.GridCell(grid)), Is.Empty, "Its cell has no station to blockade.");
            }

            var near = Server.System<SharedTransformSystem>().GetWorldPosition(secret[2]) + new Vector2(1000f, 0f);
            Assert.That(scheduler.IsClearSpace(map, near, 100f, 0f), Is.False, "Spawns keep clear of it.");
            Assert.That(scheduler.IsClearSpace(map, new Vector2(60000f, -60000f), 100f, 0f), Is.True, "Open space elsewhere is clear.");
        });
    }
}
