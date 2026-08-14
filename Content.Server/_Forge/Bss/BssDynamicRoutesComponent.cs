using Content.Shared._Forge.Bss;

namespace Content.Server._Forge.Bss;

/// <summary>
/// Runtime BSS links opened by void disks or events. Stored on the primary map so they persist.
/// </summary>
[RegisterComponent]
public sealed partial class BssDynamicRoutesComponent : Component
{
    [DataField]
    public List<BssGateLinkDefinition> Links = [];
}
