using Robust.Shared.Serialization;

namespace Content.Shared.Chemistry.Reagent;

[ImplicitDataDefinitionForInheritors, Serializable, NetSerializable]
public sealed partial class SpeciesData : ReagentData
{
    [DataField(required: true)]
    public string Species = string.Empty;

    public override ReagentData Clone()
    {
        return new SpeciesData { Species = Species };
    }

    public override bool Equals(ReagentData? other)
    {
        return other is SpeciesData data && data.Species == Species;
    }

    public override int GetHashCode()
    {
        return Species.GetHashCode();
    }
}
