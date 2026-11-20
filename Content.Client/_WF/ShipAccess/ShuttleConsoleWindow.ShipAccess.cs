using Content.Shared._WF.ShipAccess;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    /// <summary>The owner flipped the ship lock on the access tab.</summary>
    public event Action<bool>? ShipAccessLockedRequested;

    /// <summary>The owner wants a nearby person on the allow list.</summary>
    public event Action<NetEntity>? ShipAccessAddRequested;

    /// <summary>The owner wants a card off the allow list.</summary>
    public event Action<WFShipAccessKey>? ShipAccessRemoveRequested;

    /// <summary>The owner marked or unmarked a listed card as a builder's.</summary>
    public event Action<WFShipAccessKey, bool>? ShipAccessBuilderRequested;

    /// <summary>The owner picked a rule for a door.</summary>
    public event Action<NetEntity, WFDoorAccessRule>? ShipAccessDoorRuleRequested;

    /// <summary>The owner ticked or unticked a card on a door.</summary>
    public event Action<NetEntity, WFShipAccessKey, bool>? ShipAccessDoorPlayerRequested;

    /// <summary>The owner opened the access tab and wants the codes.</summary>
    public event Action? ShipAccessCodesRequested;

    /// <summary>The owner set or cleared the ship code.</summary>
    public event Action<string?>? ShipAccessShipCodeRequested;

    /// <summary>The owner set or cleared a door's code.</summary>
    public event Action<NetEntity, string?>? ShipAccessDoorCodeRequested;

    private void WfAccessInitialize()
    {
        AccessContainer.LockedChanged += locked => ShipAccessLockedRequested?.Invoke(locked);
        AccessContainer.AddRequested += target => ShipAccessAddRequested?.Invoke(target);
        AccessContainer.RemoveRequested += key => ShipAccessRemoveRequested?.Invoke(key);
        AccessContainer.BuilderChanged += (key, builder) => ShipAccessBuilderRequested?.Invoke(key, builder);
        AccessContainer.DoorRuleChanged += (door, rule) => ShipAccessDoorRuleRequested?.Invoke(door, rule);
        AccessContainer.DoorPlayerChanged += (door, key, listed) => ShipAccessDoorPlayerRequested?.Invoke(door, key, listed);
        AccessContainer.CodesRequested += () => ShipAccessCodesRequested?.Invoke();
        AccessContainer.ShipCodeChanged += code => ShipAccessShipCodeRequested?.Invoke(code);
        AccessContainer.DoorCodeChanged += (door, code) => ShipAccessDoorCodeRequested?.Invoke(door, code);
    }

    private void WfSetAccessMode(bool active)
    {
        AccessContainer.Visible = active;
    }

    private void WfAccessUpdateState(EntityUid? shuttle, EntityUid console)
    {
        AccessContainer.SetShuttle(shuttle);
        AccessContainer.SetConsole(console);
    }
}
