using Content.Server.Atmos.Piping.Binary.EntitySystems;
using Content.Shared.DeviceLinking;
using Robust.Shared.Prototypes;

namespace Content.Server.Atmos.Piping.Binary.Components;

[RegisterComponent, Access(typeof(SignalControlledValveSystem))]
public sealed partial class SignalControlledValveComponent : Component
{
    [DataField("openPort", customTypeSerializer: typeof(ProtoId<SinkPortPrototype>))]
    public string OpenPort = "Open";

    [DataField("closePort", customTypeSerializer: typeof(ProtoId<SinkPortPrototype>))]
    public string ClosePort = "Close";

    [DataField("togglePort", customTypeSerializer: typeof(ProtoId<SinkPortPrototype>))]
    public string TogglePort = "Toggle";
}
