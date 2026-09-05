using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Forge.BlackMarket.Components;

[RegisterComponent]
public sealed partial class BlackMarketConsoleComponent : Component
{
    /// <summary>
    /// Number of slots per category, e.g. Weapons: 2, Technology: 2.
    /// </summary>
    [DataField]
    public Dictionary<string, int> CategorySlots = new();

    /// <summary>
    /// Crate prototype spawned by the linked cargo lift.
    /// </summary>
    [DataField]
    public EntProtoId CratePrototype = "CrateSyndicate";

    /// <summary>
    /// Max tile distance to search for an unoccupied black market cargo lift on the same grid.
    /// </summary>
    [DataField]
    public int MaxCrateMachineDistance = 8;

    [ViewVariables]
    public List<BlackMarketSlotData> Slots = new();

    /// <summary>
    /// Per-contract purchase counts on this console during the current round.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, int> ContractPurchaseCounts = new();

    /// <summary>
    /// Recently rolled contract ids per category (most recent first).
    /// </summary>
    [ViewVariables]
    public Dictionary<string, List<string>> RecentContractsByCategory = new();

    [DataField]
    public SoundSpecifier PurchaseSound = new SoundPathSpecifier("/Audio/Effects/Lightning/lightningbolt.ogg");

    [DataField]
    public SoundSpecifier DenySound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_two.ogg");
}

[DataDefinition]
public sealed partial class BlackMarketSlotData
{
    [DataField]
    public string CategoryId = string.Empty;

    [DataField]
    public string? ContractId;

    [DataField]
    public BlackMarketSlotMode Mode = BlackMarketSlotMode.Available;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextEventTime = TimeSpan.Zero;
}

public enum BlackMarketSlotMode : byte
{
    Available,
    PurchasedCooldown,
}
