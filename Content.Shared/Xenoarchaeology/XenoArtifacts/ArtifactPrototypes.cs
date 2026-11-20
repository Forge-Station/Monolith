using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Shared.Xenoarchaeology.XenoArtifacts;

[Prototype]
public sealed partial class ArtifactTriggerPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public EntityWhitelist? Whitelist;

    [DataField]
    public EntityWhitelist? Blacklist;

    [DataField]
    public ComponentRegistry Components = new();
}

[Prototype]
public sealed partial class ArtifactEffectPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public EntityWhitelist? Whitelist;

    [DataField]
    public EntityWhitelist? Blacklist;

    [DataField]
    public ComponentRegistry Components = new();

    [DataField]
    public ComponentRegistry PermanentComponents = new();
}
