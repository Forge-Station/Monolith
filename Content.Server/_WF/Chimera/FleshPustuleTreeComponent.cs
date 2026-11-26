namespace Content.Server._WF.Chimera;

/// <summary>Each tree yields once; the harvested state is preserved across saves.</summary>
[RegisterComponent]
public sealed partial class WFFleshPustuleTreeComponent : Component
{
    [DataField] public bool Harvested;
}

/// <summary>A harvested pustule that can be planted or thrown to burst.</summary>
[RegisterComponent]
public sealed partial class WFFleshPustuleItemComponent : Component
{
    [ViewVariables] public bool Thrown;
    [ViewVariables] public bool Consumed;
}
