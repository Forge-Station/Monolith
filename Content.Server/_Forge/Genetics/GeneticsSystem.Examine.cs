using Content.Shared._Forge.Genetics.Components;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;

namespace Content.Server._Forge.Genetics;

public sealed partial class GeneticsSystem
{
    private void InitializeExamine()
    {
        SubscribeLocalEvent<GenomeComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<GenomeComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var active = 0;
        foreach (var state in ent.Comp.Genes.Values)
        {
            if (state.Active)
                active++;
        }

        if (active == 0)
            return;

        var name = Identity.Name(ent, EntityManager, args.Examiner);
        args.PushMarkup(Loc.GetString("genetics-examine-mutated", ("name", name), ("count", active)));
    }
}
