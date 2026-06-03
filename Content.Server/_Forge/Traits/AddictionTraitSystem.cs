using Content.Shared._Forge.Traits.Physical;
using Content.Server._Forge.Chemistry.Addiction;

namespace Content.Server._Forge.Traits.Physical;

public sealed partial class AddictionTraitSystem : EntitySystem
{
    [Dependency] private AddictionSystem _addictionSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AddictionTraitComponent, ComponentInit>(OnInit);
    }

    private void OnInit(EntityUid uid, AddictionTraitComponent comp, ComponentInit args)
    {
        _addictionSystem.ApplyDose(uid, comp.AddictionId, comp.InitialTolerance);
    }
}
