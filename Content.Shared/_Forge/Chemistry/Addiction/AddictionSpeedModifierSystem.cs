using Content.Shared.Movement.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Chemistry.Addiction;

/// <summary>
///    This shit exists exclusively for the AddictionSystem for one simple reason:
///    <see cref="MovementSpeedModifierSystem"/> overwrite the original modifier when the stage changes.
///    Protection against the overwriting curve and
///    persistence at the wrong speed even after successful addiction treatment.
/// </summary>
public sealed partial class AddictionMovespeedSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _movespeed = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AddictionMovespeedComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<AddictionMovespeedComponent, RefreshMovementSpeedModifiersEvent>(OnRefresh);
    }

    private void OnInit(EntityUid uid, AddictionMovespeedComponent comp, ComponentInit args)
    {
        _movespeed.RefreshMovementSpeedModifiers(uid);
    }

    private void OnRefresh(EntityUid uid, AddictionMovespeedComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(comp.WalkSpeedModifier, comp.SprintSpeedModifier);
    }
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AddictionMovespeedComponent : Component
{
    [DataField, AutoNetworkedField]
    public float WalkSpeedModifier = 1f;

    [DataField, AutoNetworkedField]
    public float SprintSpeedModifier = 1f;
}
