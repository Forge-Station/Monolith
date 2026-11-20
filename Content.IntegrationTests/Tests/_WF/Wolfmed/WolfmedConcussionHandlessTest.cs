#nullable enable
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Hands.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>A concussion only drops what a body can hold.</summary>
[TestFixture]
public sealed class WolfmedConcussionHandlessTest
{
    /// <summary>A handless mob's concussion passes its drop roll without logging a missing hands component.</summary>
    [Test]
    public async Task AHandlessMobDropsNothing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var mob = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            mob = server.EntMan.SpawnEntity("WFWreckZombieVerda", new EntityCoordinates(map.MapUid, default));
            // Zombifying a mob takes its hands.
            server.EntMan.RemoveComponent<HandsComponent>(mob);
            var concussion = server.EntMan.EnsureComponent<WolfmedConcussionComponent>(mob);
            concussion.Drop = true;
            concussion.DropChance = 1000f;
        });

        // A logged error fails the test, so getting past the first tick of the concussion is the check.
        await pair.RunTicksSync(90);

        await server.WaitPost(() => server.EntMan.DeleteEntity(mob));
        await pair.CleanReturnAsync();
    }
}
