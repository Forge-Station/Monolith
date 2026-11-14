using Content.Shared.Chat.Prototypes;
using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.Rend;

[RegisterComponent, NetworkedComponent]
public sealed partial class XenoRendComponent : Component
{
    [DataField]
    public DamageSpecifier Damage = new();

    [DataField]
    public float Range = 1.5f;

    [DataField]
    public EntProtoId Effect = "WFEffectRendSlash";

    [DataField]
    public SoundSpecifier Sound = new SoundCollectionSpecifier("AlienClaw");

    [DataField]
    public ProtoId<EmotePrototype> HissEmote = "Hiss";
}
