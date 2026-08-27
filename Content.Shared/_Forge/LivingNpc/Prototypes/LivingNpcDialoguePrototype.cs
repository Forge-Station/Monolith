using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.LivingNpc.Prototypes;

/// <summary>
/// Loc-id catalogs the NPC picks from when speaking.
/// </summary>
[Prototype]
public sealed partial class LivingNpcDialoguePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public List<string> Greetings { get; private set; } = new();

    [DataField]
    public List<string> Farewells { get; private set; } = new();

    [DataField]
    public List<string> Idle { get; private set; } = new();

    [DataField]
    public List<string> Work { get; private set; } = new();

    [DataField]
    public List<string> Hunger { get; private set; } = new();

    [DataField]
    public List<string> Fear { get; private set; } = new();

    [DataField]
    public List<string> Anger { get; private set; } = new();

    [DataField]
    public List<string> Thanks { get; private set; } = new();

    [DataField]
    public List<string> Insults { get; private set; } = new();

    [DataField]
    public List<string> Help { get; private set; } = new();

    [DataField]
    public List<string> SmallTalk { get; private set; } = new();

    [DataField]
    public List<string> HowAreYou { get; private set; } = new();

    [DataField]
    public List<string> NpcChat { get; private set; } = new();
}
