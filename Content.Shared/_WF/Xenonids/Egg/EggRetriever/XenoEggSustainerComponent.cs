using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Egg.EggRetriever;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoEggSustainerComponent : Component
{
    [DataField]
    public List<EntityUid> SustainedEggs = new();

    [DataField, AutoNetworkedField]
    public int MaxSustainedEggs = 4;

    [DataField, AutoNetworkedField]
    public int SustainedEggsRange = 14;

    [DataField, AutoNetworkedField]
    public TimeSpan SustainedEggMaxTime = TimeSpan.FromMinutes(5);

    [DataField, AutoNetworkedField]
    public SoundSpecifier DeathSound = new SoundPathSpecifier("/Audio/_WF/Xenonids/Xeno/alien_egg_burst.ogg");
}
