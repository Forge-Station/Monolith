using Content.Client.ContextMenu.UI;
using Content.Client.Verbs.UI;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Ghost;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Ghost;
using Robust.Client.Player;
using Robust.Client.Timing;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;

namespace Content.Client._WF.NpcCrew;

/// <summary>Opt-in request, delivery and attachment traces for missing admin menu contents.</summary>
public sealed partial class WFCrewUiDiagnosticsSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IClientGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    private readonly Dictionary<string, TimeSpan> _pending = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<GhostOrbitTargetsEvent>(ev => Reply("ghost", ev.Targets.Count));
        SubscribeNetworkEvent<WFCrewSetupResponse>(ev => Reply("crew", ev.Crews.Count));
    }

    /// <summary>Records a request immediately before it enters the normal network path.</summary>
    public void Request(string kind, NetEntity? target = null)
    {
        if (!_cfg.GetCVar(NpcCrewCVars.UiDiagnostics))
            return;

        _pending.TryAdd(kind, _timing.RealTime);
        Log.Info($"UI send kind={kind} target={target} {State()}");
    }

    /// <summary>Records a received reply, including whether the active verb menu accepted its contents.</summary>
    public void Reply(string kind, int count, NetEntity? target = null)
    {
        if (!_cfg.GetCVar(NpcCrewCVars.UiDiagnostics))
            return;

        var elapsed = _pending.Remove(kind, out var since) ? (_timing.RealTime - since).TotalSeconds : -1;
        Log.Info($"UI reply kind={kind} target={target} count={count} elapsed={elapsed:F3}s {State()}");
    }

    /// <summary>Records that a ghost window's subscribed callback received the target list.</summary>
    public void DisplayGhost(int count, bool open)
    {
        if (_cfg.GetCVar(NpcCrewCVars.UiDiagnostics))
            Log.Info($"UI display kind=ghost count={count} open={open} {State()}");
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        if (!_cfg.GetCVar(NpcCrewCVars.UiDiagnostics))
        {
            _pending.Clear();
            return;
        }

        foreach (var (kind, since) in _pending)
        {
            if (_timing.RealTime - since < TimeSpan.FromSeconds(5))
                continue;

            Log.Warning($"UI waiting kind={kind} elapsed={(_timing.RealTime - since).TotalSeconds:F3}s {State()}");
            _pending.Remove(kind);
            break;
        }
    }

    private string State()
    {
        var user = _players.LocalEntity;
        var menu = _ui.GetUIController<VerbMenuUIController>();
        var context = _ui.GetUIController<ContextMenuUIController>();
        return $"tick={_timing.CurTick} serverTick={_timing.LastRealTick} local={NetId(user)} " +
               $"session={NetId(_players.LocalSession?.AttachedEntity)} ghost={HasComp<GhostComponent>(user)} " +
               $"menuTarget={menu.CurrentTarget} menuOpen={menu.OpenMenu?.Visible} verbs={menu.CurrentVerbs.Count} " +
               $"contextOpen={context.RootMenu?.Visible} modals={_ui.ModalRoot.ChildCount}";
    }

    private string NetId(EntityUid? uid) => TryComp<MetaDataComponent>(uid, out var meta) ? meta.NetEntity.ToString() : $"missing:{uid}";
}
