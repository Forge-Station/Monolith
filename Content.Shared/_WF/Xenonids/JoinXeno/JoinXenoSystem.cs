using Content.Shared._WF.Xenonids.Evolution;
using Content.Shared._WF.RMCCore.Dialog;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared.Actions;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Xenonids.JoinXeno;

public sealed partial class JoinXenoSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private DialogSystem _dialog = default!;
    [Dependency] private SharedXenoHiveSystem _hive = default!;
    [Dependency] private LarvaQueueSystem _larvaQueue = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedGameTicker _gameTicker = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private TimeSpan _burrowedLarvaDeathTime;
    private TimeSpan _burrowedLarvaDeathIgnoreTime;

    public override void Initialize()
    {
        SubscribeLocalEvent<JoinXenoComponent, MapInitEvent>(OnJoinXenoMapInit);
        SubscribeLocalEvent<JoinXenoComponent, JoinXenoActionEvent>(OnJoinXenoAction);
        SubscribeLocalEvent<JoinXenoComponent, JoinXenoBurrowedLarvaEvent>(OnJoinXenoBurrowedLarva);
        SubscribeLocalEvent<HiveComponent, ComponentStartup>(OnHiveStartup); // WOLFGATE: the action only exists while a hive does
        SubscribeLocalEvent<HiveComponent, ComponentShutdown>(OnHiveShutdown);

        Subs.CVar(_config, WFGrowthCVars.RMCLateJoinsBurrowedLarvaDeathTime, v => _burrowedLarvaDeathTime = TimeSpan.FromMinutes(v), true);
        Subs.CVar(_config, WFGrowthCVars.RMCLateJoinsBurrowedLarvaDeathTimeIgnoreBeforeMinutes, v => _burrowedLarvaDeathIgnoreTime = TimeSpan.FromMinutes(v), true);
    }

    private void OnJoinXenoMapInit(Entity<JoinXenoComponent> ent, ref MapInitEvent args)
    {
        RefreshAction(ent, AnyHive());
    }

    // WOLFGATE: Wolfgate has no xeno round rule, so ghosts only see the join action once a hive exists to queue for.
    private void OnHiveStartup(Entity<HiveComponent> ent, ref ComponentStartup args)
    {
        RefreshAllActions(true);
    }

    private void OnHiveShutdown(Entity<HiveComponent> ent, ref ComponentShutdown args)
    {
        RefreshAllActions(AnyHive(ent.Owner));
    }

    private void RefreshAllActions(bool available)
    {
        var ghosts = EntityQueryEnumerator<JoinXenoComponent>();
        while (ghosts.MoveNext(out var uid, out var join))
        {
            RefreshAction((uid, join), available);
        }
    }

    private bool AnyHive(EntityUid? except = null)
    {
        var hives = EntityQueryEnumerator<HiveComponent>();
        while (hives.MoveNext(out var uid, out _))
        {
            if (uid != except)
                return true;
        }

        return false;
    }

    private void RefreshAction(Entity<JoinXenoComponent> ent, bool available)
    {
        if (_net.IsClient)
            return;

        if (available)
        {
            if (ent.Comp.Action is { } current && Exists(current))
                return;

            _actions.AddAction(ent, ref ent.Comp.Action, ent.Comp.ActionId);
        }
        else if (ent.Comp.Action != null)
        {
            _actions.RemoveAction(ent.Owner, ent.Comp.Action);
            ent.Comp.Action = null;
        }
    }

    private void OnJoinXenoAction(Entity<JoinXenoComponent> ent, ref JoinXenoActionEvent args)
    {
        if (_net.IsClient)
            return;

        var denyQueuing = _config.GetCVar(WFGrowthCVars.RMCLarvaQueueRoundstartDelaySeconds);
        if (_gameTicker.RoundDuration().TotalSeconds <= denyQueuing)
        {
            _popup.PopupEntity($"Joining a hive while observing is disabled for the first {denyQueuing} seconds of the round.", args.Performer, args.Performer);
            return;
        }

        TryComp<ActorComponent>(ent, out var actor);

        var options = new List<DialogOption>();
        var hives = EntityQueryEnumerator<HiveComponent>();
        while (hives.MoveNext(out var hiveId, out var hive))
        {
            var inQueue = actor != null && _larvaQueue.IsAlreadyQueued(actor.PlayerSession.UserId, hiveId);
            string optionText;
            if (inQueue && actor != null)
            {
                var pos = _larvaQueue.GetQueuePosition(actor.PlayerSession.UserId, hiveId);
                if (pos > 0)
                {
                    optionText = $"Leave Larva Queue for ({Name(hiveId)})\n[Position: {pos}]";
                }
                else
                {
                    var remaining = _larvaQueue.GetPreQueueTimeRemaining(actor.PlayerSession.UserId, hiveId);
                    optionText = remaining.HasValue
                        ? $"Leave Larva Queue for ({Name(hiveId)})\n[Waiting: {remaining.Value.TotalSeconds:F0}s remaining]"
                        : $"Leave Larva Queue for ({Name(hiveId)})";
                }
            }
            else
            {
                optionText = $"Join Larva Queue for ({Name(hiveId)})";
            }

            options.Add(new DialogOption(optionText, new JoinLarvaQueueEvent(GetNetEntity(hiveId))));
        }

        _dialog.OpenOptions(ent, "Join as Xeno", options, "Available Hives");
    }

    public bool CanJoinXeno(EntityUid user)
    {
        if (!TryComp<GhostComponent>(user, out var ghostComp))
            return false;

        if (HasComp<JoinXenoCooldownIgnoreComponent>(user))
            return true;

        // If the game has been going on longer than the death ignore time, then check how long since the ghost has died
        if (_gameTicker.RoundDuration() > _burrowedLarvaDeathIgnoreTime)
        {
            var timeSinceDeath = _timing.CurTime.Subtract(ghostComp.TimeOfDeath);

            if (timeSinceDeath < _burrowedLarvaDeathTime)
            {
                var msg = Loc.GetString("rmc-xeno-ui-burrowed-need-time", ("seconds", _burrowedLarvaDeathTime.TotalSeconds - (int)timeSinceDeath.TotalSeconds));
                _popup.PopupEntity(msg, user, user, PopupType.MediumCaution);
                return false;
            }
        }

        return true;
    }

    private void OnJoinXenoBurrowedLarva(Entity<JoinXenoComponent> ent, ref JoinXenoBurrowedLarvaEvent args)
    {
        if (!TryGetEntity(args.Hive, out var hive) ||
            !TryComp(hive, out HiveComponent? hiveComp) ||
            !TryComp(ent, out ActorComponent? actor))
        {
            return;
        }

        _hive.JoinBurrowedLarva((hive.Value, hiveComp), actor.PlayerSession);
    }
}
