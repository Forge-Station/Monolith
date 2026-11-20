using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared._WF.Contracts;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Contracts;

/// <summary>Posts an offer on the contract board from a template, on top of what the contractors keep up.</summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class ContractPostCommand : LocalizedEntityCommands
{
    [Dependency] private WFContractSystem _contracts = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;

    public override string Command => "contractpost";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 || !_proto.HasIndex<WFContractPrototype>(args[0]))
        {
            shell.WriteError(Loc.GetString("cmd-contractpost-invalid"));
            shell.WriteLine(Help);
            return;
        }

        if (_contracts.PostOffer(args[0]) is not { } contract)
        {
            shell.WriteError(Loc.GetString("cmd-contractpost-failed", ("template", args[0])));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-contractpost-done", ("id", contract.Id), ("title", _contracts.Title(contract)),
            ("reward", contract.Reward)));
        _adminLog.Add(LogType.AdminCommands, LogImpact.Low,
            $"{shell.Player?.Name ?? "Server"} posted contract #{contract.Id} from {args[0]}");
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(CompletionHelper.PrototypeIDs<WFContractPrototype>(), Loc.GetString("cmd-contractpost-hint"))
            : CompletionResult.Empty;
    }
}

/// <summary>Lists every offer and live contract on the board, with who holds each.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class ContractListCommand : LocalizedEntityCommands
{
    [Dependency] private WFContractSystem _contracts = default!;

    public override string Command => "contractlist";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var all = _contracts.All.OrderBy(c => c.Status).ThenBy(c => c.Id).ToList();
        if (all.Count == 0)
        {
            shell.WriteLine(Loc.GetString("cmd-contractlist-none"));
            return;
        }

        foreach (var contract in all)
        {
            shell.WriteLine(Loc.GetString("cmd-contractlist-entry", ("id", contract.Id), ("status", contract.Status.ToString()),
                ("title", _contracts.Title(contract)), ("reward", contract.Reward),
                ("holder", contract.Character.Length > 0 ? contract.Character : "-")));
        }
    }
}
