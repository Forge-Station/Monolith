using Robust.Shared.Audio;

namespace Content.Server._WF.CavernEncounters;

/// <summary>
/// A lit lantern left at the Lamplighter's lair that goes out, with a fizzle, when a player comes near: the lights
/// dying ahead of you. The Lamplighter neither douses it nor is held by it.
/// </summary>
[RegisterComponent]
public sealed partial class WFCavernOmenLightComponent : Component
{
    /// <summary>Tiles from it within which a player puts it out.</summary>
    [DataField]
    public float TriggerRange = 14f;

    /// <summary>The longest wait between a player coming near and the light dying.</summary>
    [DataField]
    public TimeSpan MaxDelay = TimeSpan.FromSeconds(2);

    /// <summary>Played where the light dies.</summary>
    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>When it goes out, once a player has come near.</summary>
    [ViewVariables]
    public TimeSpan? DiesAt;
}
