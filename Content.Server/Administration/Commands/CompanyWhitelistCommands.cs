using System.Linq;
using Content.Server.Database;
using Content.Shared.Administration;
using Content.Shared._Mono.Company;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

// Forge-Change-full: company whitelist
namespace Content.Server.Administration.Commands;

[AdminCommand(AdminFlags.WhitelistManager)]
public sealed class CompanyWhitelistAddCommand : LocalizedCommands
{
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IPlayerLocator _playerLocator = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override string Command => "companywhitelistadd";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError("Usage: companywhitelistadd <playerName> <companyId>");
            return;
        }

        var player = args[0].Trim();
        var companyId = args[1].Trim();

        if (!_prototypes.TryIndex<CompanyPrototype>(companyId, out _))
        {
            shell.WriteError($"Company '{companyId}' does not exist.");
            return;
        }

        var data = await _playerLocator.LookupIdByNameAsync(player);
        if (data == null)
        {
            shell.WriteError($"Player '{player}' not found.");
            return;
        }

        if (!await _db.AddCompanyWhitelist(data.UserId, companyId))
        {
            shell.WriteLine($"Player '{player}' is already whitelisted for '{companyId}'.");
            return;
        }

        shell.WriteLine($"Added company whitelist '{companyId}' for '{player}'.");
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(_players.Sessions.Select(s => s.Name), "Player name");

        if (args.Length == 2)
            return CompletionResult.FromHintOptions(_prototypes.EnumeratePrototypes<CompanyPrototype>().Select(p => p.ID), "Company ID");

        return CompletionResult.Empty;
    }
}

[AdminCommand(AdminFlags.WhitelistManager)]
public sealed class GetCompanyWhitelistCommand : LocalizedCommands
{
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IPlayerLocator _playerLocator = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    public override string Command => "companywhitelistget";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteError("Usage: companywhitelistget <playerName>");
            return;
        }

        var player = string.Join(' ', args).Trim();
        var data = await _playerLocator.LookupIdByNameAsync(player);
        if (data == null)
        {
            shell.WriteError($"Player '{player}' not found.");
            return;
        }

        var whitelists = await _db.GetCompanyWhitelists(data.UserId);
        if (whitelists.Count == 0)
        {
            shell.WriteLine($"Player '{player}' has no company whitelists.");
            return;
        }

        shell.WriteLine($"Player '{player}' company whitelists: {string.Join(", ", whitelists)}");
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(_players.Sessions.Select(s => s.Name), "Player name");

        return CompletionResult.Empty;
    }
}

[AdminCommand(AdminFlags.WhitelistManager)]
public sealed class RemoveCompanyWhitelistCommand : LocalizedCommands
{
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IPlayerLocator _playerLocator = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override string Command => "companywhitelistremove";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError("Usage: companywhitelistremove <playerName> <companyId>");
            return;
        }

        var player = args[0].Trim();
        var companyId = args[1].Trim();

        if (!_prototypes.TryIndex<CompanyPrototype>(companyId, out _))
        {
            shell.WriteError($"Company '{companyId}' does not exist.");
            return;
        }

        var data = await _playerLocator.LookupIdByNameAsync(player);
        if (data == null)
        {
            shell.WriteError($"Player '{player}' not found.");
            return;
        }

        if (!await _db.RemoveCompanyWhitelist(data.UserId, companyId))
        {
            shell.WriteError($"Player '{player}' is not whitelisted for '{companyId}'.");
            return;
        }

        shell.WriteLine($"Removed company whitelist '{companyId}' for '{player}'.");
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(_players.Sessions.Select(s => s.Name), "Player name");

        if (args.Length == 2)
            return CompletionResult.FromHintOptions(_prototypes.EnumeratePrototypes<CompanyPrototype>().Select(p => p.ID), "Company ID");

        return CompletionResult.Empty;
    }
}
