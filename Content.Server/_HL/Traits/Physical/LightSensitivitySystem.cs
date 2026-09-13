using Content.Server._Starlight;
using Content.Server._Starlight.Shadekin;
using Content.Shared._HL.Traits.Physical;
using Content.Shared._Starlight;
using Content.Shared._Starlight.Shadekin;
using Content.Shared.Alert;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._HL.Traits.Physical;

/// <summary>
/// Handles light sensitivity burn damage and movement penalty for non-shadekin entities.
/// Shadekin entities are handled by ShadekinSystem instead.
/// </summary>
public sealed class LightSensitivitySystem : EntitySystem
{
    private static readonly ProtoId<AlertPrototype> LightExposureAlert = "Shadekin";

    /// <summary>
    /// Concrete shadekin prototype the light-exposure thresholds are read from, so non-shadekins
    /// bucket light off the same YAML data shadekins use rather than a duplicated hardcoded curve.
    /// </summary>
    private static readonly EntProtoId ShadekinProto = "MobShadekin";

    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _speed = default!;
    [Dependency] private readonly ShadekinSystem _shadekin = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IComponentFactory _compFactory = default!;

    // Cached thresholds resolved from the shadekin prototype; invalidated on prototype reload.
    private SortedDictionary<FixedPoint2, ShadekinState>? _thresholds;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LightSensitivityComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeedModifiers);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => _thresholds = null);
    }

    /// <summary>
    /// The shadekin light-exposure thresholds, read once from <see cref="ShadekinProto"/>'s
    /// <see cref="ShadekinComponent"/>. Falls back to the canonical default curve if the prototype or
    /// component can't be read, so light sensitivity keeps working.
    /// </summary>
    private SortedDictionary<FixedPoint2, ShadekinState> Thresholds
    {
        get
        {
            if (_thresholds != null)
                return _thresholds;

            if (_proto.TryIndex(ShadekinProto, out var proto)
                && proto.TryGetComponent<ShadekinComponent>(out var shadekin, _compFactory))
            {
                _thresholds = shadekin.Thresholds;
            }
            else
            {
                Log.Error($"Could not read light-exposure thresholds from prototype '{ShadekinProto}'; falling back to default curve.");
                _thresholds = new()
                {
                    { FixedPoint2.New(0.8f), ShadekinState.Low },
                    { FixedPoint2.New(5), ShadekinState.Annoying },
                    { FixedPoint2.New(10), ShadekinState.High },
                    { FixedPoint2.New(15), ShadekinState.Extreme },
                };
            }

            return _thresholds;
        }
    }

    private void OnRefreshSpeedModifiers(EntityUid uid, LightSensitivityComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        if (HasComp<ShadekinComponent>(uid))
            return; // ShadekinSystem handles shadekins

        if (comp.CurrentLightExposure < comp.SlowdownThreshold)
            return;

        args.ModifySpeed(comp.SpeedMultiplier, comp.SpeedMultiplier);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<LightSensitivityComponent, DamageableComponent>();

        while (query.MoveNext(out var uid, out var comp, out var _))
        {
            if (HasComp<ShadekinComponent>(uid))
                continue; // ShadekinSystem handles shadekins

            if (curTime < comp.NextUpdate)
                continue;

            comp.NextUpdate = curTime + comp.UpdateCooldown;

            // Discretize to the 1-5 ShadekinState scale via the shared mapping, off the shadekin's own
            // YAML thresholds, so thresholds (burnThreshold, slowdownThreshold) and the alert behave
            // identically for non-shadekins as they do for shadekins.
            var raw = _shadekin.GetLightExposure(uid);
            comp.CurrentLightExposure = (int) ShadekinSystem.GetLightExposureLevel(Thresholds, raw);

            ApplyBurnDamage(uid, comp);
            _speed.RefreshMovementSpeedModifiers(uid);
            _alerts.ShowAlert(uid, LightExposureAlert, (short) comp.CurrentLightExposure);
        }
    }

    private void ApplyBurnDamage(EntityUid uid, LightSensitivityComponent comp)
    {
        if (comp.CurrentLightExposure < comp.BurnThreshold)
            return;

        var multiplier = (int) comp.CurrentLightExposure - comp.BurnThreshold + 1;
        var damage = new DamageSpecifier();
        damage.DamageDict.Add("Heat", multiplier);
        _damageable.TryChangeDamage(uid, damage, true, false);
    }
}
