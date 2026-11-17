using Content.Server._Forge.Genetics;
using Content.Server.Administration;
using Content.Shared._Forge.Genetics;
using Content.Shared._Forge.Genetics.Components;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Genetics.Commands;

[AdminCommand(AdminFlags.Fun)]
public sealed class AddGeneCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public string Command => "addgene";
    public string Description => "Activate a genetics gene on a target with a GenomeComponent.";
    public string Help => "addgene <entity> <genePrototypeId>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!NetEntity.TryParse(args[0], out var netEnt) || !_entities.TryGetEntity(netEnt, out var uid))
        {
            shell.WriteLine("Invalid entity.");
            return;
        }

        if (!_prototypes.HasIndex<GenePrototype>(args[1]))
        {
            shell.WriteLine($"Unknown gene '{args[1]}'.");
            return;
        }

        var genome = _entities.EnsureComponent<GenomeComponent>(uid.Value);
        var genetics = _entities.System<GeneticsSystem>();
        if (genetics.TryInjectGene(uid.Value, args[1], genome))
            shell.WriteLine($"Activated {args[1]} on {uid}.");
        else
            shell.WriteLine("Failed to activate gene.");
    }
}
