using Content.Server._Forge.Physiology.Disease;
using Content.Shared._Forge.Traits;

namespace Content.Server._Forge.Traits;

public sealed partial class StartingDiseaseSystem : EntitySystem
{
    [Dependency] private PhysiologyDiseaseSystem _disease = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StartDiseaseComponent, ComponentInit>(OnInit);
    }

    private void OnInit(Entity<StartDiseaseComponent> ent, ref ComponentInit args)
    {
        _disease.AddDisease(ent.Owner, ent.Comp.Disease, ent.Comp.Severity);
        RemCompDeferred<StartDiseaseComponent>(ent.Owner);
    }
}
