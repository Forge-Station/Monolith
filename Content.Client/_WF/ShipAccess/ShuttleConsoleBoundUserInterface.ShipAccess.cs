using Content.Shared._WF.ShipAccess;

namespace Content.Client.Shuttles.BUI;

public sealed partial class ShuttleConsoleBoundUserInterface
{
    private void WfAccessOpen()
    {
        if (_window == null)
            return;

        _window.ShipAccessLockedRequested += locked => SendMessage(new WFShipAccessSetLockedMessage(locked));
        _window.ShipAccessAddRequested += target => SendMessage(new WFShipAccessAddPlayerMessage(target));
        _window.ShipAccessRemoveRequested += key => SendMessage(new WFShipAccessRemoveMessage(key));
        _window.ShipAccessBuilderRequested += (key, builder) => SendMessage(new WFShipAccessSetBuilderMessage(key, builder));
        _window.ShipAccessDoorRuleRequested += (door, rule) => SendMessage(new WFShipAccessSetDoorRuleMessage(door, rule));
        _window.ShipAccessDoorPlayerRequested += (door, key, listed) => SendMessage(new WFShipAccessSetDoorPlayerMessage(door, key, listed));
        _window.ShipAccessCodesRequested += () => SendMessage(new WFShipAccessRequestCodesMessage());
        _window.ShipAccessShipCodeRequested += code => SendMessage(new WFShipAccessSetShipCodeMessage(code));
        _window.ShipAccessDoorCodeRequested += (door, code) => SendMessage(new WFShipAccessSetDoorCodeMessage(door, code));
    }
}
