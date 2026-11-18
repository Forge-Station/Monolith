using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Body.Systems;
using Content.Server.Light.EntitySystems;
using Content.Server.Speech.EntitySystems;
using Content.Shared._Forge.Hive.Components;
using Content.Shared._Starlight.CollectiveMind;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Drunk;
using Content.Shared.Emp;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Jittering;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Hive.Abilities;


public sealed partial class HiveAbilitySystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly CollectiveMindUpdateSystem _collectiveMind = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedDrunkSystem _drunk = default!;
    [Dependency] private readonly SharedEmpSystem _emp = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly HiveSystem _hive = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedJitteringSystem _jitter = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly PoweredLightSystem _light = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly QuickDialogSystem _quickDialog = default!;
    [Dependency] private readonly StaminaSystem _stamina = default!;
    [Dependency] private readonly StatusEffectsSystem _status = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly StutteringSystem _stutter = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        InitializeBodyParts();
        InitializeAdrenaline();
        InitializeFleshmend();
        InitializeShrieks();
        InitializeEyesight();
        InitializeMuscles();
        InitializeChameleon();
        InitializeHivemind();
        InitializeTentacle();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        UpdateAdrenaline(now);
        UpdateFleshmend(now);
        UpdateMuscles(now);
        UpdateHivemind(now);
    }
}
