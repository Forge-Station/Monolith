using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared.Damage;

namespace Content.Client._WF.Wolfmed.Damage;

/// <summary>
/// Keeps the client from writing predicted damage onto a wound host's own <c>DamageableComponent</c>.
/// </summary>
/// <remarks>
/// P6. Wound routing is server-only, so a predicted hit on a wound host used to take the whole-body path on
/// the client: the write loop added the full post-armour figure to the mob, and one state later the server's
/// projected total (the sum of what the struck part actually kept, after the part profile and the locational
/// armour) replaced it. Health bar, damage overlay, analyzer readout and the predicted crit threshold all
/// jumped on every hit. Suppressing the write leaves the mob's damage purely server state, which converges
/// quietly.
///
/// The returned specifier is deliberately left intact, so <c>SharedMeleeWeaponSystem</c> still sees a hit:
/// the attacker's red flash is predicted locally and the server's <c>DoDamageEffect</c> filter excludes them,
/// so clearing it here would mean the attacker never saw a hit land at all.
///
/// Subscribed on <c>DamageableComponent</c> rather than <c>WoundHostComponent</c> because the routing system
/// is shared and already owns that pair on both sides; the seam itself only fires for wound hosts.
/// </remarks>
public sealed class WolfmedPredictedDamageSystem : EntitySystem
{
    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DamageableComponent, DamageDealtEvent>(OnDamageDealt);
    }

    private void OnDamageDealt(Entity<DamageableComponent> ent, ref DamageDealtEvent args)
    {
        args.Suppressed = true;
    }
}
