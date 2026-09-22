using Robust.Shared.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Construction.Components
{
    [RegisterComponent, ComponentProtoName("Computer")]
    public sealed partial class ComputerComponent : Component
    {
        [DataField("board", customTypeSerializer:typeof(ProtoId<EntityPrototype>))]
        public string? BoardPrototype;
    }
}
