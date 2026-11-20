using Content.Server._WF.Encounters.Components;
using Content.Server.Popups;
using Content.Shared.Database;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Server.Administration.Logs;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// Encounter ship deeds: the highest in command of an encounter ship drops its deed when he dies, and whoever uses it
/// makes the ship permanent, cut loose from its encounter.
/// </summary>
public sealed partial class WFEncounterDeedSystem : EntitySystem
{
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFEncounterDeedDropComponent, MobStateChangedEvent>(OnCarrierState);
        SubscribeLocalEvent<WFEncounterDeedComponent, UseInHandEvent>(OnUse);
    }

    private void OnCarrierState(Entity<WFEncounterDeedDropComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        var ship = ent.Comp.Ship;
        var deedId = ent.Comp.Deed;
        RemComp<WFEncounterDeedDropComponent>(ent);
        if (TerminatingOrDeleted(ship) || !HasComp<WFEncounterGridComponent>(ship))
            return;

        var deed = Spawn(deedId, Transform(ent).Coordinates);
        EnsureComp<WFEncounterDeedComponent>(deed).Ship = ship;
        _meta.SetEntityName(deed, Loc.GetString("wf-encounter-deed-name", ("ship", Name(ship))));
    }

    private void OnUse(Entity<WFEncounterDeedComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var ship = ent.Comp.Ship;
        if (TerminatingOrDeleted(ship))
        {
            _popup.PopupEntity(Loc.GetString("wf-encounter-deed-gone"), args.User, args.User);
            return;
        }

        if (!_encounters.Free(ship))
        {
            _popup.PopupEntity(Loc.GetString("wf-encounter-deed-already", ("ship", Name(ship))), args.User, args.User);
            return;
        }

        _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(args.User):player} claimed encounter ship {ToPrettyString(ship):grid} with its deed");
        _popup.PopupEntity(Loc.GetString("wf-encounter-deed-done", ("ship", Name(ship))), args.User, args.User);
        QueueDel(ent);
    }
}
