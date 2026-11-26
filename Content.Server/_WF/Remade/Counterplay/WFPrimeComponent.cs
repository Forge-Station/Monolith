using Content.Shared.Damage.Prototypes;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Remade.Counterplay;

/// <summary>
/// A Monolith that has rooted at the Crescendo. It is armoured against everything but blasts and heavy cutting, mends itself when
/// left alone, sings again every so often until it is stopped, and carries a sector bounty for whoever brings it down.
/// </summary>
[RegisterComponent]
public sealed partial class WFPrimeComponent : Component
{
    /// <summary>The armour it wears once rooted.</summary>
    [DataField]
    public ProtoId<DamageModifierSetPrototype> Armour = "WFPrimeArmour";

    /// <summary>What the bounty pays when it is posted, and what it adds for each minute the Prime has stood.</summary>
    [DataField]
    public int BaseReward = 1_000_000;

    [DataField]
    public int RewardPerMinute = 25_000;

    [DataField]
    public int MaxReward = 2_500_000;

    /// <summary>How long between verses, each of which is announced and hands the Director more biomass.</summary>
    [DataField]
    public TimeSpan VerseEvery = TimeSpan.FromMinutes(10);

    [DataField]
    public float VerseBiomass = 400f;

    /// <summary>It mends once nothing has hurt it for this long.</summary>
    [DataField]
    public TimeSpan MendAfter = TimeSpan.FromSeconds(45);

    [DataField]
    public float MendPerSecond = 3f;

    /// <summary>Radius in which the living share the bounty when the blow that killed it can't be traced.</summary>
    [DataField]
    public float PayRange = 200f;

    [ViewVariables]
    public TimeSpan RootedAt;

    [ViewVariables]
    public TimeSpan NextVerse;

    [ViewVariables]
    public TimeSpan NextTick;

    [ViewVariables]
    public TimeSpan LastHit;

    [ViewVariables]
    public int Verses;

    /// <summary>What the bounty pays now.</summary>
    [ViewVariables]
    public int Reward;

    /// <summary>The bounty's id on the board, if the board was up.</summary>
    [ViewVariables]
    public uint? Contract;

    /// <summary>Damage each player has dealt it.</summary>
    [ViewVariables]
    public Dictionary<NetUserId, float> Hits = new();
}
