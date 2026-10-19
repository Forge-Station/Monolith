using Robust.Shared.GameStates;
using Robust.Shared.Maths;

namespace Content.Shared._WF.Xenonids.Acid;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoAcidHoleSystem))]
public sealed partial class XenoAcidHoleComponent : Component
{
    [DataField]
    public TimeSpan CrawlDelay = TimeSpan.FromSeconds(1.5);


    [DataField]
    public TimeSpan BreakDelay = TimeSpan.FromSeconds(10);


    [AutoNetworkedField]
    public EntityUid? Wall;

    [AutoNetworkedField]
    public Direction EntranceDirection = Direction.South;
}
