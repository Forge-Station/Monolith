namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    /// <summary>
    /// Lets the character wear every species' markings, hair and facial hair. Set in the creator; random profiles leave it off.
    /// </summary>
    [DataField]
    public bool MismatchedParts { get; set; }

    /// <summary>Copy of this profile with the Mismatched parts option set.</summary>
    public HumanoidCharacterProfile WithMismatchedParts(bool enabled)
    {
        return new(this) { MismatchedParts = enabled };
    }
}
