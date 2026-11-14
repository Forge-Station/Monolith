using Content.Shared._WF.CustomMarkings;

namespace Content.Shared.Humanoid;

public sealed partial class HumanoidAppearanceComponent
{
    /// <summary>
    /// Custom markings the wearer has hidden for now. They stay in <see cref="CustomMarkings"/> and come back when
    /// shown again or when the character is next loaded.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<CustomMarking> HiddenCustomMarkings = new();
}
