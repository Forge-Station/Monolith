using System.Linq;
using Content.Server._WF.TradeGoods.Components;
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
    [Dependency] private WFTradeSupplySystem _supply = default!;
    [Dependency] private WFTradeEconomySystem _economy = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;

    private static readonly string[] Subcommands = { "status", "ledger", "shortage", "rush", "consignment", "clear" };

    public override string Command => "wf_trade";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        switch (args.Length > 0 ? args[0] : string.Empty)
        {
            case "status" when args.Length == 1:
                Status(shell);
                break;
            case "ledger" when args.Length is 1 or 2:
                Ledger(shell, args.Length == 2 ? args[1] : null);
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
                ("goods", shortage.Name),
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

        foreach (var report in _supply.Reports())
        {
            shell.WriteLine(Loc.GetString("cmd-wf_trade-status-supply",
                ("station", report.Station),
                ("goods", report.Goods),
                ("soldOut", report.SoldOut),
                ("lowest", report.Lowest),
                ("stops", report.Stops),
                ("blockaded", report.Blockaded),
                ("short", report.Short)));
        }
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
            ("goods", shortage.Name)));
    }

    /// <summary>wf_trade ledger [station]: the sector's totals, or every row of the stations whose name contains the text.</summary>
    private void Ledger(IConsoleShell shell, string? filter)
    {
        if (filter == null)
        {
            foreach (var good in _economy.Sector().OrderBy(good => good.Good))
            {
                shell.WriteLine(Loc.GetString("cmd-wf_trade-ledger-good", ("good", _market.GoodName(good.Good)), ("stock", good.Stock),
                    ("owed", good.Owed), ("made", good.Made), ("used", good.Used), ("short", good.Short.Count)));
            }

            return;
        }

        foreach (var (grid, role) in _economy.Stations())
        {
            if (!Name(grid).Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            shell.WriteLine(Loc.GetString("cmd-wf_trade-ledger-station", ("station", Name(grid)), ("role", role.ID)));
            if (!EntityManager.TryGetComponent<WFTradeMarketComponent>(grid, out var market))
                continue;

            foreach (var (good, row) in market.Ledger.OrderBy(entry => entry.Key))
            {
                shell.WriteLine(Loc.GetString("cmd-wf_trade-ledger-row", ("good", _market.GoodName(good)), ("stock", (int) row.Stock),
                    ("level", WFTradeMarketSystem.Shown(_market.Level(grid, good))), ("priced", WFTradeMarketSystem.Shown(row.Priced)),
                    ("owed", (int) row.Inbound), ("made", (int) row.Made), ("used", (int) row.Used)));
            }
        }
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
