using Content.Shared._Onyx.Body.Systems;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared._WF.Synthetics;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Radiation.Events;
using Content.Shared.Tools.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Synthetics;

/// <summary>
/// Radiation on a synthetic goes to its collector, never to the body's damage. Full, the overflow strains the
/// collector and leaks into the core at half rate; missing or burnt out, it all corrupts the core. A pulled collector
/// vents its charge and can be welded back to full integrity; at zero integrity it burns out and is gone.
/// </summary>
public sealed class WFRadCollectorSystem : EntitySystem
{
    private static readonly ProtoId<DamageTypePrototype> Radiation = "Radiation";
    private static readonly TimeSpan WarningCooldown = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan VentInterval = TimeSpan.FromSeconds(1);

    /// <summary>Core corruption per rad when no collector is fitted.</summary>
    public const float BareCorruptionPerRad = 0.02f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private OrganHealthSystem _organs = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedToolSystem _tools = default!;
    [Dependency] private WolfmedLifeSystem _life = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _nextWarning = new();

    /// <summary>The last radiation each chassis took, in rads a second, and when.</summary>
    private readonly Dictionary<EntityUid, (float Rate, TimeSpan At)> _exposure = new();

    /// <summary>How long a reading holds after the last irradiation; the radiation system ticks once a second.</summary>
    private static readonly TimeSpan ExposureHold = TimeSpan.FromSeconds(2.5);
    private TimeSpan _nextVent;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFChassisComponent, OnIrradiatedEvent>(OnIrradiated);
        SubscribeLocalEvent<WFChassisComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<WFChassisComponent, ComponentShutdown>(OnChassisShutdown);
        SubscribeLocalEvent<WFRadCollectorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WFRadCollectorComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WFRadCollectorComponent, WFRadCollectorWeldDoAfterEvent>(OnWeldDone);
    }

    private void OnChassisShutdown(Entity<WFChassisComponent> ent, ref ComponentShutdown args)
    {
        _nextWarning.Remove(ent);
        _exposure.Remove(ent);
    }

    /// <summary>Rads a second reaching the chassis now: the built-in meter the chassis panel reads.</summary>
    public float GetExposure(EntityUid body) =>
        _exposure.TryGetValue(body, out var last) && _timing.CurTime - last.At <= ExposureHold ? last.Rate : 0f;

    // A chassis keeps no radiation of its own: the collector or the core takes it in OnIrradiated.
    private void OnDamageModify(Entity<WFChassisComponent> ent, ref DamageModifyEvent args)
    {
        if (!args.Damage.DamageDict.ContainsKey(Radiation))
            return;

        var damage = new DamageSpecifier(args.Damage);
        damage.DamageDict.Remove(Radiation);
        args.Damage = damage;
    }

    private void OnIrradiated(EntityUid uid, WFChassisComponent comp, OnIrradiatedEvent args)
    {
        _exposure[uid] = (args.RadsPerSecond, _timing.CurTime);
        var rads = args.TotalRads;
        if (rads > 0f)
            Absorb(uid, rads);
    }

    /// <summary>Takes the rads into the body's collector, and what it cannot hold into the core.</summary>
    public void Absorb(EntityUid body, float rads)
    {
        var collector = GetCollector(body);
        var corruption = BareCorruptionPerRad * rads;
        if (collector is { } found)
        {
            var comp = found.Comp1;
            var taken = MathF.Min(rads, MathF.Max(0f, comp.Capacity - comp.Charge));
            comp.Charge += taken;
            Dirty(found.Owner, comp);

            var overflow = rads - taken;
            corruption = overflow * comp.CorruptionPerRad * 0.5f;
            if (overflow > 0f)
            {
                _organs.ChangeHealth((found.Owner, found.Comp2), -FixedPoint2.New(overflow * comp.StrainPerRad));
                Warn(body, found.Comp2.Health > FixedPoint2.Zero
                    ? "wf-rad-collector-saturated"
                    : "wf-rad-collector-burnt-out");
            }
        }
        else
        {
            Warn(body, "wf-rad-collector-missing");
        }

        if (corruption > 0f && _life.GetBrainOrgan(body) is { } core && core.Comp.Health > FixedPoint2.Zero)
            _organs.ChangeHealth(core, -FixedPoint2.New(corruption));
    }

    /// <summary>The body's working collector: fitted and not burnt out.</summary>
    public Entity<WFRadCollectorComponent, WolfmedOrganComponent>? GetCollector(EntityUid body)
    {
        foreach (var (organ, _) in _body.GetBodyOrgans(body))
        {
            if (TryComp(organ, out WFRadCollectorComponent? collector) &&
                TryComp(organ, out WolfmedOrganComponent? health) &&
                health.Health > FixedPoint2.Zero)
                return (organ, collector, health);
        }

        return null;
    }

    /// <summary>Whether radiation reaching the body is corrupting its core: no working collector, or a full one.</summary>
    public bool IsCorrupting(EntityUid body) =>
        GetCollector(body) is not { } collector || collector.Comp1.Charge >= collector.Comp1.Capacity;

    private void Warn(EntityUid body, string key)
    {
        var now = _timing.CurTime;
        if (_nextWarning.TryGetValue(body, out var next) && now < next)
            return;

        _nextWarning[body] = now + WarningCooldown;
        _popup.PopupEntity(Loc.GetString(key), body, body, PopupType.MediumCaution);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextVent)
            return;

        _nextVent = _timing.CurTime + VentInterval;
        var seconds = (float) VentInterval.TotalSeconds;
        var query = EntityQueryEnumerator<WFRadCollectorComponent, OrganComponent>();
        while (query.MoveNext(out var uid, out var collector, out var organ))
        {
            if (organ.Body != null || collector.Charge <= 0f)
                continue;

            collector.Charge = MathF.Max(0f, collector.Charge - collector.VentPerSecond * seconds);
            Dirty(uid, collector);
        }
    }

    private void OnExamined(Entity<WFRadCollectorComponent> ent, ref ExaminedEvent args)
    {
        var percent = ent.Comp.Capacity > 0f ? (int) MathF.Round(ent.Comp.Charge / ent.Comp.Capacity * 100f) : 0;
        args.PushMarkup(Loc.GetString("wf-rad-collector-examine-charge", ("percent", percent)));
        if (TryComp(ent, out WolfmedOrganComponent? health))
        {
            var integrity = (int) MathF.Round(health.Fraction * 100f);
            args.PushMarkup(Loc.GetString("wf-rad-collector-examine-integrity", ("percent", integrity)));
        }

        if (CompOrNull<OrganComponent>(ent)?.Body == null && ent.Comp.Charge > 0f)
            args.PushMarkup(Loc.GetString("wf-rad-collector-examine-venting"));
    }

    private void OnInteractUsing(Entity<WFRadCollectorComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || CompOrNull<OrganComponent>(ent)?.Body != null ||
            !TryComp(ent, out WolfmedOrganComponent? health) || !_tools.HasQuality(args.Used, "Welding"))
            return;

        if (health.Health >= health.MaxHealth)
        {
            _popup.PopupEntity(Loc.GetString("wf-rad-collector-weld-full"), ent, args.User);
            args.Handled = true;
            return;
        }

        args.Handled = _tools.UseTool(args.Used, args.User, ent, ent.Comp.WeldTime, new[] { "Welding" },
            new WFRadCollectorWeldDoAfterEvent(), fuel: 2f);
    }

    private void OnWeldDone(Entity<WFRadCollectorComponent> ent, ref WFRadCollectorWeldDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !TryComp(ent, out WolfmedOrganComponent? health))
            return;

        args.Handled = true;
        _organs.ChangeHealth((ent, health), FixedPoint2.New(ent.Comp.WeldRepair));
        _popup.PopupEntity(Loc.GetString("wf-rad-collector-welded",
            ("percent", (int) MathF.Round(health.Fraction * 100f))), ent, args.User);
    }
}
