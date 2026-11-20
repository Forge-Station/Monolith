using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Construction.DeployedTraps;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoDeployedTrapsComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan StunDuration = TimeSpan.FromSeconds(1.75);

    [DataField, AutoNetworkedField]
    public EntityUid? PlacedBy;

    [DataField, AutoNetworkedField]
    public SoundSpecifier CatchSound = new SoundPathSpecifier("/Audio/_WF/Xenonids/Xeno/alien_claw_block.ogg");

    [DataField, AutoNetworkedField]
    public HashSet<EntityUid> ContactsAtStart = new();

    public bool SoundPlayed = false;
}
