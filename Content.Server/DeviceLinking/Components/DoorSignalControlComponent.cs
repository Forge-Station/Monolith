using Content.Shared.DeviceLinking;
using Robust.Shared.Prototypes;

namespace Content.Server.DeviceLinking.Components
{
    [RegisterComponent]
    public sealed partial class DoorSignalControlComponent : Component
    {
        [DataField("openPort", customTypeSerializer: typeof(ProtoId<SinkPortPrototype>))]
        public string OpenPort = "Open";

        [DataField("closePort", customTypeSerializer: typeof(ProtoId<SinkPortPrototype>))]
        public string ClosePort = "Close";

        [DataField("togglePort", customTypeSerializer: typeof(ProtoId<SinkPortPrototype>))]
        public string TogglePort = "Toggle";

        [DataField("boltPort", customTypeSerializer: typeof(ProtoId<SinkPortPrototype>))]
        public string InBolt = "DoorBolt";

        [DataField("onOpenPort", customTypeSerializer: typeof(ProtoId<SourcePortPrototype>))]
        public string OutOpen = "DoorStatus";
    }
}
