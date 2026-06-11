using Content.Shared.Movement.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Body.Syndromes;

public sealed partial class SyndromeMovespeedSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _movespeed = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SyndromeMovespeedComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<SyndromeMovespeedComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<SyndromeMovespeedComponent, RefreshMovementSpeedModifiersEvent>(OnRefresh);
    }

    private void OnInit(EntityUid uid, SyndromeMovespeedComponent comp, ComponentInit args)
    {
        _movespeed.RefreshMovementSpeedModifiers(uid);
    }

    private void OnShutdown(EntityUid uid, SyndromeMovespeedComponent comp, ComponentShutdown args)
    {
        _movespeed.RefreshMovementSpeedModifiers(uid);
    }

    private void OnRefresh(EntityUid uid, SyndromeMovespeedComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(comp.WalkSpeedModifier, comp.SprintSpeedModifier);
    }
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SyndromeMovespeedComponent : Component
{
    [DataField, AutoNetworkedField]
    public float WalkSpeedModifier = 1f;

    [DataField, AutoNetworkedField]
    public float SprintSpeedModifier = 1f;
}
