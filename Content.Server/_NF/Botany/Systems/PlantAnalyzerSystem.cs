using Content.Server.Botany.Components;
using Content.Shared._NF.PlantAnalyzer;
using Content.Shared.Atmos;
using Content.Shared.CartridgeLoader;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Server.Botany.Systems;

public sealed partial class PlantAnalyzerSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly Content.Server.Labels.LabelSystem _label = default!;
    [Dependency] private readonly Content.Server.Popups.PopupSystem _popup = default!;

    private TimeSpan _nextUpdate = TimeSpan.Zero;
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlantAnalyzerComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<PlantAnalyzerComponent, PlantAnalyzerDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<PlantAnalyzerComponent, EntGotInsertedIntoContainerMessage>(OnInsertedIntoContainer);
        SubscribeLocalEvent<PlantAnalyzerComponent, ItemToggledEvent>(OnToggled);
        SubscribeLocalEvent<PlantAnalyzerComponent, DroppedEvent>(OnDropped);
        Subs.BuiEvents<PlantAnalyzerComponent>(PlantAnalyzerUiKey.Key, subs =>
        {
            subs.Event<BoundUIClosedEvent>(OnPlantAnalyzerUiClosed);
            subs.Event<PlantAnalyzerPrintLabelMessage>(OnPrintLabel);
        });
    }

    private void OnAfterInteract(Entity<PlantAnalyzerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Target == null || !args.CanReach || args.Handled)
            return;

        if (ent.Comp.DoAfter != null)
            return;

        if (HasComp<SeedComponent>(args.Target) ||
            TryComp<PlantHolderComponent>(args.Target, out var plantHolder) && plantHolder.Seed != null)
        {
            args.Handled = true;
            var doAfterArgs = new DoAfterArgs(EntityManager, args.User, ent.Comp.Settings.ScanDelay,
                new PlantAnalyzerDoAfterEvent(), ent, target: args.Target, used: ent)
            {
                NeedHand = true,
                BreakOnDamage = true,
                BreakOnMove = true,
                MovementThreshold = 0.01f
            };

            _doAfterSystem.TryStartDoAfter(doAfterArgs, out ent.Comp.DoAfter);
        }
    }

    private void OnDoAfter(Entity<PlantAnalyzerComponent> ent, ref PlantAnalyzerDoAfterEvent args)
    {
        ent.Comp.DoAfter = null;

        if (args.Handled || args.Cancelled || args.Args.Target == null)
            return;

        _audio.PlayPvs(ent.Comp.ScanningEndSound, ent);

        ent.Comp.ScannedEntity = args.Args.Target.Value;
        ent.Comp.User = args.User;
        _nextUpdate = TimeSpan.Zero;

        TrySetToggle(ent.Owner, true);
        OpenUserInterface(args.User, ent);
        UpdateScannedUser(ent, args.Args.Target.Value);

        args.Handled = true;
    }

    private void OnPrintLabel(Entity<PlantAnalyzerComponent> ent, ref PlantAnalyzerPrintLabelMessage args)
    {
        if (ent.Comp.ScannedEntity is not { } target || TerminatingOrDeleted(target))
        {
            _popup.PopupEntity(Loc.GetString("plant-analyzer-print-no-target"), ent, args.Actor);
            return;
        }

        if (!TryComp<SeedComponent>(target, out var seedComp))
        {
            _popup.PopupEntity(Loc.GetString("plant-analyzer-print-not-packet"), ent, args.Actor);
            return;
        }

        SeedData? seed = null;
        if (seedComp.Seed != null)
            seed = seedComp.Seed;
        else if (seedComp.SeedId != null && _prototypeManager.TryIndex(seedComp.SeedId, out SeedPrototype? proto))
            seed = proto;

        if (seed == null)
        {
            _popup.PopupEntity(Loc.GetString("plant-analyzer-print-no-target"), ent, args.Actor);
            return;
        }

        var label = BuildSeedLabel(seed);
        _label.Label(target, label);
        _popup.PopupEntity(Loc.GetString("plant-analyzer-print-ok", ("label", label)), ent, args.Actor);
    }

    private static string BuildSeedLabel(SeedData seed)
    {
        var name = Robust.Shared.Localization.Loc.GetString(seed.Name);
        var bits = new List<string> { $"P{seed.Potency:0}" };
        if (seed.Radioactive)
            bits.Add("rad");
        if (seed.CarnivorousGrab)
            bits.Add("grab");
        if (seed.Ligneous)
            bits.Add("wood");
        foreach (var chem in seed.Chemicals.Keys.Take(2))
            bits.Add(chem);

        var label = $"{name} {string.Join(" ", bits)}";
        return label.Length <= 48 ? label : label[..48];
    }

    private void OnPlantAnalyzerUiClosed(EntityUid uid, PlantAnalyzerComponent comp, BoundUIClosedEvent args)
    {
        if (!args.UiKey.Equals(PlantAnalyzerUiKey.Key))
            return;

        if (!_uiSystem.IsUiOpen(uid, PlantAnalyzerUiKey.Key))
            TrySetToggle(uid, false);
    }

    private void OnInsertedIntoContainer(Entity<PlantAnalyzerComponent> uid, ref EntGotInsertedIntoContainerMessage args)
    {
        // Keep the window open while the analyzer stays on the scanning user (inactive hand or pocket).
        if (uid.Comp.User is { } user && !TerminatingOrDeleted(user) && IsCarriedBy(uid.Owner, user))
            return;

        if (uid.Comp.ScannedEntity != null)
            StopAnalyzingEntity(uid);
    }

    private void OnDropped(Entity<PlantAnalyzerComponent> uid, ref DroppedEvent args)
    {
        if (uid.Comp.ScannedEntity != null)
            StopAnalyzingEntity(uid);
    }

    private void OnToggled(Entity<PlantAnalyzerComponent> uid, ref ItemToggledEvent args)
    {
        // PDAs use ItemToggle for the flashlight; never treat that as ending a plant scan.
        if (HasComp<CartridgeLoaderComponent>(uid.Owner))
            return;

        if (!args.Activated && uid.Comp.ScannedEntity != null)
            StopAnalyzingEntity(uid);
    }

    private void OpenUserInterface(EntityUid user, EntityUid analyzer)
    {
        if (!_uiSystem.HasUi(analyzer, PlantAnalyzerUiKey.Key))
            return;

        _uiSystem.OpenUi(analyzer, PlantAnalyzerUiKey.Key, user);
    }

    private void StopAnalyzingEntity(Entity<PlantAnalyzerComponent> ent)
    {
        ent.Comp.ScannedEntity = null;
        ent.Comp.User = null;
        _uiSystem.CloseUi(ent.Owner, PlantAnalyzerUiKey.Key);
        TrySetToggle(ent.Owner, false);
    }

    private void TrySetToggle(EntityUid uid, bool on)
    {
        if (HasComp<CartridgeLoaderComponent>(uid) || !HasComp<ItemToggleComponent>(uid))
            return;

        if (on)
            _toggle.TryActivate(uid);
        else
            _toggle.TryDeactivate(uid);
    }

    private bool IsCarriedBy(EntityUid item, EntityUid user)
    {
        var current = item;
        var hops = 0;
        while (_container.TryGetContainingContainer(current, out var container) && hops++ < 8)
        {
            current = container.Owner;
            if (current == user)
                return true;
        }

        return false;
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;

        var query = EntityQueryEnumerator<PlantAnalyzerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var analyzerXform))
        {
            if (comp.ScannedEntity is not { } target)
                continue;

            if (TerminatingOrDeleted(target))
            {
                StopAnalyzingEntity((uid, comp));
                continue;
            }

            if (comp.User is { } user && !TerminatingOrDeleted(user) && !IsCarriedBy(uid, user))
            {
                StopAnalyzingEntity((uid, comp));
                continue;
            }

            if (!HasComp<SeedComponent>(target) && !HasComp<PlantHolderComponent>(target) ||
                !_transformSystem.InRange(Transform(target).Coordinates, analyzerXform.Coordinates, comp.MaxScanRange))
            {
                StopAnalyzingEntity((uid, comp));
                continue;
            }

            UpdateScannedUser((uid, comp), target);
        }
    }

    public void UpdateScannedUser(Entity<PlantAnalyzerComponent> ent, EntityUid target)
    {
        if (!_uiSystem.HasUi(ent, PlantAnalyzerUiKey.Key))
            return;

        if (TryComp<SeedComponent>(target, out var seedComp))
        {
            if (seedComp.Seed != null)
            {
                var state = ObtainingGeneDataSeed(seedComp.Seed, target, false, true);
                _uiSystem.ServerSendUiMessage(ent.Owner, PlantAnalyzerUiKey.Key, state);
            }
            else if (seedComp.SeedId != null && _prototypeManager.TryIndex(seedComp.SeedId, out SeedPrototype? protoSeed))
            {
                var state = ObtainingGeneDataSeed(protoSeed, target, false, true);
                _uiSystem.ServerSendUiMessage(ent.Owner, PlantAnalyzerUiKey.Key, state);
            }
        }
        else if (TryComp<PlantHolderComponent>(target, out var plantComp))
        {
            if (plantComp.Seed != null)
            {
                var state = ObtainingGeneDataSeed(plantComp.Seed, target, true, true);
                ApplyTrayStatus(state, plantComp);
                _uiSystem.ServerSendUiMessage(ent.Owner, PlantAnalyzerUiKey.Key, state);
            }
        }
    }

    private static void ApplyTrayStatus(PlantAnalyzerScannedSeedPlantInformation ret, PlantHolderComponent plantComp)
    {
        ret.HasPlant = plantComp.Seed != null;
        ret.Dead = plantComp.Dead;
        ret.HarvestReady = plantComp.Harvest;
        ret.TrayHealth = plantComp.Health;
        ret.TrayEndurance = plantComp.Seed?.Endurance ?? 0f;
        ret.WaterLevel = plantComp.WaterLevel;
        ret.NutritionLevel = plantComp.NutritionLevel;
        ret.Toxins = plantComp.Toxins;
        ret.WeedLevel = plantComp.WeedLevel;
        ret.PestLevel = plantComp.PestLevel;
        ret.Age = plantComp.Age;
        ret.ImproperHeat = plantComp.ImproperHeat;
        ret.ImproperPressure = plantComp.ImproperPressure;
        ret.ImproperLight = plantComp.ImproperLight;
        ret.MissingGas = plantComp.MissingGas > 0;
        ret.LightMode = plantComp.LightMode;
    }

    /// <summary>
    ///     Analysis of seed from prototype.
    /// </summary>
    public PlantAnalyzerScannedSeedPlantInformation ObtainingGeneDataSeed(
        SeedData seedData,
        EntityUid target,
        bool isTray,
        bool includeAdvancedData)
    {
        AnalyzerHarvestType harvestType = AnalyzerHarvestType.Unknown;
        switch (seedData.HarvestRepeat)
        {
            case HarvestType.Repeat:
                harvestType = AnalyzerHarvestType.Repeat;
                break;
            case HarvestType.NoRepeat:
                harvestType = AnalyzerHarvestType.NoRepeat;
                break;
            case HarvestType.SelfHarvest:
                harvestType = AnalyzerHarvestType.SelfHarvest;
                break;
        }

        var mutationProtos = seedData.MutationPrototypes;
        List<string> mutationStrings = new();
        foreach (var mutationProto in mutationProtos)
        {
            if (_prototypeManager.TryIndex<SeedPrototype>(mutationProto, out var seed))
                mutationStrings.Add(seed.DisplayName);
        }

        var chemStrings = seedData.Chemicals
            .Select(c => $"{c.Key} ({c.Value.Min}-{c.Value.Max})")
            .ToArray();

        var mutationNames = seedData.Mutations
            .Select(m => m.Name)
            .Distinct()
            .ToArray();

        return new PlantAnalyzerScannedSeedPlantInformation
        {
            TargetEntity = GetNetEntity(target),
            IsTray = isTray,
            HasPlant = true,
            SeedName = seedData.DisplayName,
            SeedChem = chemStrings,
            MutationNames = mutationNames,
            HarvestType = harvestType,
            ExudeGases = GetGasFlags(seedData.ExudeGasses.Keys),
            ConsumeGases = GetGasFlags(seedData.ConsumeGasses.Keys),
            Endurance = seedData.Endurance,
            SeedYield = seedData.Yield,
            Lifespan = seedData.Lifespan,
            Maturation = seedData.Maturation,
            Production = seedData.Production,
            GrowthStages = seedData.GrowthStages,
            SeedPotency = seedData.Potency,
            Speciation = mutationStrings.ToArray(),
            NutrientConsumption = seedData.NutrientConsumption,
            WaterConsumption = seedData.WaterConsumption,
            IdealHeat = seedData.IdealHeat,
            HeatTolerance = seedData.HeatTolerance,
            IdealLight = seedData.IdealLight,
            LightTolerance = seedData.LightTolerance,
            ToxinsTolerance = seedData.ToxinsTolerance,
            LowPressureTolerance = seedData.LowPressureTolerance,
            HighPressureTolerance = seedData.HighPressureTolerance,
            PestTolerance = seedData.PestTolerance,
            WeedTolerance = seedData.WeedTolerance,
            Mutations = GetMutationFlags(seedData)
        };
    }

    public MutationFlags GetMutationFlags(SeedData plant)
    {
        MutationFlags ret = MutationFlags.None;
        if (plant.TurnIntoKudzu)
            ret |= MutationFlags.TurnIntoKudzu;
        if (plant.Seedless || plant.PermanentlySeedless)
            ret |= MutationFlags.Seedless;
        if (plant.Ligneous)
            ret |= MutationFlags.Ligneous;
        if (plant.CanScream)
            ret |= MutationFlags.CanScream;
        if (!plant.Viable)
            ret |= MutationFlags.Unviable;
        if (plant.Radioactive)
            ret |= MutationFlags.Radioactive;
        if (plant.CarnivorousGrab)
            ret |= MutationFlags.CarnivorousGrab;
        if (plant.CarnivorousPestEater)
            ret |= MutationFlags.CarnivorousPestEater;
        if (plant.GeneLocked)
            ret |= MutationFlags.GeneLocked;

        return ret;
    }

    public GasFlags GetGasFlags(IEnumerable<Gas> gases)
    {
        var gasFlags = GasFlags.None;
        foreach (var gas in gases)
        {
            gasFlags |= gas switch
            {
                Gas.Nitrogen => GasFlags.Nitrogen,
                Gas.Oxygen => GasFlags.Oxygen,
                Gas.CarbonDioxide => GasFlags.CarbonDioxide,
                Gas.Plasma => GasFlags.Plasma,
                Gas.Tritium => GasFlags.Tritium,
                Gas.WaterVapor => GasFlags.WaterVapor,
                Gas.Ammonia => GasFlags.Ammonia,
                Gas.NitrousOxide => GasFlags.NitrousOxide,
                Gas.Frezon => GasFlags.Frezon,
                Gas.BZ => GasFlags.BZ,
                Gas.Healium => GasFlags.Healium,
                Gas.Nitrium => GasFlags.Nitrium,
                Gas.Pluoxium => GasFlags.Pluoxium,
                _ => GasFlags.None
            };
        }

        return gasFlags;
    }
}
