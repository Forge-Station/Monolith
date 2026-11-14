using Content.Shared.Humanoid;

namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    /// <summary>Passport nationality. Jobs can overwrite it when the character spawns.</summary>
    [DataField]
    public string Nationality { get; set; } = SharedHumanoidAppearanceSystem.DefaultNationality;

    public HumanoidCharacterProfile WithNationality(string nationality) => new(this) { Nationality = nationality };
}
