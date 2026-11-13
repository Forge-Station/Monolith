using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Weapons.MeleeStance;

/// <summary>
/// Gives a melee weapon several "stances" (modes) that the wielder switches between with an action.
/// Each stance can completely redefine the weapon: damage, attack rate, range, reflect, stamina damage,
/// item size, in-hand / equipped sprites, world sprite layers (with an optional transition animation),
/// point light and even a set of extra components that only exist while the stance is active.
/// </summary>
/// <remarks>
/// Any stat left <c>null</c> in a stance falls back to the value from the entity prototype,
/// so a stance only has to describe what makes it different from the base weapon.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), Access(typeof(MeleeStanceSystem))]
public sealed partial class MeleeStanceComponent : Component
{
    /// <summary>
    /// All stances of this weapon. The action cycles through them in order.
    /// </summary>
    [DataField(required: true)]
    public List<MeleeStanceMode> Modes = new();

    /// <summary>
    /// Index of the currently active stance. Can be set in YAML to choose the starting stance.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public int CurrentMode;

    /// <summary>
    /// Action granted to the holder that switches to the next stance.
    /// </summary>
    [DataField]
    public EntProtoId<InstantActionComponent> Action = "ActionMeleeStanceSwitch";

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    /// <summary>
    /// If true the action is only granted while the weapon is in hands (not when worn on the belt / back).
    /// </summary>
    [DataField]
    public bool RequireInHand = true;

    /// <summary>
    /// If true, using the weapon in hand (Z by default) also switches the stance.
    /// Leave false if the weapon already uses <c>ItemToggle</c> or other use-in-hand logic.
    /// </summary>
    [DataField]
    public bool SwitchOnUseInHand;

    /// <summary>
    /// Sound played on every switch if the stance doesn't define its own.
    /// </summary>
    [DataField]
    public SoundSpecifier? SwitchSound;

    /// <summary>
    /// Show a popup with the stance name to the user on switch.
    /// </summary>
    [DataField]
    public bool ShowPopup = true;

    /// <summary>
    /// Prevents attacking for this long after a switch (adds to the melee cooldown).
    /// </summary>
    [DataField]
    public TimeSpan AttackDelayOnSwitch = TimeSpan.Zero;

    /// <summary>
    /// Sprite layer map key that <see cref="MeleeStanceMode.State"/> and the transition animation are applied to.
    /// If the sprite doesn't have a layer with this key, layer 0 is used.
    /// </summary>
    [DataField]
    public string SpriteLayer = "stance";

    /// <summary>
    /// Stance whose stats are currently applied on this side (client or server). Not networked.
    /// </summary>
    [ViewVariables]
    public int? AppliedMode;
}
