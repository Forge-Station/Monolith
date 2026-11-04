#nullable enable
using System.Linq;
using Content.Server.Decals;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>Tread marks run either side of the hull, not off to one side of it.</summary>
[TestFixture]
public sealed class VehicleTrackMarkTest
{
    [Test]
    public async Task TreadMarksStraddleTheHull()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        await server.WaitPost(() =>
        {
            var engine = entMan.EnsureComponent<WFVehicleEngineComponent>(rig.Vehicle);
            engine.Electrics = engine.Running = true;
        });
        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(120);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.None));
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var position = VehicleRigs.LocalPosition(rig);
            var marks = server.System<DecalSystem>().GetDecalsInRange(rig.Grid, position, 30f, decal => decal.Id == "WFVehicleTrack");
            Assert.That(marks, Is.Not.Empty, "A running vehicle that drove should leave tread marks.");

            // A decal's coordinates are its bottom-left corner.
            var centres = marks.Select(mark => mark.Decal.Coordinates.Y + 0.5f).ToList();
            var above = centres.Where(y => y > position.Y).ToList();
            var below = centres.Where(y => y < position.Y).ToList();
            Assert.That(above, Is.Not.Empty.And.Count.EqualTo(below.Count), $"The tracks should run on both sides of {position.Y}: {string.Join(", ", centres)}");
            Assert.That(above.Average() - position.Y, Is.EqualTo(position.Y - below.Average()).Within(0.05f),
                $"The tracks should sit evenly either side of the hull: {string.Join(", ", centres)}");
        });

        await pair.CleanReturnAsync();
    }
}
