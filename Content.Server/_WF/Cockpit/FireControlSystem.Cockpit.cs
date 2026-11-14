using System.Linq;
using Content.Server._WF.Cockpit;
using Content.Server._WF.CombatConsole;
using Content.Shared._Mono.FireControl;
using Content.Shared._WF.CombatConsole;

namespace Content.Server._Mono.FireControl;

public sealed partial class FireControlSystem
{
    /// <summary>Runs native server discovery after the cockpit has authorized physical access to an unopened console.</summary>
    internal void WfCockpitDiscoverConsole(EntityUid console, FireControlConsoleComponent control)
    {
        var settings = EnsureComp<WFCombatConsoleComponent>(console);
        if (settings.NextDiscovery > _timing.CurTime)
            return;
        settings.NextDiscovery = _timing.CurTime + TimeSpan.FromSeconds(1);
        DoRefreshServer(console, control);
    }

    /// <summary>Builds the existing gunnery snapshot for an authorized cockpit link.</summary>
    public FireControlConsoleBoundInterfaceState? WfCockpitState(EntityUid console, bool refresh = true)
    {
        if (refresh)
            WfRefreshConsole(console, periodic: true);
        return _ui.TryGetUiState<FireControlConsoleBoundInterfaceState>(console, FireControlConsoleUiKey.Key, out var state)
            ? state : null;
    }

    /// <summary>Retains native firing, logging and guided-cursor handling after cockpit authorization.</summary>
    public bool WfCockpitCommand(EntityUid uid, EntityUid actor, BoundUserInterfaceMessage command)
    {
        if (!EntityManager.System<WFCockpitGunnerySystem>().CanOperate(actor, uid) ||
            !TryComp<FireControlConsoleComponent>(uid, out var console) ||
            !EntityManager.System<WFCombatConsoleSystem>().TryGetServer(uid, console, out var serverUid, out var server))
            return false;
        if (command is FireControlConsoleFireMessage fire)
        {
            if (fire.Selected == null || fire.Selected.Count > WFWeaponGroups.MaximumWeapons ||
                !float.IsFinite(fire.Coordinates.X) || !float.IsFinite(fire.Coordinates.Y) ||
                !TryGetEntity(fire.Coordinates.NetEntity, out var target) || TerminatingOrDeleted(target) ||
                !TryComp<TransformComponent>(target, out var targetTransform) || targetTransform.MapID != Transform(uid).MapID)
                return false;
            // Cursor guidance has no ammunition or radar changes to publish.
            if (fire.Selected.Count == 0)
            {
                RaiseLocalEvent(uid, new FireControlConsoleFireEvent(fire.Coordinates, fire.Selected));
                return true;
            }
            var requested = fire.Selected.ToHashSet();
            var selected = server.Controlled.Where(weapon => !TerminatingOrDeleted(weapon) &&
                requested.Contains(GetNetEntity(weapon)) && Transform(weapon).GridUid == server.ConnectedGrid &&
                !HasComp<WFFlareLauncherComponent>(weapon) &&
                TryComp<FireControllableComponent>(weapon, out var control) && control.ControllingServer == serverUid)
                .Select(weapon => GetNetEntity(weapon)).ToList();
            var forwarded = new FireControlConsoleFireMessage(selected, fire.Coordinates)
            {
                Actor = actor,
                Entity = GetNetEntity(uid),
                UiKey = FireControlConsoleUiKey.Key,
            };
            OnFire(uid, console, forwarded);
            return true;
        }
        if (command is FireControlConsoleRefreshServerMessage)
        {
            DoRefreshServer(uid, console);
            return true;
        }
        return EntityManager.System<WFCombatConsoleSystem>().WfCockpitCommand(uid, console, actor, command);
    }
}
