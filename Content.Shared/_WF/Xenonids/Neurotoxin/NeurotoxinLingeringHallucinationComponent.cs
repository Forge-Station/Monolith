using Content.Shared.Chat.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.Neurotoxin;

[RegisterComponent, NetworkedComponent]
public sealed partial class NeurotoxinLingeringHallucinationComponent : Component
{
    /// <summary>
    /// Stores Haullucination type, stage, next trigger, and position if nessassary
    /// </summary>
    [DataField]
    public List<(NeuroHallucinations, int, TimeSpan, EntityCoordinates?)> Hallucinations = new();

    [DataField]
    public SoundSpecifier BoneBreak = new SoundPathSpecifier("/Audio/_WF/Xenonids/Weapons/alien_knockdown.ogg"); //TODO RMC14 Bonebreak sound

    [DataField]
    public SoundSpecifier XenoClaw = new SoundCollectionSpecifier("AlienClaw");

    [DataField]
    public SoundSpecifier OBTravel = new SoundPathSpecifier("/Audio/_WF/Xenonids/Weapons/gun_orbital_travel.ogg");

    [DataField]
    public SoundSpecifier MortarTravel = new SoundPathSpecifier("/Audio/_WF/Xenonids/Weapons/gun_mortar_travel.ogg");

    [DataField]
    public SoundSpecifier GauFire = new SoundPathSpecifier("/Audio/_WF/Xenonids/Dropship/gau.ogg");

    [DataField]
    public SoundSpecifier RocketFire = new SoundPathSpecifier("/Audio/_WF/Xenonids/Effects/rocketpod_fire.ogg");

    [DataField]
    public SoundSpecifier GauHit = new SoundPathSpecifier("/Audio/_WF/Xenonids/Dropship/gauimpact.ogg", AudioParams.Default.WithVolume(-5));

    [DataField]
    public SoundSpecifier Explosion = new SoundCollectionSpecifier("CMExplosion");

    [DataField]
    public SoundSpecifier BigExplosion = new SoundCollectionSpecifier("RMCExplosionBig");

    [DataField]
    public ProtoId<EmotePrototype> PainEmote = "Scream";
}
