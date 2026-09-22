using Robust.Shared.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Construction.Components
{
    /// <summary>
    /// Used for construction graphs in building computers.
    /// </summary>
    [RegisterComponent]
    public sealed partial class ComputerBoardComponent : Component
    {
        [DataField("prototype", customTypeSerializer: typeof(ProtoId<EntityPrototype>))]
        public string? Prototype { get; private set; }
    }
}
