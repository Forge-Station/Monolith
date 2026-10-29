using System.Linq;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.Administration;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.TradeGoods.Commands;

/// <summary>Trade events from the console: show them, start a shortage or rush order, post a consignment, clear them.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WFTradeCommand : LocalizedEntityCommands
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WFTradeShortageSystem _shortages = default!;
    [Dependency] private WFTradeRushSystem _rushes = default!;
    [Dependency] private WFTradeSpecialCrateSystem _special = default!;

    private static readonly string[] Subcommands = { "status", "shortage", "rush", "consignment", "clear" };

    public override string Command => "wf_trade";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        switch (args.Length > 0 ? args[0] : string.Empty)
        {
            case "status" when args.Length == 1:
                Status(shell);
                break;
            case "shortage" when args.Length is 1 or 2:
                Shortage(shell, args.Length == 2 ? args[1] : null);
                break;
            case "rush" when args.Length is 1 or 2:
                Rush(shell, args.Length == 2 ? args[1] : null);
                break;
            case "consignment" when args.Length == 1:
                if (_special.SpawnConsignment() is { } crate)
                    shell.WriteLine(Loc.GetString("cmd-wf_trade-consignment", ("uid", EntityManager.GetNetEntity(crate))));
                else
                    shell.WriteLine(Loc.GetString("cmd-wf_trade-consignment-failed"));
                break;
            case "clear" when args.Length == 1:
                shell.WriteLine(Loc.GetString("cmd-wf_trade-cleared", ("shortages", _shortages.Clear()), ("rushes", _rushes.Clear())));
                break;
            default:
                shell.WriteLine(Help);
                break;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(Subcommands, Loc.GetString("cmd-wf_trade-hint-sub")),
            2 when args[0] == "shortage" => CompletionResult.FromHintOptions(
                _prototypes.EnumeratePrototypes<WFTradeShortagePrototype>().Select(proto => proto.ID).Order(),
                Loc.GetString("cmd-wf_trade-hint-shortage")),
            2 when args[0] == "rush" => CompletionResult.FromHintOptions(
                _prototypes.EnumeratePrototypes<WFTradeRushPrototype>().Select(proto => proto.ID).Order(),
                Loc.GetString("cmd-wf_trade-hint-rush")),
            _ => CompletionResult.Empty,
        };
    }

    private void Status(IConsoleShell shell)
    {
        var now = _timing.CurTime;
        var lines = 0;
        foreach (var shortage in _shortages.Active)
        {
            lines++;
            shell.WriteLine(Loc.GetString("cmd-wf_trade-status-shortage",
                ("station", shortage.Grid is { } grid ? Name(grid) : Loc.GetString("cmd-wf_trade-status-sector")),
                ("goods", Loc.GetString(shortage.Name)),
                ("factor", WFTradeMarketSystem.Shown(shortage.Factor)),
                ("seconds", Seconds(shortage.Ends, now))));
        }

        foreach (var rush in _rushes.Active)
        {
            lines++;
            shell.WriteLine(Loc.GetString("cmd-wf_trade-status-rush",
                ("station", Name(rush.Grid)),
                ("goods", Loc.GetString(rush.Goods)),
                ("left", rush.Remaining),
                ("count", rush.Total),
                ("factor", WFTradeMarketSystem.Shown(rush.Factor)),
                ("seconds", Seconds(rush.Ends, now))));
        }

        foreach (var (destination, _, carrier, deadline, _) in _special.Consignments())
        {
            lines++;
            shell.WriteLine(Loc.GetString("cmd-wf_trade-status-consignment",
                ("station", Name(destination)),
                ("ship", carrier ?? "-"),
                ("seconds", Seconds(deadline, now))));
        }

        if (lines == 0)
            shell.WriteLine(Loc.GetString("cmd-wf_trade-status-empty"));
    }

    /// <summary>wf_trade shortage [prototype]: a random shortage or boom, or the given one.</summary>
    private void Shortage(IConsoleShell shell, string? id)
    {
        if (id != null && !_prototypes.HasIndex<WFTradeShortagePrototype>(id))
        {
            shell.WriteLine(Loc.GetString("cmd-wf_trade-bad-shortage", ("arg", id)));
            return;
        }

        ProtoId<WFTradeShortagePrototype>? forced = null;
        if (id != null)
            forced = id;

        if (_shortages.Start(forced) is not { } shortage)
        {
            shell.WriteLine(Loc.GetString("cmd-wf_trade-shortage-failed"));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wf_trade-shortage",
            ("station", shortage.Grid is { } grid ? Name(grid) : Loc.GetString("cmd-wf_trade-status-sector")),
            ("goods", Loc.GetString(shortage.Name))));
    }

    /// <summary>wf_trade rush [prototype]: a random rush order, or the given one, even past the usual limit.</summary>
    private void Rush(IConsoleShell shell, string? id)
    {
        if (id != null && !_prototypes.HasIndex<WFTradeRushPrototype>(id))
        {
            shell.WriteLine(Loc.GetString("cmd-wf_trade-bad-rush", ("arg", id)));
            return;
        }

        ProtoId<WFTradeRushPrototype>? forced = null;
        if (id != null)
            forced = id;

        if (_rushes.Start(forced) is not { } rush)
        {
            shell.WriteLine(Loc.GetString("cmd-wf_trade-rush-failed"));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wf_trade-rush", ("station", Name(rush.Grid)), ("goods", Loc.GetString(rush.Goods))));
    }

    private string Name(EntityUid uid)
    {
        return EntityManager.GetComponent<MetaDataComponent>(uid).EntityName;
    }

    /// <summary>Seconds until a time, never negative.</summary>
    private static int Seconds(TimeSpan at, TimeSpan now)
    {
        return Math.Max(0, (int) (at - now).TotalSeconds);
    }
}
