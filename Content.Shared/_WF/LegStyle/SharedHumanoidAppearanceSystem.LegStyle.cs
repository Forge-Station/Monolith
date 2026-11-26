using Content.Shared._WF.LegStyle;
using Content.Shared.Preferences;

namespace Content.Shared.Humanoid;

public abstract partial class SharedHumanoidAppearanceSystem
{
    /// <summary>
    /// Swaps the leg layers for the profile's leg style. Runs before body parts copy their sprites, so a
    /// severed leg keeps its shape.
    /// </summary>
    protected void ApplyLegStyle(HumanoidCharacterProfile profile, HumanoidAppearanceComponent humanoid)
    {
        if (humanoid.LegStyle is { } oldId && _proto.TryIndex(oldId, out var old))
        {
            foreach (var layer in old.Sprites.Keys)
                humanoid.CustomBaseLayers.Remove(layer);
        }

        var style = LegStyleRules.Find(profile.Species, profile.LegStance, _proto);
        humanoid.LegStyle = style?.ID;
        if (style == null)
            return;

        // A custom base layer doesn't follow the skin colour by itself.
        foreach (var (layer, sprite) in style.Sprites)
            humanoid.CustomBaseLayers[layer] = new(sprite, humanoid.SkinColor);
    }
}
