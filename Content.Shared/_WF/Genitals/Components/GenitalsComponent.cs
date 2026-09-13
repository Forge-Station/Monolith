using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.Genitals.Components;

/// <summary>Body-level mirror of genital organs plus owner-controlled runtime state.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true, true), AutoGenerateComponentPause]
public sealed partial class GenitalsComponent : Component
{
    /// <summary>State goes only to the owner and to viewers with Adult content on (server GenitalsSystem.Privacy.cs).</summary>
    public override bool SessionSpecific => true;

    // Mirrored from organs by GenitalOrganSystem.Recompute (server). Null means no organ in that slot,
    // or the owner's master switch is off (the organs stay in the body).

    [DataField, AutoNetworkedField]
    public GenitalOrganState? Penis;

    [DataField, AutoNetworkedField]
    public GenitalOrganState? Testicles;

    [DataField, AutoNetworkedField]
    public GenitalOrganState? Vagina;

    [DataField, AutoNetworkedField]
    public bool Womb;

    [DataField, AutoNetworkedField]
    public GenitalOrganState? Breasts;

    // Owner-controlled runtime state. Defaults come from the profile when anatomy is built; never written back.

    /// <summary>Arousal 0-100.</summary>
    [DataField, AutoNetworkedField]
    public byte Arousal;

    [DataField, AutoNetworkedField]
    public GenitalRevealMode RevealMode;

    [DataField, AutoNetworkedField]
    public GenitalVisibilitySet Visibility;

    [DataField, AutoNetworkedField]
    public UndergarmentFlags Undergarments;

    /// <summary>Others cannot start removing an undergarment before this time.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan StripCooldownUntil;

    /// <summary>Genetic template for cloning and for building organs once consent is known. Server only.</summary>
    [DataField]
    public GenitalProfile? SourceProfile;

    /// <summary>Organs were built from SourceProfile at least once. Server only.</summary>
    [DataField]
    public bool OrgansBuilt;

    /// <summary>Client-only: undergarment categories hidden at the last marking rebuild (change detection).</summary>
    [ViewVariables]
    public UndergarmentFlags LastHiddenUndergarments;

    /// <summary>Client-only creator preview mode; ignored in game.</summary>
    [ViewVariables]
    public GenitalPreviewMode PreviewMode;

    /// <summary>Client-only creator arousal preview; null uses the real value.</summary>
    [ViewVariables]
    public ArousalState? PreviewArousal;

    /// <summary>Client-only: this is a lobby doll filled from profile data.</summary>
    [ViewVariables]
    public bool IsPreview;

    /// <summary>Client-only: arousal in the last applied server state. The panel compares requests with it, since the server may still refuse a predicted value.</summary>
    [ViewVariables]
    public byte ConfirmedArousal;

    /// <summary>Any organ is mirrored.</summary>
    public bool HasAnyOrgan => Penis != null || Testicles != null || Vagina != null || Womb || Breasts != null;

    /// <summary>The slot holds an organ, external or internal.</summary>
    public bool HasOrgan(GenitalSlot slot)
    {
        return slot switch
        {
            GenitalSlot.Penis => Penis != null,
            GenitalSlot.Testicles => Testicles != null,
            GenitalSlot.Vagina => Vagina != null,
            GenitalSlot.Womb => Womb,
            GenitalSlot.Breasts => Breasts != null,
            _ => false,
        };
    }

    /// <summary>The slot holds an organ that can be exposed; the womb and internal testicles cannot.</summary>
    public bool HasExternalOrgan(GenitalSlot slot)
    {
        return slot switch
        {
            GenitalSlot.Testicles => Testicles is { } testicles && testicles.Testicles != TesticleType.Internal,
            GenitalSlot.Womb => false,
            _ => HasOrgan(slot),
        };
    }
}

/// <summary>Per-organ visibility preference of the person (not the tissue), so it stays on the body.</summary>
[DataDefinition, Serializable, NetSerializable]
public partial struct GenitalVisibilitySet : IEquatable<GenitalVisibilitySet>
{
    /// <summary>The sheath follows the penis.</summary>
    [DataField]
    public GenitalVisibility Penis;

    [DataField]
    public GenitalVisibility Testicles;

    [DataField]
    public GenitalVisibility Vagina;

    [DataField]
    public GenitalVisibility Breasts;

    /// <summary>Visibility of one slot; the womb is internal and always Normal.</summary>
    public readonly GenitalVisibility Get(GenitalSlot slot)
    {
        return slot switch
        {
            GenitalSlot.Penis => Penis,
            GenitalSlot.Testicles => Testicles,
            GenitalSlot.Vagina => Vagina,
            GenitalSlot.Breasts => Breasts,
            _ => GenitalVisibility.Normal,
        };
    }

    /// <summary>Copy with one slot changed; the womb is ignored.</summary>
    public readonly GenitalVisibilitySet With(GenitalSlot slot, GenitalVisibility value)
    {
        var copy = this;
        switch (slot)
        {
            case GenitalSlot.Penis:
                copy.Penis = value;
                break;
            case GenitalSlot.Testicles:
                copy.Testicles = value;
                break;
            case GenitalSlot.Vagina:
                copy.Vagina = value;
                break;
            case GenitalSlot.Breasts:
                copy.Breasts = value;
                break;
        }

        return copy;
    }

    public readonly bool Equals(GenitalVisibilitySet other)
    {
        return Penis == other.Penis
               && Testicles == other.Testicles
               && Vagina == other.Vagina
               && Breasts == other.Breasts;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is GenitalVisibilitySet other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(Penis, Testicles, Vagina, Breasts);
    }

    public static bool operator ==(GenitalVisibilitySet left, GenitalVisibilitySet right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GenitalVisibilitySet left, GenitalVisibilitySet right)
    {
        return !left.Equals(right);
    }
}
