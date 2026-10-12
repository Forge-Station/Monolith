#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Numerics;
using System.Linq;
using System.Reflection;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.GameTicking;
using Content.Shared._Mono.CCVar;
using Content.Shared._WF.SectorControl;
using Content.Shared.Station.Components;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.SectorControl;

/// <summary>Set-up for the sector control and Reaver campaign tests: a round on the test map as the sector map, and grids for ships and stations.</summary>
public abstract class WFSectorTestBase : InteractionTest
{
    /// <summary>The faction the tests claim for.</summary>
    protected const string Holder = "WFTestSectorHolder";

    /// <summary>A second faction, so there is someone else's cell to be refused.</summary>
    protected const string Rival = "WFTestSectorRival";

    // Test prototypes are loaded into every pair, so they are declared once.
    [TestPrototypes]
    private const string FactionPrototypes = @"
- type: wfSectorFaction
  id: WFTestSectorHolder
  name: cmd-wf_sector-hint-faction
  color: Red
  blockades: true
- type: wfSectorFaction
  id: WFTestSectorRival
  name: cmd-wf_sector-hint-sub
  color: Blue
";

    /// <summary>A shuttle airlock: the docking port that makes a grid a station to the storyteller.</summary>
    private const string DockingPort = "AirlockShuttle";

    // Spawned ships play sounds, and the client's room echo asserts on audio whose parent changes mid-setup.
    [SetUp]
    public async Task DisableRoomEcho()
    {
        await Client.WaitPost(() => Client.ResolveDependency<IConfigurationManager>().SetCVar(MonoCVars.AreaEchoEnabled, false));
    }

    [TearDown]
    public async Task RestoreRoomEcho()
    {
        await Client.WaitPost(() => Client.ResolveDependency<IConfigurationManager>().SetCVar(MonoCVars.AreaEchoEnabled, MonoCVars.AreaEchoEnabled.DefaultValue));
    }

    /// <summary>Puts the pair's dummy ticker in a round on the test map, which it never does itself.</summary>
    protected async Task StartRound()
    {
        await Server.WaitPost(() =>
        {
            var ticker = Server.System<GameTicker>();
            SetPrivate(ticker, nameof(GameTicker.DefaultMap), MapData.MapId);
            SetPrivate(ticker, nameof(GameTicker.RunLevel), GameRunLevel.InRound);
        });
    }

    private static void SetPrivate(GameTicker ticker, string property, object value)
    {
        typeof(GameTicker).GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.GetSetMethod(true)!.Invoke(ticker, new[] { value });
    }

    /// <summary>The map position of a cell's centre at the default cell size.</summary>
    protected static Vector2 CentreOf(WFSectorCell cell)
    {
        return WFSectorHex.Centre(cell, 3000f);
    }

    /// <summary>A one-tile grid on the test map, in a place; a ship or station for the sector systems to find.</summary>
    protected EntityUid SpawnGrid(Vector2 position, string? name = null)
    {
        var grid = Server.ResolveDependency<IMapManager>().CreateGridEntity(MapData.MapId);
        Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
        Server.System<SharedTransformSystem>().SetCoordinates(grid.Owner, new EntityCoordinates(MapData.MapUid, position));
        if (name != null)
            Server.System<MetaDataSystem>().SetEntityName(grid.Owner, name);

        return grid.Owner;
    }

    /// <summary>A grid with a docking port and station membership: a station to the storyteller.</summary>
    protected EntityUid SpawnStation(Vector2 position, string name)
    {
        var grid = SpawnGrid(position, name);
        SEntMan.SpawnAtPosition(DockingPort, new EntityCoordinates(grid, new Vector2(0.5f)));
        SEntMan.EnsureComponent<StationMemberComponent>(grid);
        return grid;
    }

    /// <summary>Moves the test player aboard a grid.</summary>
    protected void Board(EntityUid grid)
    {
        Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(grid, new Vector2(0.5f)));
    }

    /// <summary>Moves a grid to a map position.</summary>
    protected void Place(EntityUid grid, Vector2 position)
    {
        Server.System<SharedTransformSystem>().SetCoordinates(grid, new EntityCoordinates(MapData.MapUid, position));
    }

    /// <summary>Puts the player back where the test map began.</summary>
    protected void Disembark()
    {
        Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
    }

    /// <summary>How many chat lines the client has had; a pooled client can keep the lines of earlier tests.</summary>
    protected int ChatCount()
    {
        return Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>().History.Count;
    }

    /// <summary>The chat lines the client has had after the first <paramref name="skip"/>.</summary>
    protected List<string> ChatSince(int skip)
    {
        var chat = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        return chat.History.Skip(skip).Select(entry => entry.Msg.Message).ToList();
    }

    protected async Task WaitUntilServer(Func<bool> condition, int maxTicks)
    {
        var met = false;
        for (var waited = 0; waited < maxTicks && !met; waited += 20)
        {
            await RunTicks(20);
            await Server.WaitPost(() => met = condition());
        }

        Assert.That(met, Is.True, "The condition was not met in time.");
    }
}
