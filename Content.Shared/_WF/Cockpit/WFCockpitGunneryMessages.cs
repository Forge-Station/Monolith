using Content.Shared._Mono.FireControl;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Cockpit;

/// <summary>Starts or ends the seated pilot's optional gunnery link; Controlling is set while the pilot operates the guns.</summary>
[Serializable, NetSerializable]
public sealed class WFCockpitGunnerySessionMessage(bool active, bool controlling = false) : BoundUserInterfaceMessage
{
    public bool Active = active;
    public bool Controlling = controlling;
}

/// <summary>Targets one previously advertised gunnery console with an existing, whitelisted command.</summary>
[Serializable, NetSerializable]
public sealed class WFCockpitGunneryCommandMessage(NetEntity console, BoundUserInterfaceMessage command) : BoundUserInterfaceMessage
{
    public NetEntity Console = console;
    public BoundUserInterfaceMessage Command = command;
}

/// <summary>Supplies the current nearby gunnery console and its normal weapon telemetry to one pilot.</summary>
[Serializable, NetSerializable]
public sealed class WFCockpitGunneryStateMessage(NetEntity? console, FireControlConsoleBoundInterfaceState? state) : BoundUserInterfaceMessage
{
    public NetEntity? Console = console;
    public FireControlConsoleBoundInterfaceState? State = state;
}
