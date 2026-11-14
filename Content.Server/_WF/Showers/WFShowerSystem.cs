using System.Linq;
using Content.Server._WF.BloodTrail;
using Content.Server._WF.Cleaning;
using Content.Server.Administration.Logs;
using Content.Server.Decals;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Forensics;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._WF.Showers;
using Content.Shared.Audio;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Showers;

/// <summary>
/// Runs showers: toggling, tool adjustments, the tank refill, and the spray that washes whatever stands under it.
/// Washing a chemical burn, cooling an overheating limb and putting out a fire come from the reagents' own touch
/// reactions; this adds body temperature, stamina and the scrub.
/// </summary>
public sealed partial class WFShowerSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private WFDecalCleaningSystem _cleaning = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private DecalSystem _decal = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PuddleSystem _puddle = default!;
    [Dependency] private ReactiveSystem _reactive = default!;
    [Dependency] private Content.Shared._WF.ArmorSpecialization.WFCivilianSpecializationSystem _wfSpecs = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private StaminaSystem _stamina = default!;
    [Dependency] private TemperatureSystem _temperature = default!;
    [Dependency] private SharedToolSystem _tool = default!;

    private static readonly ProtoId<DamageTypePrototype> Heat = "Heat";
    private const string ScrewingQuality = "Screwing";
    private const string PulsingQuality = "Pulsing";
    private const string PryingQuality = "Prying";

    private readonly HashSet<EntityUid> _targets = new();
    private readonly HashSet<EntityUid> _mobs = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFShowerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFShowerComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<WFShowerComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WFShowerComponent, WFShowerTemperatureDoAfterEvent>(OnTemperatureDoAfter);
        SubscribeLocalEvent<WFShowerComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(Entity<WFShowerComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextTick = _timing.CurTime + ent.Comp.TickInterval;
        UpdateAppearance(ent);
    }

    private void OnActivate(Entity<WFShowerComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        var on = !ent.Comp.IntendedOn;
        if (!TrySetOn(ent, on))
        {
            _popup.PopupEntity(Loc.GetString("wf-shower-dry", ("shower", ent)), ent, args.User);
            return;
        }

        _popup.PopupEntity(Loc.GetString(on ? "wf-shower-turned-on" : "wf-shower-turned-off"), ent, args.User);
    }

    /// <summary>Turns the shower on or off as a user would. Returns false when it is too dry to start.</summary>
    public bool TrySetOn(Entity<WFShowerComponent> ent, bool on)
    {
        ent.Comp.IntendedOn = on;
        SetActuallyOn(ent, on);
        if (on && !ent.Comp.ActuallyOn)
        {
            ent.Comp.IntendedOn = false;
            return false;
        }

        return true;
    }

    private void OnInteractUsing(Entity<WFShowerComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (_tool.HasQuality(args.Used, ScrewingQuality))
        {
            args.Handled = _tool.UseTool(args.Used, args.User, ent, (float) ent.Comp.TemperatureAdjustDelay.TotalSeconds,
                ScrewingQuality, new WFShowerTemperatureDoAfterEvent());
            if (args.Handled)
                _popup.PopupEntity(Loc.GetString("wf-shower-adjust-start"), ent, args.User);
            return;
        }

        if (_tool.HasQuality(args.Used, PulsingQuality))
        {
            args.Handled = true;
            ent.Comp.Mode = (WFShowerMode) (((int) ent.Comp.Mode + 1) % 3);
            PlayToolSound(args.Used, args.User);
            _popup.PopupEntity(Loc.GetString("wf-shower-mode-set", ("mode", ModeName(ent.Comp.Mode))), ent, args.User);
            return;
        }

        if (_tool.HasQuality(args.Used, PryingQuality))
        {
            args.Handled = true;
            ent.Comp.Reclaimer = !ent.Comp.Reclaimer;
            PlayToolSound(args.Used, args.User);
            _popup.PopupEntity(Loc.GetString(ent.Comp.Reclaimer ? "wf-shower-reclaimer-on" : "wf-shower-reclaimer-off"), ent, args.User);
        }
    }

    private void OnTemperatureDoAfter(Entity<WFShowerComponent> ent, ref WFShowerTemperatureDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;
        ent.Comp.Temperature = ent.Comp.Temperature switch
        {
            WFShowerTemperature.Normal => WFShowerTemperature.Freezing,
            WFShowerTemperature.Freezing => WFShowerTemperature.Boiling,
            _ => WFShowerTemperature.Normal,
        };
        ent.Comp.Showering.Clear();
        ScheduleMist(ent);
        _popup.PopupEntity(Loc.GetString("wf-shower-adjusted", ("temperature", TemperatureName(ent.Comp.Temperature))), ent, args.User);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(args.User):user} set {ToPrettyString(ent):shower} to {ent.Comp.Temperature} water");
    }

    private void OnExamined(Entity<WFShowerComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(WFShowerComponent)))
        {
            args.PushMarkup(Loc.GetString("wf-shower-examine-temperature", ("temperature", TemperatureName(ent.Comp.Temperature))));
            args.PushMarkup(Loc.GetString("wf-shower-examine-mode", ("mode", ModeName(ent.Comp.Mode))));
            if (ent.Comp.Reclaimer)
                args.PushMarkup(Loc.GetString("wf-shower-examine-reclaimer"));

            if (_solution.TryGetSolution(ent.Owner, ent.Comp.Solution, out _, out var tank))
                args.PushMarkup(Loc.GetString("wf-shower-examine-volume", ("volume", tank.Volume), ("max", tank.MaxVolume)));
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFShowerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.MistChangeAt is { } mistAt && now >= mistAt)
            {
                comp.MistChangeAt = null;
                comp.Mist = MistWanted(comp);
                UpdateAppearance((uid, comp));
            }

            if (now < comp.NextTick)
                continue;

            comp.NextTick = now + comp.TickInterval;
            Tick((uid, comp), now);
        }
    }

    private void Tick(Entity<WFShowerComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        if (!_solution.TryGetSolution(ent.Owner, comp.Solution, out var tankEnt, out var tank))
            return;

        if (comp.IntendedOn && comp.Mode == WFShowerMode.Timed && now >= comp.TimedEnd)
            comp.IntendedOn = false;

        if (comp.ActuallyOn && tank.Volume < comp.SprayVolume)
        {
            SetActuallyOn(ent, false);
            if (comp.Mode != WFShowerMode.Forever)
                comp.IntendedOn = false;
        }
        else
        {
            SetActuallyOn(ent, comp.IntendedOn);
        }

        if (!comp.ActuallyOn)
        {
            if (comp.Reclaimer && tank.AvailableVolume > 0)
                _solution.TryAddReagent(tankEnt.Value, comp.RefillReagent, comp.RefillRate * (float) comp.TickInterval.TotalSeconds);
            return;
        }

        var clean = tank.Volume > 0 &&
                    tank.GetTotalPrototypeQuantity(comp.RefillReagent) >= tank.Volume * comp.CleanFraction;
        var spray = _solution.SplitSolution(tankEnt.Value, comp.SprayVolume);
        UpdateAppearance(ent);

        var exposure = spray.Clone();
        exposure.ScaleSolution(comp.ExposureMultiplier);
        Spray(ent, exposure, clean);
    }

    private void Spray(Entity<WFShowerComponent> ent, Solution exposure, bool clean)
    {
        var xform = Transform(ent);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var tile = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        _targets.Clear();
        _lookup.GetLocalEntitiesIntersecting(gridUid, tile, _targets,
            flags: LookupFlags.Dynamic | LookupFlags.Sundries | LookupFlags.Uncontained, gridComp: grid);
        _targets.Remove(ent);

        _mobs.Clear();
        foreach (var target in _targets)
        {
            if (TerminatingOrDeleted(target))
                continue;

            // A decon coating is meant to be washed down, so the shower reaches through it.
            _wfSpecs.BypassDecon(() => _reactive.DoEntityReaction(target, exposure, ReactionMethod.Touch));
            if (clean)
                Scrub(target);

            if (!HasComp<MobStateComponent>(target))
                continue;

            _mobs.Add(target);
            Soak(ent, target, clean);
        }

        ent.Comp.Showering.IntersectWith(_mobs);

        if (clean)
            CleanFloor(gridUid, _map.GridTileToLocal(gridUid, grid, tile).Position);
    }

    /// <summary>Applies the water temperature, scalding and the stamina recovery to one mob.</summary>
    private void Soak(Entity<WFShowerComponent> ent, EntityUid mob, bool clean)
    {
        var comp = ent.Comp;
        if (comp.Showering.Add(mob))
        {
            var feel = comp.Temperature switch
            {
                WFShowerTemperature.Freezing => "wf-shower-feel-freezing",
                WFShowerTemperature.Boiling => "wf-shower-feel-boiling",
                _ => clean ? "wf-shower-feel-normal" : "wf-shower-feel-dirty",
            };
            _popup.PopupEntity(Loc.GetString(feel), mob, mob,
                comp.Temperature == WFShowerTemperature.Normal ? PopupType.Small : PopupType.MediumCaution);
        }

        if (TryComp<TemperatureComponent>(mob, out var temp))
        {
            var target = comp.Temperature switch
            {
                WFShowerTemperature.Freezing => comp.FreezingTemperature,
                WFShowerTemperature.Boiling => comp.BoilingTemperature,
                _ => comp.NormalTemperature,
            };
            var heat = (target - temp.CurrentTemperature) * _temperature.GetHeatCapacity(mob, temp) * comp.HeatTransfer;
            _temperature.ChangeHeat(mob, heat, false, temp);
        }

        if (comp.Temperature == WFShowerTemperature.Boiling)
        {
            var damage = new DamageSpecifier(_proto.Index(Heat), comp.ScaldDamage);
            _damage.TryChangeDamage(mob, damage, origin: ent);
            return;
        }

        if (clean && TryComp<StaminaComponent>(mob, out var stamina) && !stamina.Critical && stamina.StaminaDamage > 0)
            _stamina.TakeStaminaDamage(mob, -comp.StaminaRecovery, stamina, visual: false);
    }

    /// <summary>Washes forensics and footprint stains off an entity and everything it wears or holds.</summary>
    private void Scrub(EntityUid target)
    {
        ClearForensics(target);
        foreach (var item in _inventory.GetHandOrInventoryEntities(target))
            ClearForensics(item);

        if (TryComp<FootPrintsComponent>(target, out var prints))
            prints.PrintsColor = Color.Transparent;
    }

    private void ClearForensics(EntityUid uid)
    {
        if (!TryComp<ForensicsComponent>(uid, out var forensics))
            return;

        forensics.Fingerprints.Clear();
        forensics.Fibers.Clear();
        forensics.Residues.Clear();
        if (forensics.CanDnaBeCleaned)
            forensics.DNAs.Clear();
    }

    private void CleanFloor(EntityUid gridUid, System.Numerics.Vector2 center)
    {
        foreach (var (id, _) in _decal.GetDecalsInRange(gridUid, center, 0.5f, _cleaning.IsCleanable).ToArray())
        {
            _decal.RemoveDecal(gridUid, id);
        }
    }

    private void PlayToolSound(EntityUid tool, EntityUid user)
    {
        if (TryComp<ToolComponent>(tool, out var toolComp))
            _tool.PlayToolSound(tool, toolComp, user);
    }

    /// <summary>Starts or stops the spray. Returns false when nothing changed or the tank is too low to start.</summary>
    private bool SetActuallyOn(Entity<WFShowerComponent> ent, bool on)
    {
        var comp = ent.Comp;
        if (on == comp.ActuallyOn)
            return false;

        if (!_solution.TryGetSolution(ent.Owner, comp.Solution, out var tankEnt, out var tank))
            return false;

        if (on && tank.Volume < comp.SprayVolume)
            return false;

        comp.ActuallyOn = on;
        _ambient.SetAmbience(ent, on);
        if (on)
        {
            comp.TimedEnd = _timing.CurTime + comp.TimedLength;
            comp.NextTick = _timing.CurTime;
            _audio.PlayPvs(comp.StartSound, ent);
        }
        else
        {
            comp.Showering.Clear();
            _audio.PlayPvs(comp.EndSound, ent);
            if (tank.Volume > 0)
            {
                var runoff = _solution.SplitSolution(tankEnt.Value, comp.RunoffVolume);
                _puddle.TrySpillAt(ent, runoff, out _, sound: false);
            }
        }

        ScheduleMist(ent);
        UpdateAppearance(ent);
        return true;
    }

    private bool MistWanted(WFShowerComponent comp)
    {
        return comp.ActuallyOn && comp.Temperature != WFShowerTemperature.Freezing;
    }

    private void ScheduleMist(Entity<WFShowerComponent> ent)
    {
        var wanted = MistWanted(ent.Comp);
        if (wanted == ent.Comp.Mist)
        {
            ent.Comp.MistChangeAt = null;
            return;
        }

        ent.Comp.MistChangeAt ??= _timing.CurTime + (wanted ? ent.Comp.MistDelay : ent.Comp.MistClearDelay);
    }

    private void UpdateAppearance(Entity<WFShowerComponent> ent)
    {
        _appearance.SetData(ent, WFShowerVisuals.On, ent.Comp.ActuallyOn);
        _appearance.SetData(ent, WFShowerVisuals.Mist, ent.Comp.Mist);
        if (_solution.TryGetSolution(ent.Owner, ent.Comp.Solution, out _, out var tank) && tank.Volume > 0)
            _appearance.SetData(ent, WFShowerVisuals.Color, tank.GetColor(_proto));
    }

    private string TemperatureName(WFShowerTemperature temperature)
    {
        return Loc.GetString($"wf-shower-temperature-{temperature.ToString().ToLowerInvariant()}");
    }

    private string ModeName(WFShowerMode mode)
    {
        return Loc.GetString(mode switch
        {
            WFShowerMode.Timed => "wf-shower-mode-timed",
            WFShowerMode.Forever => "wf-shower-mode-forever",
            _ => "wf-shower-mode-until-empty",
        });
    }
}
