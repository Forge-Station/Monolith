using Content.Server._Mono.Temperature.Components;
using Content.Server._Mono.Temperature.Systems;
using Content.Server._WF.Dormancy;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Chat.Systems;
using Content.Server.Temperature.Components;
using Content.Shared._WF.Warmth;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Chat;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Light.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Temperature;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Warmth;

/// <summary>
/// Reads each body's tile air, body temperature, rain and wind once a second: the HUD's readings, wet clothes, wind
/// chill, and the shivering, sweating and slowdown that warn before the cold or heat does harm.
/// </summary>
public sealed partial class WFWarmthSystem : SharedWFWarmthSystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private WFDormancySystem _dormancy = default!;
    [Dependency] private EntityRadiusHeaterSystem _heaters = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WolfmedBodyTemperatureSystem _bodyTemperature = default!;
    [Dependency] private SharedWolfWeatherSystem _wolfWeather = default!;

    private static readonly ProtoId<EmotePrototype> Shiver = "WFWolfmedShiver";

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    /// <summary>Seconds of full-strength heavy rain that drench a body.</summary>
    private const float RainSoakSeconds = 60f;

    /// <summary>Seconds of full-strength heavy snow that drench a body, as it melts into the clothes.</summary>
    private const float SnowSoakSeconds = 120f;

    /// <summary>How much faster a body dries under cover than in the open.</summary>
    private const float ShelterDrying = 2.5f;

    /// <summary>How much faster a body dries within a lit fire's reach.</summary>
    private const float FireDrying = 6f;

    /// <summary>Kelvin the skin may sit under or over normal before the body feels chilly or warm whatever its core.</summary>
    private const float SkinChill = 8f;
    private const float SkinWarm = 4f;

    /// <summary>Kelvin over the skin's cold damage threshold at which the cold warns whatever the core says.</summary>
    private const float SkinDamageMargin = 3f;

    /// <summary>Shares of the way to the harm line where the body feels the first, mild word.</summary>
    private const float MildShare = 0.2f;

    /// <summary>Shares of the way to the harm line past which the body feels the worst word.</summary>
    private const float SevereShare = 0.8f;

    /// <summary>Under this many moles the tile counts as vacuum and has no temperature.</summary>
    private const float VacuumMoles = 0.5f;

    private readonly List<EntityUid> _due = new();
    private readonly HashSet<Entity<EntityRadiusHeaterComponent>> _fires = new();
    private TimeSpan _nextTick;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFWarmthComponent, ModifyChangedTemperatureEvent>(OnModifyTemperature);
        SubscribeLocalEvent<WFWarmthComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WFWarmthComponent, PlayerAttachedEvent>(OnPlayerAttached);
        InitializeClothing();
    }

    /// <summary>A player taking over a body gets its warmth as it stands, since an unplayed body sends none.</summary>
    private void OnPlayerAttached(Entity<WFWarmthComponent> ent, ref PlayerAttachedEvent args)
    {
        Dirty(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextTick)
            return;

        _nextTick = now + TickInterval;
        _due.Clear();
        var query = EntityQueryEnumerator<TemperatureComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out _, out _))
        {
            _due.Add(uid);
        }

        foreach (var body in _due)
        {
            if (TerminatingOrDeleted(body) || _dormancy.BodySleeps(body))
                continue;

            Tick(body, (float) TickInterval.TotalSeconds);
        }
    }

    /// <summary>One body's second: its readings, wetness, wind and warning signs. Public for tests.</summary>
    public void Tick(EntityUid body, float seconds)
    {
        if (!TryComp(body, out TemperatureComponent? temperature))
            return;

        var comp = EnsureComp<WFWarmthComponent>(body);
        var xform = Transform(body);
        var dirty = false;

        var air = _atmos.GetContainingMixture((body, xform));
        int? here = air is { } mix && mix.TotalMoles > VacuumMoles
            ? (int) MathF.Round(TemperatureHelpers.KelvinToCelsius(mix.Temperature))
            : null;
        if (comp.HereCelsius != here)
        {
            comp.HereCelsius = here;
            dirty = true;
        }

        var open = IsOpen(body, xform, out var wetting, out var wind);
        comp.WindChill = open && wind > 0f
            ? MathF.Min(1f + wind * Cfg.GetCVar(WFWarmthCVars.WindChillPerSpeed),
                MathF.Max(1f, Cfg.GetCVar(WFWarmthCVars.WindChillMax)))
            : 1f;

        dirty |= UpdateWetness(body, comp, xform, open, wetting, seconds);
        dirty |= UpdateFeel(body, comp, temperature);
        dirty |= UpdateGauge(body, comp, xform, seconds);

        // Only a player's client reads this; an NPC's stays on the server.
        if (dirty && HasComp<ActorComponent>(body))
            Dirty(body, comp);
    }

    /// <summary>Wind and wet clothes take heat off a body faster; neither changes how fast it warms.</summary>
    private void OnModifyTemperature(Entity<WFWarmthComponent> ent, ref ModifyChangedTemperatureEvent args)
    {
        if (args.TemperatureDelta >= 0f)
            return;

        args.TemperatureDelta *= ent.Comp.WindChill * (1f + MathF.Max(0f, Cfg.GetCVar(WFWarmthCVars.WetCooling)) * ent.Comp.Wetness);
    }

    private void OnExamined(Entity<WFWarmthComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || ent.Comp.Wetness < 0.2f)
            return;

        var id = ent.Comp.Wetness >= 0.6f ? "wf-warmth-examine-soaked" : "wf-warmth-examine-damp";
        args.PushMarkup(Loc.GetString(id, ("target", Identity.Entity(ent, EntityManager))));
    }

    /// <summary>
    /// Whether the body stands in the open under a WolfWeather sky, and if so how fast the weather soaks it (a share a
    /// second) and how hard the wind blows (tiles a second).
    /// </summary>
    public bool IsOpen(EntityUid body, TransformComponent xform, out float wetting, out float wind)
    {
        wetting = 0f;
        wind = 0f;
        if (!TryComp(xform.MapUid, out WolfWeatherMapComponent? weather) ||
            _container.IsEntityOrParentInContainer(body) ||
            xform.GridUid is not { } gridUid ||
            !TryComp(gridUid, out MapGridComponent? grid))
            return false;

        TryComp(gridUid, out RoofComponent? roof);
        if (!_wolfWeather.IsExposed((gridUid, grid, roof), _map.GetTileRef(gridUid, grid, xform.Coordinates)))
            return false;

        var now = _timing.CurTime;
        var weathers = weather.Weathers;
        var mapWind = weather.Wind;
        foreach (var entry in weathers)
        {
            if (!_proto.TryIndex(entry.Weather, out var proto))
                continue;

            var strength = SharedWolfWeatherSystem.GetStrength(entry, proto, now);
            var soak = MathF.Max(proto.GroundWetness / RainSoakSeconds, MathF.Min(proto.Snowfall, 1f) / SnowSoakSeconds);
            wetting = MathF.Max(wetting, soak * strength);
        }

        if (SharedWolfWeatherSystem.GetCurrent(weather) is { } current &&
            _proto.TryIndex(current.Weather, out var currentProto))
            wind = mapWind.Length() * SharedWolfWeatherSystem.GetStrength(current, currentProto, now);

        return true;
    }

    /// <summary>Soaks the body in the open while it rains or snows; otherwise dries it, faster under cover or by a fire.</summary>
    private bool UpdateWetness(EntityUid body, WFWarmthComponent comp, TransformComponent xform, bool open,
        float wetting, float seconds)
    {
        var before = comp.Wetness;
        if (wetting > 0f)
        {
            comp.Wetness = MathF.Min(1f, comp.Wetness + wetting * seconds);
        }
        else if (comp.Wetness > 0f)
        {
            var drySeconds = Cfg.GetCVar(WFWarmthCVars.DrySeconds);
            var rate = drySeconds > 0f ? 1f / drySeconds : 1f;
            if (!open)
                rate *= ShelterDrying;
            if (NearFire(xform))
                rate *= FireDrying;

            comp.Wetness = MathF.Max(0f, comp.Wetness - rate * seconds);
        }

        return Quantize(before) != Quantize(comp.Wetness);
    }

    /// <summary>Whether a lit fire, or another active radiant heater, reaches the body.</summary>
    public bool NearFire(TransformComponent xform)
    {
        _fires.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, EntityRadiusHeaterSystem.MaxRadius, _fires);
        foreach (var fire in _fires)
        {
            if (!_heaters.IsActive(fire.Owner, fire.Comp))
                continue;

            if (xform.Coordinates.TryDistance(EntityManager, Transform(fire.Owner).Coordinates, out var distance) &&
                distance < fire.Comp.Radius)
                return true;
        }

        return false;
    }

    /// <summary>The body feel, and the warning signs once the cold or heat passes the warning line.</summary>
    private bool UpdateFeel(EntityUid body, WFWarmthComponent comp, TemperatureComponent temperature)
    {
        var (cold, heat, normal) = Strain(body, temperature);
        var surface = temperature.CurrentTemperature;
        var warn = Math.Clamp(Cfg.GetCVar(WFWarmthCVars.WarningShare), 0f, 0.99f);

        // The skin takes damage on its own, long before a slow core moves: warn as it nears either damage line.
        var coldLine = temperature.ParentColdDamageThreshold ?? temperature.ColdDamageThreshold;
        var heatLine = temperature.ParentHeatDamageThreshold ?? temperature.HeatDamageThreshold;
        if (surface <= coldLine + SkinDamageMargin)
            cold = MathF.Max(cold, warn);
        if (heatLine > normal)
            heat = MathF.Max(heat, (surface - normal) / (heatLine - normal));

        comp.ColdShare = cold;
        comp.HeatShare = heat;

        var feel = WFBodyFeel.Comfortable;
        if (cold >= SevereShare)
            feel = WFBodyFeel.Freezing;
        else if (cold >= warn)
            feel = WFBodyFeel.Cold;
        else if (heat >= SevereShare)
            feel = WFBodyFeel.Overheating;
        else if (heat >= warn)
            feel = WFBodyFeel.Hot;
        else if (cold >= MildShare || surface < normal - SkinChill)
            feel = WFBodyFeel.Chilly;
        else if (heat >= MildShare || surface > normal + SkinWarm)
            feel = WFBodyFeel.Warm;

        var strain = WFWarmthStrain.None;
        var severity = 0f;
        if (!_mobState.IsDead(body))
        {
            if (cold >= warn)
            {
                strain = WFWarmthStrain.Cold;
                severity = Math.Clamp((cold - warn) / (1f - warn), 0f, 1f);
            }
            else if (heat >= warn)
            {
                strain = WFWarmthStrain.Hot;
                severity = Math.Clamp((heat - warn) / (1f - warn), 0f, 1f);
            }
        }

        var dirty = false;
        if (comp.Feel != feel)
        {
            comp.Feel = feel;
            dirty = true;
        }

        if (comp.Strain != strain)
        {
            var started = comp.Strain == WFWarmthStrain.None;
            comp.Strain = strain;
            dirty = true;
            Movement.RefreshMovementSpeedModifiers(body);
            if (strain != WFWarmthStrain.None && started)
                FirstWarning(body, comp, strain);
        }

        if (Quantize(comp.Severity) != Quantize(severity))
            dirty = true;
        comp.Severity = severity;

        if (strain != WFWarmthStrain.None)
            Signs(body, comp, strain);

        return dirty;
    }

    /// <summary>
    /// How far the body is from normal toward harm, 0 at normal and 1 at the line where harm starts, for cold and for
    /// heat. A wound host reads its core against Wolfmed's hypothermia and heat exhaustion lines; any other body reads
    /// its surface against its damage thresholds.
    /// </summary>
    public (float Cold, float Heat, float Normal) Strain(EntityUid body, TemperatureComponent temperature)
    {
        if (_bodyTemperature.GetCore(body) is { } core && _bodyTemperature.GetLines(body) is { } lines)
        {
            var coldSpan = lines.Normal - lines.ColdDown;
            var heatSpan = lines.HeatDown - lines.Normal;
            var cold = coldSpan > 0f && !float.IsInfinity(coldSpan) ? (lines.Normal - core) / coldSpan : 0f;
            var heat = heatSpan > 0f && !float.IsInfinity(heatSpan) ? (core - lines.Normal) / heatSpan : 0f;
            return (MathF.Max(0f, cold), MathF.Max(0f, heat), lines.Normal);
        }

        var coldLine = temperature.ParentColdDamageThreshold ?? temperature.ColdDamageThreshold;
        var heatLine = temperature.ParentHeatDamageThreshold ?? temperature.HeatDamageThreshold;
        var normal = TryComp(body, out ThermalRegulatorComponent? regulator) &&
                     regulator.NormalBodyTemperature > coldLine && regulator.NormalBodyTemperature < heatLine
            ? regulator.NormalBodyTemperature
            : (coldLine + heatLine) / 2f;
        var surface = temperature.CurrentTemperature;
        var surfaceCold = normal > coldLine ? (normal - surface) / (normal - coldLine) : 0f;
        var surfaceHeat = heatLine > normal ? (surface - normal) / (heatLine - normal) : 0f;
        return (MathF.Max(0f, surfaceCold), MathF.Max(0f, surfaceHeat), normal);
    }

    private void FirstWarning(EntityUid body, WFWarmthComponent comp, WFWarmthStrain strain)
    {
        if (!HasComp<ActorComponent>(body))
            return;

        var id = strain == WFWarmthStrain.Cold ? "wf-warmth-cold-start" : "wf-warmth-heat-start";
        _popup.PopupEntity(Loc.GetString(id), body, body, PopupType.MediumCaution);
        comp.NextSign = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(15f, 25f));
    }

    /// <summary>Shivering or sweating: a message to the body now and then, and a sign others can see less often.</summary>
    private void Signs(EntityUid body, WFWarmthComponent comp, WFWarmthStrain strain)
    {
        if (!HasComp<ActorComponent>(body))
            return;

        var now = _timing.CurTime;
        if (now >= comp.NextSign)
        {
            comp.NextSign = now + TimeSpan.FromSeconds(_random.NextFloat(20f, 35f));
            var id = strain == WFWarmthStrain.Cold
                ? _random.Pick(ColdSigns)
                : _random.Pick(HeatSigns);
            _popup.PopupEntity(Loc.GetString(id), body, body, PopupType.SmallCaution);
        }

        if (now < comp.NextVisibleSign)
            return;

        comp.NextVisibleSign = now + TimeSpan.FromSeconds(_random.NextFloat(45f, 75f));
        if (strain == WFWarmthStrain.Cold)
        {
            _chat.TryEmoteWithChat(body, Shiver, ChatTransmitRange.Normal, ignoreActionBlocker: true, forceEmote: true);
            return;
        }

        _popup.PopupEntity(Loc.GetString("wf-warmth-sweat-others", ("target", Identity.Entity(body, EntityManager))),
            body, Filter.PvsExcept(body), true);
    }

    private static readonly string[] ColdSigns =
        { "wf-warmth-cold-sign-1", "wf-warmth-cold-sign-2", "wf-warmth-cold-sign-3" };

    private static readonly string[] HeatSigns =
        { "wf-warmth-heat-sign-1", "wf-warmth-heat-sign-2", "wf-warmth-heat-sign-3" };

    /// <summary>A share rounded to the 5% steps the client is told about.</summary>
    private static int Quantize(float share) => (int) MathF.Round(share * 20f);
}
