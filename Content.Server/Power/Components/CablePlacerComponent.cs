using Robust.Shared.Prototypes;
using Robust.Shared.Prototypes;
using Content.Shared.Power;

namespace Content.Server.Power.Components
{
    [RegisterComponent]
    public sealed partial class CablePlacerComponent : Component
    {
        [DataField("cablePrototypeID", customTypeSerializer:typeof(ProtoId<EntityPrototype>))]
        public string? CablePrototypeId = "CableHV";

        [DataField("blockingWireType")]
        public CableType BlockingCableType = CableType.HighVoltage;
    }
}
