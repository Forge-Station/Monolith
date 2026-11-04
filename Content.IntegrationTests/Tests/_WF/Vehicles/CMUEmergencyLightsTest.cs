#nullable enable
using System.Collections.Generic;
using Content.Client.Actions;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Actions;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>
/// Every vehicle prototype that carries emergency lights: its operator gets the action, using it cycles the lightbar through
/// its modes and the action's toggled state with it, nobody else can use it, and it is taken back when the operator leaves.
/// </summary>
public sealed class CMUEmergencyLightsTest : InteractionTest
{
    private const string ActionPrototype = "CMUActionVehicleEmergencyLights";

    [Test]
    public async Task EveryVehicleWithEmergencyLightsLetsItsOperatorSwitchThemOn()
    {
        var plating = TileMan["Plating"].TileId;
        var vehicles = new List<string>();

        await Server.WaitPost(() =>
        {
            foreach (var proto in ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (!proto.Abstract && proto.TryComp<CMUEmergencyLightsComponent>(out _, Factory))
                    vehicles.Add(proto.ID);
            }

            // Floor under every spot a vehicle stands on.
            for (var x = -3; x < 12; x++)
            {
                for (var y = 0; y < 8; y++)
                {
                    MapSystem.SetTile(MapData.Grid.Owner, MapData.Grid.Comp, new Vector2i(x, y), new Tile(plating));
                }
            }
        });

        Assert.That(vehicles, Has.Count.GreaterThanOrEqualTo(3), "The ambulance, the police car and the paddywagon should carry emergency lights.");
        vehicles.Sort();

        foreach (var id in vehicles)
        {
            await CheckVehicle(id);
        }
    }

    private async Task CheckVehicle(string id)
    {
        var bystander = EntityUid.Invalid;
        var vehicle = EntityUid.Invalid;

        await Server.WaitPost(() =>
        {
            bystander = SEntMan.SpawnAtPosition("InteractionTestMob", SEntMan.GetCoordinates(PlayerCoords));
            vehicle = SEntMan.SpawnEntity(id, new EntityCoordinates(MapData.Grid.Owner, 4.5f, 4.5f));
            SEntMan.GetComponent<WFVehicleEngineComponent>(vehicle).Electrics = true;

            // A driver's eye follows the vehicle; the test mob has none of its own.
            SEntMan.EnsureComponent<EyeComponent>(SPlayer);
            SEntMan.EnsureComponent<EyeComponent>(bystander);
        });
        await RunTicks(5);

        var system = SEntMan.System<RMCVehicleSystem>();
        var lights = SEntMan.GetComponent<CMUEmergencyLightsComponent>(vehicle);

        Assert.That(lights.Mode, Is.Zero, $"{id} starts with its lights on.");
        Assert.That(FindAction(SEntMan, SPlayer), Is.Null, $"{id}: the action was handed out before anyone drove.");

        // The operator gets the action, on the server and on their client.
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<RMCVehicleComponent>(vehicle);
            Assert.That(system.TrySetOperator((vehicle, comp), SPlayer), Is.True, $"{id}: the mob could not take the wheel.");
        });
        await RunTicks(5);

        Assert.That(FindAction(SEntMan, SPlayer), Is.Not.Null, $"{id}: the operator did not get the lights action.");
        Assert.That(FindAction(CEntMan, CPlayer), Is.Not.Null, $"{id}: the action never reached the operator's client.");
        Assert.That(FindAction(SEntMan, bystander), Is.Null, $"{id}: a bystander got the lights action.");

        // Nobody but the operator can use it: the event does nothing on a mob that does not drive this vehicle.
        await Server.WaitPost(() => RaiseAction(bystander));
        await RunTicks(2);
        Assert.That(lights.Mode, Is.Zero, $"{id}: a bystander switched the lights on.");

        // Using it from the client steps through the modes, a lightbar for each, and back to off.
        var modes = lights.LightPrototypes.Count + 1;
        for (var press = 1; press <= modes; press++)
        {
            await PressFromClient(id);

            var expected = press % modes;
            Assert.That(lights.Mode, Is.EqualTo(expected), $"{id}: press {press} left the lights in the wrong mode.");
            CheckLights(id, vehicle, lights, expected);
        }

        // One more press and the lights stay on when the operator leaves.
        await PressFromClient(id);
        Assert.That(lights.Mode, Is.EqualTo(1), $"{id}: the lights did not come back on.");
        var serverAction = FindAction(SEntMan, SPlayer)!.Value;

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<RMCVehicleComponent>(vehicle);
            Assert.That(system.TryRemoveOperator((vehicle, comp)), Is.True);
        });
        await RunTicks(5);

        Assert.That(FindAction(SEntMan, SPlayer), Is.Null, $"{id}: the action stayed behind after the operator left.");
        Assert.That(FindAction(CEntMan, CPlayer), Is.Null, $"{id}: the client kept the action after the operator left.");
        Assert.That(SEntMan.Deleted(serverAction), Is.True, $"{id}: the action entity was left behind.");
        Assert.That(lights.Mode, Is.EqualTo(1), $"{id}: the lights went out with the operator.");
        Assert.That(lights.Lightbar, Is.Not.Null, $"{id}: the lightbar went with the operator.");

        // The former operator cannot use it any more either.
        await Server.WaitPost(() => RaiseAction(SPlayer));
        await RunTicks(2);
        Assert.That(lights.Mode, Is.EqualTo(1), $"{id}: a driver who stepped out switched the lights.");

        // A new operator gets their own action, showing that the lights are on.
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<RMCVehicleComponent>(vehicle);
            Assert.That(system.TrySetOperator((vehicle, comp), bystander), Is.True);
        });
        await RunTicks(5);

        var second = FindAction(SEntMan, bystander);
        Assert.That(second, Is.Not.Null, $"{id}: the next operator did not get the lights action.");
        Assert.That(SEntMan.GetComponent<InstantActionComponent>(second!.Value).Toggled, Is.True,
            $"{id}: the next operator's action does not show the lights as on.");
        Assert.That(FindAction(SEntMan, SPlayer), Is.Null);

        // Taking the vehicle away takes the lightbar with it.
        var lightbar = lights.Lightbar!.Value;
        await Server.WaitPost(() =>
        {
            SEntMan.DeleteEntity(vehicle);
            SEntMan.DeleteEntity(bystander);
        });
        await RunTicks(5);

        Assert.That(SEntMan.Deleted(lightbar), Is.True, $"{id}: the lightbar outlived the vehicle.");
    }

    /// <summary>Presses the action on the client, as the button does, and lets the server answer.</summary>
    private async Task PressFromClient(string id)
    {
        // The action has a short use delay.
        await RunSeconds(0.6f);

        var clientAction = FindAction(CEntMan, CPlayer);
        Assert.That(clientAction, Is.Not.Null, $"{id}: the client has no lights action to press.");
        await Client.WaitPost(() =>
        {
            var actions = CEntMan.System<ActionsSystem>();
            Assert.That(actions.TryGetActionData(clientAction, out var data), Is.True);
            actions.TriggerAction(clientAction!.Value, data!);
        });
        await RunTicks(5);
    }

    /// <summary>The lightbar for a mode exists and is lit, the action shows on or off, and both sides agree on the mode.</summary>
    private void CheckLights(string id, EntityUid vehicle, CMUEmergencyLightsComponent lights, int mode)
    {
        var action = FindAction(SEntMan, SPlayer)!.Value;
        Assert.That(SEntMan.GetComponent<InstantActionComponent>(action).Toggled, Is.EqualTo(mode > 0),
            $"{id}: the action does not show whether the lights are on.");
        Assert.That(CEntMan.GetComponent<InstantActionComponent>(FindAction(CEntMan, CPlayer)!.Value).Toggled, Is.EqualTo(mode > 0),
            $"{id}: the client's action does not show whether the lights are on.");
        Assert.That(CEntMan.GetComponent<CMUEmergencyLightsComponent>(CEntMan.GetEntity(SEntMan.GetNetEntity(vehicle))).Mode,
            Is.EqualTo(mode), $"{id}: the client does not know the mode.");

        if (mode == 0)
        {
            Assert.That(lights.Lightbar, Is.Null, $"{id}: the lightbar stayed when the lights went off.");
            return;
        }

        Assert.That(lights.Lightbar, Is.Not.Null, $"{id}: mode {mode} has no lightbar.");
        var bar = lights.Lightbar!.Value;
        Assert.That(SEntMan.GetComponent<MetaDataComponent>(bar).EntityPrototype?.ID, Is.EqualTo(lights.LightPrototypes[mode - 1].Id),
            $"{id}: mode {mode} has the wrong lightbar.");
        Assert.That(SEntMan.GetComponent<TransformComponent>(bar).ParentUid, Is.EqualTo(vehicle),
            $"{id}: the lightbar is not attached to the vehicle.");
        Assert.That(SEntMan.HasComponent<Robust.Server.GameObjects.PointLightComponent>(bar), Is.True, $"{id}: the lightbar gives no light.");
    }

    private void RaiseAction(EntityUid performer)
    {
        var ev = new CMUEmergencyLightsActionEvent { Performer = performer };
        SEntMan.EventBus.RaiseLocalEvent(performer, ev);
    }

    private EntityUid? FindAction(IEntityManager entMan, EntityUid holder)
    {
        if (!entMan.TryGetComponent(holder, out ActionsComponent? actions))
            return null;

        foreach (var action in actions.Actions)
        {
            if (entMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == ActionPrototype)
                return action;
        }

        return null;
    }
}
