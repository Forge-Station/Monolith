using Robust.Shared.Prototypes;


namespace Content.Server._NF.Construction.Components
{
    /// <summary>
    /// Used for construction graphs in building tabletop computers.
    /// </summary>
    [RegisterComponent]
    public sealed partial class ComputerTabletopBoardComponent : Component
    {
        [DataField("prototype", customTypeSerializer: typeof(ProtoId<EntityPrototype>))]
        public string? Prototype { get; private set; }
    }
}
