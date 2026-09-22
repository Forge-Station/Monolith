using Robust.Shared.Audio;
using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Prototypes;

namespace Content.Shared.NukeOps;

/// <summary>
/// This is used for tagging a mob as a nuke operative.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NukeOperativeComponent : Component
{

    /// <summary>
    ///
    /// </summary>
    [DataField("syndStatusIcon", customTypeSerializer: typeof(ProtoId<FactionIconPrototype>))]
    public string SyndStatusIcon = "SyndicateFaction";
}
