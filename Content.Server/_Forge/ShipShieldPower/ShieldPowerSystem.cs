using Content.Server.Power.EntitySystems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Forge.ShipShieldPower;
using Content.Shared.Examine;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.Server._Forge.ShipShieldPower;

/// <summary>
/// Lightweight per-grid shield bus: one (or more) ИП banks feed all shield emitters without Pow3r.
/// </summary>
public sealed class ShieldPowerSystem : EntitySystem
{
    [Dependency] private readonly BatterySystem _battery = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5);
    private TimeSpan _accumulator;

    private EntityQuery<BatteryComponent> _batteryQuery;
    private EntityQuery<ShipShieldEmitterComponent> _emitterQuery;

    public override void Initialize()
    {
        base.Initialize();

        _batteryQuery = GetEntityQuery<BatteryComponent>();
        _emitterQuery = GetEntityQuery<ShipShieldEmitterComponent>();

        SubscribeLocalEvent<ShieldPowerSourceComponent, ExaminedEvent>(OnSourceExamined);
        SubscribeLocalEvent<ShieldPowerSourceComponent, GetVerbsEvent<AlternativeVerb>>(OnSourceVerbs);
        SubscribeLocalEvent<ShieldPowerConsumerComponent, ExaminedEvent>(OnConsumerExamined);
        SubscribeLocalEvent<ShieldPowerConsumerComponent, GetVerbsEvent<AlternativeVerb>>(OnConsumerVerbs);
        SubscribeLocalEvent<ShieldPowerConsumerComponent, ComponentStartup>(OnConsumerStartup);
        SubscribeLocalEvent<ShieldPowerSourceComponent, ComponentStartup>(OnSourceStartup);
    }

    private void OnSourceStartup(Entity<ShieldPowerSourceComponent> ent, ref ComponentStartup args)
    {
        UpdateSourceAppearance(ent);
    }

    private void OnConsumerStartup(Entity<ShieldPowerConsumerComponent> ent, ref ComponentStartup args)
    {
        if (_emitterQuery.TryGetComponent(ent, out var emitter))
            ent.Comp.DesiredDraw = emitter.BaseDraw + ShipShieldEmitterMath.CalculateAdditionalLoad(emitter);

        UpdateConsumerAppearance(ent, ent.Comp.Powered);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += TimeSpan.FromSeconds(frameTime);
        if (_accumulator < UpdateInterval)
            return;

        var dt = (float)_accumulator.TotalSeconds;
        _accumulator = TimeSpan.Zero;

        // Collect consumers by grid.
        var consumersByGrid = new Dictionary<EntityUid, List<(EntityUid Uid, ShieldPowerConsumerComponent Comp)>>();
        var consumerQuery = EntityQueryEnumerator<ShieldPowerConsumerComponent, TransformComponent>();
        while (consumerQuery.MoveNext(out var uid, out var consumer, out var xform))
        {
            if (xform.GridUid is not { } grid || !xform.Anchored)
            {
                SetConsumerPower(uid, consumer, 0f, false);
                continue;
            }

            if (!consumersByGrid.TryGetValue(grid, out var list))
            {
                list = new List<(EntityUid, ShieldPowerConsumerComponent)>();
                consumersByGrid[grid] = list;
            }

            list.Add((uid, consumer));
        }

        // Collect sources by grid.
        var sourcesByGrid = new Dictionary<EntityUid, List<(EntityUid Uid, ShieldPowerSourceComponent Comp)>>();
        var sourceQuery = EntityQueryEnumerator<ShieldPowerSourceComponent, TransformComponent>();
        while (sourceQuery.MoveNext(out var uid, out var source, out var xform))
        {
            if (xform.GridUid is not { } grid || !xform.Anchored)
                continue;

            if (!sourcesByGrid.TryGetValue(grid, out var list))
            {
                list = new List<(EntityUid, ShieldPowerSourceComponent)>();
                sourcesByGrid[grid] = list;
            }

            list.Add((uid, source));
        }

        foreach (var (grid, consumers) in consumersByGrid)
        {
            sourcesByGrid.TryGetValue(grid, out var sources);
            DistributeGrid(consumers, sources, dt);
        }

        // Idle recharge sources that had no consumer pass this tick (or leftover capacity).
        if (sourcesByGrid.Count == 0)
            return;

        foreach (var (_, sources) in sourcesByGrid)
        {
            foreach (var (uid, source) in sources)
                TryIdleRecharge(uid, source, dt);
        }
    }

    private void DistributeGrid(
        List<(EntityUid Uid, ShieldPowerConsumerComponent Comp)> consumers,
        List<(EntityUid Uid, ShieldPowerSourceComponent Comp)>? sources,
        float dt)
    {
        // Refresh desired draw from emitters (debounced path for hits).
        var totalDesired = 0f;
        foreach (var (uid, consumer) in consumers)
        {
            if (!consumer.Enabled)
            {
                consumer.DesiredDraw = 0f;
                SetConsumerPower(uid, consumer, 0f, false);
                continue;
            }

            if (_emitterQuery.TryGetComponent(uid, out var emitter))
            {
                if (consumer.LoadDirty || consumer.DesiredDraw <= 0f)
                {
                    consumer.DesiredDraw = emitter.BaseDraw + ShipShieldEmitterMath.CalculateAdditionalLoad(emitter);
                    consumer.LoadDirty = false;
                }
            }

            totalDesired += Math.Max(0f, consumer.DesiredDraw);
        }

        if (sources == null || sources.Count == 0)
        {
            foreach (var (uid, consumer) in consumers)
                SetConsumerPower(uid, consumer, 0f, false);
            return;
        }

        // Pick the best enabled source: most charge, then highest MaxDischarge.
        EntityUid? bestUid = null;
        ShieldPowerSourceComponent? bestSource = null;
        float bestCharge = -1f;

        foreach (var (uid, source) in sources)
        {
            if (!source.Enabled)
                continue;

            if (!_batteryQuery.TryGetComponent(uid, out var battery))
                continue;

            if (battery.CurrentCharge > bestCharge
                || (MathHelper.CloseTo(battery.CurrentCharge, bestCharge)
                    && bestSource != null
                    && source.MaxDischarge > bestSource.MaxDischarge))
            {
                bestCharge = battery.CurrentCharge;
                bestUid = uid;
                bestSource = source;
            }
        }

        if (bestUid == null || bestSource == null || !_batteryQuery.TryGetComponent(bestUid.Value, out var bestBattery))
        {
            foreach (var (uid, consumer) in consumers)
                SetConsumerPower(uid, consumer, 0f, false);
            return;
        }

        bestSource.LastTotalDraw = totalDesired;

        if (totalDesired <= 0f)
        {
            bestSource.LastSupplyRatio = 1f;
            foreach (var (uid, consumer) in consumers)
            {
                consumer.LinkedSource = bestUid;
                SetConsumerPower(uid, consumer, 0f, consumer.Enabled);
            }

            return;
        }

        var maxFromRate = bestSource.MaxDischarge;
        var maxFromCharge = bestBattery.CurrentCharge / Math.Max(dt, 0.001f);
        var available = Math.Min(maxFromRate, maxFromCharge);
        var ratio = Math.Clamp(available / totalDesired, 0f, 1f);
        bestSource.LastSupplyRatio = ratio;

        var joulesToDrain = 0f;
        var powered = ratio >= bestSource.PoweredThreshold;

        foreach (var (uid, consumer) in consumers)
        {
            if (!consumer.Enabled)
                continue;

            consumer.LinkedSource = bestUid;
            var received = consumer.DesiredDraw * ratio;
            joulesToDrain += received * dt;
            SetConsumerPower(uid, consumer, received, powered && received > 0f);
        }

        if (joulesToDrain > 0f)
            _battery.UseCharge(bestUid.Value, joulesToDrain, bestBattery);

        UpdateSourceAppearance((bestUid.Value, bestSource));
        SyncSourcePoweredEvent(bestUid.Value, bestSource, bestBattery);
    }

    private void SyncSourcePoweredEvent(EntityUid uid, ShieldPowerSourceComponent source, BatteryComponent battery)
    {
        var powered = source.Enabled && battery.CurrentCharge > 1f;
        if (powered == source.LastPowered)
            return;

        source.LastPowered = powered;
        var ev = new PowerChangedEvent(powered, source.LastTotalDraw);
        RaiseLocalEvent(uid, ref ev);
    }

    private void TryIdleRecharge(EntityUid uid, ShieldPowerSourceComponent source, float dt)
    {
        if (!source.Enabled || source.IdleRechargeRate <= 0f)
            return;

        if (!_batteryQuery.TryGetComponent(uid, out var battery))
            return;

        if (battery.CurrentCharge >= battery.MaxCharge)
            return;

        // Scale idle recharge down while under heavy combat draw.
        var loadFactor = source.LastTotalDraw <= 0f
            ? 1f
            : Math.Clamp(1f - source.LastTotalDraw / Math.Max(source.MaxDischarge, 1f), 0f, 1f);

        var rate = source.IdleRechargeRate * loadFactor;
        if (rate <= 0f)
            return;

        _battery.ChangeCharge(uid, rate * dt, battery);
        UpdateSourceAppearance((uid, source));
    }

    private void SetConsumerPower(EntityUid uid, ShieldPowerConsumerComponent consumer, float received, bool powered)
    {
        var wasPowered = consumer.Powered;
        var oldReceived = consumer.PowerReceived;

        consumer.PowerReceived = received;
        consumer.Powered = powered;

        if (wasPowered == powered && MathHelper.CloseToPercent(oldReceived, received))
            return;

        if (wasPowered != powered)
        {
            var ev = new PowerChangedEvent(powered, received);
            RaiseLocalEvent(uid, ref ev);
            UpdateConsumerAppearance(uid, powered);
        }
    }

    private void UpdateConsumerAppearance(EntityUid uid, bool powered)
    {
        _appearance.SetData(uid, PowerDeviceVisuals.Powered, powered);
    }

    private void UpdateSourceAppearance(Entity<ShieldPowerSourceComponent> ent)
    {
        if (!_batteryQuery.TryGetComponent(ent, out var battery) || battery.MaxCharge <= 0f)
            return;

        var pct = battery.CurrentCharge / battery.MaxCharge;
        _appearance.SetData(ent, PowerDeviceVisuals.Powered, ent.Comp.Enabled && pct > 0.01f);
    }

    private void OnSourceExamined(EntityUid uid, ShieldPowerSourceComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (!_batteryQuery.TryGetComponent(uid, out var battery))
            return;

        var chargeKj = battery.CurrentCharge / 1000f;
        var maxKj = battery.MaxCharge / 1000f;
        var drawKw = component.LastTotalDraw / 1000f;
        var maxKw = component.MaxDischarge / 1000f;
        var enabled = Loc.GetString(component.Enabled
            ? "shield-power-source-examine-enabled"
            : "shield-power-source-examine-disabled");

        args.PushMarkup(Loc.GetString("shield-power-source-examine",
            ("charge", chargeKj.ToString("F0")),
            ("max", maxKj.ToString("F0")),
            ("draw", drawKw.ToString("F1")),
            ("maxdraw", maxKw.ToString("F0")),
            ("ratio", (component.LastSupplyRatio * 100f).ToString("F0")),
            ("state", enabled)));
    }

    private void OnConsumerExamined(EntityUid uid, ShieldPowerConsumerComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var desiredKw = component.DesiredDraw / 1000f;
        var recvKw = component.PowerReceived / 1000f;
        var linked = component.LinkedSource != null && Exists(component.LinkedSource.Value);

        args.PushMarkup(Loc.GetString("shield-power-consumer-examine",
            ("desired", desiredKw.ToString("F1")),
            ("received", recvKw.ToString("F1")),
            ("powered", component.Powered),
            ("linked", linked)));
    }

    private void OnSourceVerbs(EntityUid uid, ShieldPowerSourceComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(component.Enabled
                ? "shield-power-source-verb-disable"
                : "shield-power-source-verb-enable"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/Spare/poweronoff.svg.192dpi.png")),
            Priority = -3,
            Act = () =>
            {
                component.Enabled = !component.Enabled;
                UpdateSourceAppearance((uid, component));
            },
        });
    }

    private void OnConsumerVerbs(EntityUid uid, ShieldPowerConsumerComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(component.Enabled
                ? "shield-power-consumer-verb-disable"
                : "shield-power-consumer-verb-enable"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/Spare/poweronoff.svg.192dpi.png")),
            Priority = -3,
            Act = () =>
            {
                component.Enabled = !component.Enabled;
                if (!component.Enabled)
                    SetConsumerPower(uid, component, 0f, false);
            },
        });
    }

    /// <summary>
    /// Mark consumer load dirty after shield damage so DesiredDraw is refreshed on the next power tick.
    /// </summary>
    public void MarkLoadDirty(EntityUid uid, ShieldPowerConsumerComponent? consumer = null)
    {
        if (!Resolve(uid, ref consumer, false))
            return;

        consumer.LoadDirty = true;
    }

    /// <summary>
    /// Immediately recompute DesiredDraw from the emitter (used by the emitter update tick).
    /// </summary>
    public void SyncDesiredDraw(EntityUid uid, ShipShieldEmitterComponent emitter, ShieldPowerConsumerComponent? consumer = null)
    {
        if (!Resolve(uid, ref consumer, false))
            return;

        consumer.DesiredDraw = emitter.BaseDraw + ShipShieldEmitterMath.CalculateAdditionalLoad(emitter);
        consumer.LoadDirty = false;
    }
}
