using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Console;

namespace Content.Server._WF.Companies;

/// <summary>Shared lookup and logging for the company admin commands.</summary>
public abstract class WFCompanyCommand : LocalizedEntityCommands
{
    [Dependency] protected WFCompanySystem Companies = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;

    protected bool TryFind(IConsoleShell shell, string query, out int id)
    {
        if (Companies.FindCompany(query) is { } found)
        {
            id = found;
            return true;
        }
        id = 0;
        shell.WriteError(Loc.GetString("cmd-company-missing", ("query", query)));
        return false;
    }

    protected void Log(IConsoleShell shell, string what) =>
        _adminLog.Add(LogType.AdminCommands, LogImpact.Medium, $"{shell.Player?.Name ?? "Server"} {what}");

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args) => args.Length == 1
        ? CompletionResult.FromHintOptions(Companies.ListCompanies().Select(company => new CompletionOption(company.Id.ToString(),
            $"[{company.Tag}] {company.Name}")), Loc.GetString("cmd-company-hint"))
        : CompletionResult.Empty;
}

[AdminCommand(AdminFlags.Moderator)]
public sealed class WFCompanyListCommand : WFCompanyCommand
{
    public override string Command => "companylist";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        foreach (var (id, tag, name) in Companies.ListCompanies())
            shell.WriteLine($"{id}: [{tag}] {name}");
    }
}

[AdminCommand(AdminFlags.Moderator)]
public sealed class WFCompanyInfoCommand : WFCompanyCommand
{
    public override string Command => "companyinfo";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Help);
            return;
        }
        if (TryFind(shell, args[0], out var id))
            shell.WriteLine(Companies.Describe(id));
    }
}

[AdminCommand(AdminFlags.Moderator)]
public sealed class WFCompanyLedgerCommand : WFCompanyCommand
{
    public override string Command => "companyledger";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2 || args.Length == 2 && !int.TryParse(args[1], out _))
        {
            shell.WriteError(Help);
            return;
        }
        if (TryFind(shell, args[0], out var id))
            shell.WriteLine(await Companies.DescribeLedger(id, args.Length == 2 ? Math.Clamp(int.Parse(args[1]), 1, 500) : 25));
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class WFCompanyFreezeCommand : WFCompanyCommand
{
    public override string Command => "companyfreeze";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2 || !bool.TryParse(args[1], out var frozen))
        {
            shell.WriteError(Help);
            return;
        }
        if (!TryFind(shell, args[0], out var id))
            return;
        Companies.SetFrozen(id, frozen);
        shell.WriteLine(Loc.GetString("cmd-companyfreeze-done", ("id", id), ("frozen", frozen.ToString())));
        Log(shell, $"set company {id} frozen to {frozen}");
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class WFCompanyAdjustCommand : WFCompanyCommand
{
    public override string Command => "companyadjust";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 3 || !long.TryParse(args[1], out var amount) || amount == 0)
        {
            shell.WriteError(Help);
            return;
        }
        if (!TryFind(shell, args[0], out var id))
            return;
        var reason = string.Join(' ', args.Skip(2));
        if (!Companies.AdminAdjust(id, amount, shell.Player?.Name ?? "Server", reason))
        {
            shell.WriteError(Loc.GetString("cmd-companyadjust-overdraw"));
            return;
        }
        shell.WriteLine(Loc.GetString("cmd-companyadjust-done", ("id", id), ("balance", Companies.GetBalance(id) ?? 0)));
        Log(shell, $"adjusted company {id} by {amount}: {reason}");
    }
}

[AdminCommand(AdminFlags.Moderator)]
public sealed class WFCompanyArtCommand : WFCompanyCommand
{
    public override string Command => "companyart";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2 || args[1] is not ("wipe" or "lock" or "unlock"))
        {
            shell.WriteError(Help);
            return;
        }
        if (!TryFind(shell, args[0], out var id))
            return;
        Companies.AdminArt(id, args[1] == "wipe", args[1] switch { "lock" => true, "unlock" => false, _ => null });
        shell.WriteLine(Loc.GetString("cmd-company-done"));
        Log(shell, $"ran companyart {args[1]} on company {id}");
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class WFCompanyRenameCommand : WFCompanyCommand
{
    public override string Command => "companyrename";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 3)
        {
            shell.WriteError(Help);
            return;
        }
        if (!TryFind(shell, args[0], out var id))
            return;
        var name = string.Join(' ', args.Skip(2));
        if (!Companies.AdminRename(id, name, args[1]))
        {
            shell.WriteError(Loc.GetString("cmd-companyrename-invalid"));
            return;
        }
        shell.WriteLine(Loc.GetString("cmd-company-done"));
        Log(shell, $"renamed company {id} to [{args[1]}] {name}");
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class WFCompanyTransferCommand : WFCompanyCommand
{
    public override string Command => "companytransfer";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2 || !int.TryParse(args[1], out var profile))
        {
            shell.WriteError(Help);
            return;
        }
        if (!TryFind(shell, args[0], out var id))
            return;
        if (!Companies.AdminTransfer(id, profile))
        {
            shell.WriteError(Loc.GetString("cmd-companytransfer-invalid"));
            return;
        }
        shell.WriteLine(Loc.GetString("cmd-company-done"));
        Log(shell, $"transferred company {id} to profile {profile}");
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class WFCompanyDisbandCommand : WFCompanyCommand
{
    public override string Command => "companydisband";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Help);
            return;
        }
        if (!TryFind(shell, args[0], out var id))
            return;
        Companies.AdminDisband(id);
        shell.WriteLine(Loc.GetString("cmd-company-done"));
        Log(shell, $"disbanded company {id}");
    }
}
