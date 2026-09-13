using Content.Shared._WF.Genitals.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Genitals.Components;

/// <summary>Render and examine data for one organ. Shared by organs, the body mirror and the preview.</summary>
[DataDefinition, Serializable, NetSerializable]
public partial struct GenitalOrganState : IEquatable<GenitalOrganState>
{
    /// <summary>Null for the womb and internal testicles.</summary>
    [DataField]
    public ProtoId<GenitalShapePrototype>? Shape;

    /// <summary>Logical step: penis 1-5 (from LengthCm), testicles 1-5, breasts 1-19.</summary>
    [DataField]
    public byte Step;

    /// <summary>Penis only.</summary>
    [DataField]
    public byte LengthCm;

    /// <summary>Penis only.</summary>
    [DataField]
    public SheathType Sheath;

    /// <summary>Testicles only (External or Internal).</summary>
    [DataField]
    public TesticleType Testicles;

    /// <summary>Breasts only.</summary>
    [DataField]
    public bool Lactation;

    /// <summary>Resolved when built (skin colour if MatchSkin).</summary>
    [DataField]
    public Color Color;

    /// <summary>Penis only, resolved when built.</summary>
    [DataField]
    public Color SheathColor;

    /// <summary>Re-tint on skin changes and UI hint.</summary>
    [DataField]
    public bool MatchSkin;

    /// <summary>Same as MatchSkin, for the sheath outer.</summary>
    [DataField]
    public bool SheathMatchSkin;

    public readonly bool Equals(GenitalOrganState other)
    {
        return Shape == other.Shape
               && Step == other.Step
               && LengthCm == other.LengthCm
               && Sheath == other.Sheath
               && Testicles == other.Testicles
               && Lactation == other.Lactation
               && Color.Equals(other.Color)
               && SheathColor.Equals(other.SheathColor)
               && MatchSkin == other.MatchSkin
               && SheathMatchSkin == other.SheathMatchSkin;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is GenitalOrganState other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(
            HashCode.Combine(Shape, Step, LengthCm, Sheath, Testicles, Lactation),
            HashCode.Combine(Color, SheathColor, MatchSkin, SheathMatchSkin));
    }

    public static bool operator ==(GenitalOrganState left, GenitalOrganState right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GenitalOrganState left, GenitalOrganState right)
    {
        return !left.Equals(right);
    }
}
