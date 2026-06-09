using Content.Shared.Movement.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Physiology.Disease;

public sealed partial class DiseaseMovespeedSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _movespeed = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DiseaseMovespeedComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<DiseaseMovespeedComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<DiseaseMovespeedComponent, RefreshMovementSpeedModifiersEvent>(OnRefresh);
    }

    private void OnInit(EntityUid uid, DiseaseMovespeedComponent comp, ComponentInit args)
    {
        _movespeed.RefreshMovementSpeedModifiers(uid);
    }

    private void OnShutdown(EntityUid uid, DiseaseMovespeedComponent comp, ComponentShutdown args)
    {
        _movespeed.RefreshMovementSpeedModifiers(uid);
    }

    private void OnRefresh(EntityUid uid, DiseaseMovespeedComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(comp.WalkSpeedModifier, comp.SprintSpeedModifier);
    }
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DiseaseMovespeedComponent : Component
{
    [DataField, AutoNetworkedField]
    public float WalkSpeedModifier = 1f;

    [DataField, AutoNetworkedField]
    public float SprintSpeedModifier = 1f;
}
