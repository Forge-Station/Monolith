using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Content.Server.Disposal.Tube;
using Content.Shared.Stacks;

namespace Content.Server._Forge.OrePipe;

/// <summary>
/// Three-way disposal junction that routes by ore stack type.
/// Selected (filtered) ores are diverted to the side; everything else continues straight.
/// Empty filter = all ores pass straight.
/// </summary>
[RegisterComponent]
[Access(typeof(OreDisposalFilterSystem))]
public sealed partial class OreDisposalFilterComponent : DisposalJunctionComponent
{
    /// <summary>
    /// Ore stack types diverted to the side. Empty means nothing is filtered (all go straight).
    /// </summary>
    [DataField]
    public HashSet<ProtoId<StackPrototype>> Filtered = new();

    [DataField]
    public SoundSpecifier ClickSound = new SoundPathSpecifier("/Audio/Machines/machine_switch.ogg");
}
