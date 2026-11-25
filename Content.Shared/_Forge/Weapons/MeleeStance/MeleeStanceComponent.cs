using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Weapons.MeleeStance;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), Access(typeof(MeleeStanceSystem))]
public sealed partial class MeleeStanceComponent : Component
{
    [DataField(required: true)]
    public List<MeleeStanceMode> Modes = new();

    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public int CurrentMode;

    /// <summary>
    /// Sound played on every switch if the stance doesn't define its own.
    /// </summary>
    [DataField]
    public SoundSpecifier? SwitchSound;

    [DataField]
    public bool ShowPopup = true;

    [DataField]
    public TimeSpan AttackDelayOnSwitch = TimeSpan.Zero;

    [DataField]
    public string SpriteLayer = "stance";

    [ViewVariables]
    public int? AppliedMode;


    [DataField]
    public bool SwitchOnUseInHand = true;
}