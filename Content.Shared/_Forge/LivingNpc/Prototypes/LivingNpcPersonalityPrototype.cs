using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.LivingNpc.Prototypes;

/// <summary>
/// Named personality preset copied onto a living NPC at spawn.
/// </summary>
[Prototype]
public sealed partial class LivingNpcPersonalityPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public LivingNpcPersonality Traits { get; private set; } = new();
}
