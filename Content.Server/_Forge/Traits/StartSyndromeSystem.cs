using Content.Server._Forge.Body.Syndromes;
using Content.Shared._Forge.Traits;

namespace Content.Server._Forge.Traits;

public sealed partial class StartSyndromeSystem : EntitySystem
{
    [Dependency] private SyndromeSystem _disease = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StartSyndromeComponent, ComponentInit>(OnInit);
    }

    private void OnInit(Entity<StartSyndromeComponent> ent, ref ComponentInit args)
    {
        _disease.AddSyndrome(ent.Owner, ent.Comp.Syndrome, ent.Comp.Severity);
        RemCompDeferred<StartSyndromeComponent>(ent.Owner);
    }
}
