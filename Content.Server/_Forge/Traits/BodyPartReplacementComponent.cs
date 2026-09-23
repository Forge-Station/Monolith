using Robust.Shared.GameObjects;

namespace Content.Shared._Forge.Traits;

[RegisterComponent]
public sealed partial class BodyPartReplacementComponent : Component
{
    [DataField]
    public List<BodyPartReplacement> Replacements = new();
}

[DataDefinition]
public sealed partial class BodyPartReplacement
{
    [DataField(required: true)]
    public string Slot = string.Empty;

    [DataField(required: true)]
    public string Prototype = string.Empty;
}