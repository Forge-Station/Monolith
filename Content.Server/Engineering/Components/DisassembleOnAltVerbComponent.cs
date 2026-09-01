using System.Threading;
using Robust.Shared.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Engineering.Components
{
    [RegisterComponent]
    public sealed partial class DisassembleOnAltVerbComponent : Component
    {
        [DataField("prototype", customTypeSerializer: typeof(ProtoId<EntityPrototype>))]
        public string? Prototype { get; private set; }

        [DataField("doAfter")]
        public float DoAfterTime = 0;
    }
}
