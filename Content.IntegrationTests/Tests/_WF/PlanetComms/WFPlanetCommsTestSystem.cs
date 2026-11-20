using System.Collections.Generic;
using Content.Server.Radio;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.PlanetComms;

/// <summary>Records which test radios a message reached, after every real filter.</summary>
public sealed class WFPlanetCommsTestSystem : EntitySystem
{
    public readonly List<EntityUid> Deliveries = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPlanetCommsTestReceiverComponent, RadioReceiveEvent>(OnReceive);
    }

    private void OnReceive(EntityUid uid, WFPlanetCommsTestReceiverComponent component, ref RadioReceiveEvent args)
    {
        Deliveries.Add(uid);
    }
}

/// <summary>Marks a radio the spy watches, without changing what it hears.</summary>
[RegisterComponent]
public sealed partial class WFPlanetCommsTestReceiverComponent : Component;
