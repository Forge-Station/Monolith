using Content.Shared._Onyx.Body; // OrganFunctionChangedEvent is declared here, in OrganHealthSystem.cs.
using Content.Shared.Body.Organ; // OrganComponent - Wolfgate keeps it here, not in _Shitmed.
using Content.Server._WF.Wolfmed.Life;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WF.Wolfmed.Body;

namespace Content.Server._WF.Wolfmed.Body;

/// <summary>Bridges Onyx's organ-functionality event onto Shitmed's organ enable/disable switch.</summary>
/// <remarks>
/// Covers the one-tick window in which an organ sits at 0 HP but has not been destroyed yet;
/// OrganHealthSystem.Update destroys it on the next tick and RemoveOrgan raises the same event again,
/// which is idempotent. Everything Onyx's OrganEffectSystem does on organ *removal* Wolfgate already
/// does through Shitmed (P3-D8), so this is the only consequence glue phase 3 needs.
/// </remarks>
public sealed class WolfmedOrganConsequenceSystem : EntitySystem
{
    [Dependency] private WolfmedShutdownSystem _shutdown = default!; // BRAIN

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        // Pair verified free: OrganFunctionChangedEvent has no other subscriber in Wolfgate
        // (declared and raised only in OrganHealthSystem.cs:21,71).
        SubscribeLocalEvent<WolfmedOrganComponent, OrganFunctionChangedEvent>(OnOrganFunctionChanged);
    }

    private void OnOrganFunctionChanged(Entity<WolfmedOrganComponent> ent, ref OrganFunctionChangedEvent args)
    {
        if (TerminatingOrDeleted(ent) || !HasComp<OrganComponent>(ent))
            return;

        // <OrganComponent, OrganEnableChangedEvent> is owned by SharedBodySystem.Organs.cs:23, so raise it
        // rather than subscribing it (precedent: CyberneticsSystem.cs:27-28,45-46). Its handler turns the
        // event into OrganComponentsModifyEvent, which revokes the organ's OnAdd grants and drives the eyes
        // blindness path.
        var enable = new OrganEnableChangedEvent(args.Functional);
        RaiseLocalEvent(ent, ref enable);

        // BRAIN: a machine body's pump is its heart. This pair is ours, so the shutdown system is told here.
        if (HasComp<HeartComponent>(ent))
            _shutdown.Refresh(args.Body);
    }
}
