using Content.Shared.Damage;
using Content.Shared.NPC.Prototypes;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Caverns;

/// <summary>A pool that digests whoever wades in, a second at a time, sparing the creatures that live around it.</summary>
[RegisterComponent, Access(typeof(WFDigestiveAcidSystem))]
public sealed partial class WFDigestiveAcidComponent : Component
{
    /// <summary>The damage dealt each second to whoever stands in the pool.</summary>
    [DataField(required: true)]
    public DamageSpecifier Damage = new();

    /// <summary>What the pool can digest; anything when null.</summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>Factions the pool spares.</summary>
    [DataField]
    public HashSet<ProtoId<NpcFactionPrototype>> IgnoreFactions = new();

    /// <summary>Anchored entities on the pool's tile that keep whoever stands on them out of the acid, such as catwalks.</summary>
    [DataField]
    public EntityWhitelist? CoveredBy;

    /// <summary>The loop that hisses on whoever the pool is burning, until it stops.</summary>
    [DataField]
    public SoundSpecifier? BurnSound;
}
