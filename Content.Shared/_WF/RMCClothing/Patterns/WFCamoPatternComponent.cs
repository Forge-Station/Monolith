using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.RMCClothing.Patterns;

/// <summary>Clothing drawn in several camouflage patterns, picked from the item's "Pattern" verbs.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(WFCamoPatternSystem))]
public sealed partial class WFCamoPatternComponent : Component
{
    /// <summary>The patterns, in menu order; the first is the item's own look.</summary>
    [DataField(required: true)]
    public List<WFCamoPattern> Patterns = new();

    /// <summary>Index of the pattern worn now.</summary>
    [DataField, AutoNetworkedField]
    public int Current;
}

/// <summary>One pattern: its name for the menu and the prototype whose sprite and clothing visuals it copies.</summary>
[DataDefinition]
public sealed partial class WFCamoPattern
{
    /// <summary>Suffix of the <c>wf-camo-pattern-</c> Fluent string.</summary>
    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField(required: true)]
    public EntProtoId Look;
}
