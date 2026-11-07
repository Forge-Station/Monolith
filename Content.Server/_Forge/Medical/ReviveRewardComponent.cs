using System.Numerics;
using Robust.Shared.Prototypes;
using Content.Shared.Tag;

namespace Content.Server._Forge.ReviveReward;

[RegisterComponent]
public sealed partial class ReviveRewardComponent : Component
{
    /// <summary>
    /// Flat reward amount
    /// </summary>
    [DataField]
    public int Reward = 500;

    /// <summary>
    /// Additional reward per tile of distance from map center
    /// </summary>
    [DataField]
    public float PerTileBonus = 0.5f;

    /// <summary>
    /// Cooldown after a successful reward payout before this tracker can pay again
    /// </summary>
    [DataField]
    public TimeSpan RewardCooldown = TimeSpan.FromMinutes(30);

    [DataField(customTypeSerializer: typeof(ProtoId<TagPrototype>))]
    public string RewardTag = "TTIEquip";

    public bool Death;
    public Vector2 DeathPos;
    public TimeSpan NextRewardAvailable;
}
