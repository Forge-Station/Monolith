using Content.Server.Administration;
using Content.Shared._WF.Xenonids.Acid;
using Content.Shared.Administration;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.IoC;

namespace Content.Server._WF.Xenonids.Acid;

[AdminCommand(AdminFlags.Debug)]
public sealed class RMCAcidInstantCommand : IConsoleCommand
{
    public string Command => "rmc_acid_instant";
    public string Description => "Toggle or set instant corrosive acid.";
    public string Help => "Usage: rmc_acid_instant [true/false]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var config = IoCManager.Resolve<IConfigurationManager>();
        var current = config.GetCVar(WFXenoAcidCVars.RMCCorrosiveAcidInstant);
        bool next;

        if (args.Length == 0)
        {
            next = !current;
        }
        else if (!bool.TryParse(args[0], out next))
        {
            shell.WriteError("Expected true/false.");
            return;
        }

        config.SetCVar(WFXenoAcidCVars.RMCCorrosiveAcidInstant, next);
        shell.WriteLine($"rmc.corrosive_acid_instant set to {next}.");
    }
}
