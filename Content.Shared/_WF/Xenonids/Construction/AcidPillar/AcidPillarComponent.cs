using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Xenonids.Construction.AcidPillar;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(AcidPillarSystem))]
public sealed partial class AcidPillarComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan CheckEvery = TimeSpan.FromSeconds(1);

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan NextCheck;

    [DataField, AutoNetworkedField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(1);

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan Next;

    [DataField, AutoNetworkedField]
    public EntProtoId Acid = "XenoAcidSprayWeak";

    [DataField, AutoNetworkedField]
    public TimeSpan AcidSpreadDelay = TimeSpan.FromSeconds(0.3);

    [DataField, AutoNetworkedField]
    public float Range = 5;

    /// <summary>Played when the pillar fires; Wolfgate addition.</summary>
    [DataField, AutoNetworkedField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/_WF/Xenonids/Xeno/alien_spitacid.ogg");

    [DataField, AutoNetworkedField]
    public SpriteSpecifier.Rsi? IdleSprite = new(new ResPath("_WF/Xenonids/Structures/Xenos/xeno_acid_pillar.rsi"), "idle");

    [DataField, AutoNetworkedField]
    public SpriteSpecifier.Rsi? FiringSprite = new(new ResPath("_WF/Xenonids/Structures/Xenos/xeno_acid_pillar.rsi"), "attack");
}
