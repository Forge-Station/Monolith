using Content.Shared._WF.Headshot;

namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    /// <summary>Image URL shown when someone examines this character with their face uncovered. Empty for none.</summary>
    [DataField]
    public string HeadshotUrl { get; set; } = string.Empty;

    public HumanoidCharacterProfile WithHeadshotUrl(string url)
    {
        return new(this) { HeadshotUrl = url };
    }

    private void EnsureHeadshotValid()
    {
        // Crafted network data can deliver null.
        HeadshotUrl = HeadshotRules.Clean(HeadshotUrl);
    }
}
