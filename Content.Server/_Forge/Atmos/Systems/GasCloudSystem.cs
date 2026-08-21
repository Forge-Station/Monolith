using Content.Server._Mono.Radar;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._Forge.Atmos.Components;
using Content.Shared._Forge.Atmos.Systems;
using Content.Shared._Mono.Radar;
using Content.Shared._NF.Atmos.Components;
using Content.Shared.Alert;
using Content.Shared.Atmos;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Atmos.Systems;

public sealed partial class GasCloudSystem : SharedGasCloudSystem
{
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private HungerSystem _hunger = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedInternalsSystem _internals = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TemperatureSystem _temperature = default!;

    private static readonly ProtoId<AlertPrototype> GasCloudAlert = "GasCloud";

    private const float UpdateInterval = 0.5f;
    private const float MinCloudRadius = 0.5f;

    private TimeSpan _nextUpdate;
    private readonly HashSet<EntityUid> _inCloud = new();
    private readonly HashSet<EntityUid> _rangeBuffer = new();
    private readonly List<EntityUid> _toRemove = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GasCloudComponent, MapInitEvent>(OnCloudMapInit);
        SubscribeLocalEvent<GasCloudComponent, ExaminedEvent>(OnCloudExamined);
    }

    private void OnCloudMapInit(Entity<GasCloudComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.CurrentRadius = ent.Comp.Radius;
        SyncRadarBlip(ent);
        Dirty(ent);
    }

    private void OnCloudExamined(Entity<GasCloudComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("gas-cloud-examined-radius",
            ("radius", MathF.Round(ent.Comp.CurrentRadius, 1))));

        if (ent.Comp.Pressure > 0)
        {
            args.PushMarkup(Loc.GetString("gas-cloud-examined-pressure",
                ("pressure", MathF.Round(ent.Comp.Pressure))));
        }

        if (ent.Comp.Temperature is { } temperature)
        {
            args.PushMarkup(Loc.GetString("gas-cloud-examined-temperature",
                ("celsius", MathF.Round(temperature - Atmospherics.T0C, 1))));
        }

        if (!ent.Comp.ToxinDamage.Empty)
        {
            args.PushMarkup(Loc.GetString("gas-cloud-examined-toxin"));
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(UpdateInterval);

        _inCloud.Clear();

        var cloudQuery = EntityQueryEnumerator<GasCloudComponent, TransformComponent>();
        while (cloudQuery.MoveNext(out var cloudUid, out var cloud, out var cloudXform))
        {
            UpdateCloudRadius((cloudUid, cloud));

            if (cloud.CurrentRadius < MinCloudRadius)
                continue;

            var cloudGrid = cloudXform.GridUid;
            var cloudMap = _transform.GetMapCoordinates(cloudUid, cloudXform);

            _rangeBuffer.Clear();
            _lookup.GetEntitiesInRange(cloudXform.Coordinates, cloud.CurrentRadius, _rangeBuffer);

            foreach (var target in _rangeBuffer)
            {
                if (target == cloudUid)
                    continue;

                if (!_inCloud.Add(target))
                {
                    // Already affected by another cloud this tick; keep the harsher values.
                    if (TryComp(target, out GasCloudAffectedComponent? existing))
                        MergeEffects(existing, cloud, target);
                    continue;
                }

                if (!CanAffect(target, cloudGrid))
                {
                    _inCloud.Remove(target);
                    continue;
                }

                var targetMap = _transform.GetMapCoordinates(target);
                if (targetMap.MapId != cloudMap.MapId)
                {
                    _inCloud.Remove(target);
                    continue;
                }

                ApplyCloudEffects(target, cloud);
            }
        }

        var affectedQuery = EntityQueryEnumerator<GasCloudAffectedComponent>();
        _toRemove.Clear();
        while (affectedQuery.MoveNext(out var uid, out _))
        {
            if (!_inCloud.Contains(uid))
                _toRemove.Add(uid);
        }

        foreach (var uid in _toRemove)
        {
            _alerts.ClearAlert(uid, GasCloudAlert);
            RemComp<GasCloudAffectedComponent>(uid);
        }
    }

    private bool CanAffect(EntityUid target, EntityUid? cloudGrid)
    {
        if (HasComp<GhostComponent>(target))
            return false;

        if (HasComp<MobStateComponent>(target) && !_mobState.IsAlive(target))
            return false;

        if (!HasComp<MobStateComponent>(target) && !HasComp<HungerComponent>(target))
            return false;

        var targetGrid = Transform(target).GridUid;
        if (targetGrid != null && targetGrid != cloudGrid)
            return false;

        return true;
    }

    private void ApplyCloudEffects(EntityUid target, GasCloudComponent cloud)
    {
        var affected = EnsureComp<GasCloudAffectedComponent>(target);
        var speedChanged = !MathHelper.CloseTo(affected.WalkSpeedModifier, cloud.WalkSpeedModifier)
            || !MathHelper.CloseTo(affected.SprintSpeedModifier, cloud.SprintSpeedModifier);

        affected.WalkSpeedModifier = cloud.WalkSpeedModifier;
        affected.SprintSpeedModifier = cloud.SprintSpeedModifier;
        affected.Pressure = cloud.Pressure;
        affected.HungerMultiplier = cloud.HungerMultiplier;
        Dirty(target, affected);

        if (speedChanged)
            RefreshCloudMovement(target);

        _alerts.ShowAlert(target, GasCloudAlert);
        ApplyHazards(target, cloud);

        if (cloud.HungerMultiplier > 1f
            && HasComp<HungerComponent>(target)
            && _mobState.IsAlive(target))
        {
            // Approximate extra decay without reading HungerComponent internals (Access-restricted).
            const float baseHungerDecay = 0.018f;
            var extraDecay = baseHungerDecay * (cloud.HungerMultiplier - 1f) * UpdateInterval;
            if (extraDecay > 0f)
                _hunger.ModifyHunger(target, -extraDecay);
        }
    }

    private void MergeEffects(GasCloudAffectedComponent affected, GasCloudComponent cloud, EntityUid target)
    {
        var walk = MathF.Min(affected.WalkSpeedModifier, cloud.WalkSpeedModifier);
        var sprint = MathF.Min(affected.SprintSpeedModifier, cloud.SprintSpeedModifier);
        var speedChanged = !MathHelper.CloseTo(affected.WalkSpeedModifier, walk)
            || !MathHelper.CloseTo(affected.SprintSpeedModifier, sprint);

        affected.WalkSpeedModifier = walk;
        affected.SprintSpeedModifier = sprint;
        affected.Pressure = MathF.Max(affected.Pressure, cloud.Pressure);
        affected.HungerMultiplier = MathF.Max(affected.HungerMultiplier, cloud.HungerMultiplier);
        Dirty(target, affected);

        if (speedChanged)
            RefreshCloudMovement(target);

        ApplyHazards(target, cloud);
    }

    private void ApplyHazards(EntityUid target, GasCloudComponent cloud)
    {
        ApplyToxin(target, cloud);
        ApplyTemperature(target, cloud);
    }

    private void ApplyToxin(EntityUid target, GasCloudComponent cloud)
    {
        if (cloud.ToxinDamage.Empty)
            return;

        if (cloud.InternalsBlockToxins && _internals.AreInternalsWorking(target))
            return;

        _damageable.TryChangeDamage(target, cloud.ToxinDamage * UpdateInterval, interruptsDoAfters: false);
    }

    private void ApplyTemperature(EntityUid target, GasCloudComponent cloud)
    {
        if (cloud.Temperature is not { } targetTemp)
            return;

        if (!TryComp(target, out TemperatureComponent? temperature))
            return;

        var delta = targetTemp - temperature.CurrentTemperature;
        if (MathF.Abs(delta) < 0.5f)
            return;

        var heat = delta * _temperature.GetHeatCapacity(target, temperature) * cloud.TemperatureTransfer * UpdateInterval;
        _temperature.ChangeHeat(target, heat, temperature: temperature);
    }

    private void UpdateCloudRadius(Entity<GasCloudComponent> ent)
    {
        if (!TryComp(ent.Owner, out GasDepositComponent? deposit))
        {
            if (!MathHelper.CloseTo(ent.Comp.CurrentRadius, ent.Comp.Radius))
            {
                ent.Comp.CurrentRadius = ent.Comp.Radius;
                SyncRadarBlip(ent);
                Dirty(ent);
            }

            return;
        }

        if (deposit.YieldBased)
        {
            var yieldRadius = ent.Comp.Radius * MathF.Sqrt(MathF.Max(deposit.MinYield, deposit.Yield));
            if (MathHelper.CloseTo(ent.Comp.CurrentRadius, yieldRadius))
                return;

            ent.Comp.CurrentRadius = yieldRadius;
            SyncRadarBlip(ent);
            Dirty(ent);
            return;
        }

        if (ent.Comp.InitialMoles <= 0f && deposit.GasLeft > 0f)
            ent.Comp.InitialMoles = deposit.GasLeft;

        var fraction = ent.Comp.InitialMoles <= 0f
            ? 0f
            : Math.Clamp(deposit.GasLeft / ent.Comp.InitialMoles, 0f, 1f);
        var radius = ent.Comp.Radius * MathF.Sqrt(fraction);

        if (MathHelper.CloseTo(ent.Comp.CurrentRadius, radius))
            return;

        ent.Comp.CurrentRadius = radius;
        SyncRadarBlip(ent);
        Dirty(ent);
    }

    private void SyncRadarBlip(Entity<GasCloudComponent> ent)
    {
        if (!TryComp(ent.Owner, out RadarBlipComponent? blip))
            return;

        var r = MathF.Max(ent.Comp.CurrentRadius, MinCloudRadius);
        var config = blip.Config;
        config.Bounds = new Box2(-r, -r, r, r);
        config.Color = ent.Comp.Color.WithAlpha(0.45f);
        config.Shape = RadarBlipShape.Circle;
        config.RespectZoom = true;
        blip.Config = config;
    }
}
