#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WF.Wolfmed.Gore;
using Content.Server.Body.Systems;
using Content.Server.Decals;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Body.Part;
using Content.Shared.Decals;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Players;
using NUnit.Framework;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// Playtest 4 (GORE): extreme trauma leaves gib decals around the body, the blood layer in the blood colour, the
/// skin-coloured bits in the skin colour; a limb off leaves a few, a gibbed body leaves them all; a machine none.
/// </summary>
[TestFixture]
public sealed class WolfmedGibDecalTest : GameTest
{
    private int Count(EntityUid body, string? suffix = null)
    {
        var xform = SEntMan.GetComponent<TransformComponent>(body);
        var grid = xform.GridUid!.Value;
        var position = SEntMan.System<SharedTransformSystem>().GetMoverCoordinates(body, xform).Position;
        return SEntMan.System<DecalSystem>().GetDecalsInRange(grid, position, 4f)
            .Count(d => d.Decal.Id.StartsWith(WolfmedGibDecalSystem.Prefix) &&
                        (suffix == null ? !d.Decal.Id.EndsWith("_meat") && !d.Decal.Id.EndsWith("_flesh") : d.Decal.Id.EndsWith(suffix)));
    }

    [Test]
    public async Task LimbOffAndGibLeaveGibsTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.GibDecals, true);
        await OverrideCVar(Side.Server, WolfmedCVars.GibSpread, 1f);
        await OverrideCVar(Side.Server, WolfmedCVars.GibsDismemberment, 2);
        await OverrideCVar(Side.Server, WolfmedCVars.GibsGib, 7);
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var minds = SEntMan.System<SharedMindSystem>();
        var session = Server.PlayerMan.GetSessionById(Client.Session!.UserId);
        EntityUid a = default;
        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            // The client's player stands in the body: the server only sends decal chunks to a session that can see them.
            minds.WipeMind(session.ContentData()?.Mind);
            a = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            minds.TransferTo(minds.CreateMind(session.UserId).Owner, a);
        });
        await RunSeconds(2);
        Assert.That(session.AttachedEntity, Is.EqualTo(a), "the player did not attach to the body.");

        await Server.WaitAssertion(() =>
        {
            Assert.That(Count(a), Is.EqualTo(0), "gibs before any trauma.");
            Assert.That(SEntMan.System<AmputationSystem>().TryAmputate(a, s.Part(a, BodyPartType.Arm, BodyPartSymmetry.Left)), Is.True);
            Assert.That(Count(a), Is.EqualTo(2), "a limb off did not leave two gibs.");

            var blood = SEntMan.System<WolfmedGoreSystem>().GetBloodColor(a)!.Value;
            var xform = SEntMan.GetComponent<TransformComponent>(a);
            var decals = SEntMan.System<DecalSystem>().GetDecalsInRange(xform.GridUid!.Value, xform.Coordinates.Position, 4f)
                .Select(d => d.Decal).Where(d => d.Id.StartsWith(WolfmedGibDecalSystem.Prefix)).ToList();
            Assert.That(decals.Where(d => !d.Id.EndsWith("_meat") && !d.Id.EndsWith("_flesh")).All(d => d.Color == blood), Is.True,
                "a blood layer is not the blood colour.");
            var skin = SEntMan.GetComponent<HumanoidAppearanceComponent>(a).SkinColor;
            Assert.That(decals.Where(d => d.Id.EndsWith("_flesh")).All(d => d.Color == skin), Is.True, "a flesh layer is not the skin colour.");
            Assert.That(decals.All(d => d.Cleanable), Is.True, "a gib is not cleanable.");
        });

        // Playtest 4: "haven't been seeing the gibs". The client has to receive the chunk and resolve every gib's art.
        await RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            var grid = CEntMan.GetComponent<DecalGridComponent>(ToClientUid(map.Grid.Owner));
            var seen = grid.ChunkCollection.ChunkCollection.Values.SelectMany(c => c.Decals.Values)
                .Count(d => d.Id.StartsWith(WolfmedGibDecalSystem.Prefix));
            Assert.That(seen, Is.GreaterThan(0), "the client never received a gib decal.");
            var sprites = CEntMan.System<SpriteSystem>();
            foreach (var proto in CProtoMan.EnumeratePrototypes<DecalPrototype>())
            {
                if (!proto.ID.StartsWith(WolfmedGibDecalSystem.Prefix))
                    continue;

                var texture = sprites.Frame0(proto.Sprite);
                Assert.That(texture.Width == 32 && texture.Height == 32, Is.True, $"{proto.ID} has no 32x32 art.");
            }
        });

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<BodySystem>().GibBody(a);
        });
        await RunTicksSync(5);
        await Server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var all = SEntMan.System<DecalSystem>().GetDecalsInRange(grid, map.GridCoords.Position, 6f)
                .Count(d => d.Decal.Id.StartsWith(WolfmedGibDecalSystem.Prefix) && !d.Decal.Id.EndsWith("_meat") && !d.Decal.Id.EndsWith("_flesh"));
            Assert.That(all, Is.EqualTo(9), "the gib did not add seven more gibs.");
        });
    }

    [Test]
    public async Task MachinesLeaveNoGibsTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.GibDecals, true);
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid ipc = default;
        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            ipc = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
        });
        await RunSeconds(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<AmputationSystem>().TryAmputate(ipc, s.Part(ipc, BodyPartType.Arm, BodyPartSymmetry.Left)), Is.True);
            Assert.That(Count(ipc), Is.EqualTo(0), "a chassis left gibs.");
        });
    }
}
