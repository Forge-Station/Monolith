using Content.Server.Botany.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Kitchen.Components;
using Content.Shared.Kitchen.Components;
using Content.Server.Popups;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;// Forge-Change
using Content.Shared.Chemistry.Components;
using Content.Shared.Botany;
using Content.Shared._Forge.Botany;// Forge-Change
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Random;
using Content.Shared.Random.Helpers;
using Content.Shared.Sprite;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server._NF.Contraband.Systems; // Frontier
using Robust.Shared.Maths;

namespace Content.Server.Botany.Systems;

public sealed partial class BotanySystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private PopupSystem _popupSystem = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private RandomHelperSystem _randomHelper = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;// Forge-Change
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SeedComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<SeedComponent, PriceCalculationEvent>(OnSeedPrice);
        SubscribeLocalEvent<ProduceComponent, PriceCalculationEvent>(OnProducePrice);
    }

    public bool TryGetSeed(SeedComponent comp, [NotNullWhen(true)] out SeedData? seed)
    {
        if (comp.Seed != null)
        {
            seed = comp.Seed;
            return true;
        }

        if (comp.SeedId != null
            && _prototypeManager.TryIndex(comp.SeedId, out SeedPrototype? protoSeed))
        {
            seed = protoSeed;
            return true;
        }

        seed = null;
        return false;
    }

    public bool TryGetSeed(ProduceComponent comp, [NotNullWhen(true)] out SeedData? seed)
    {
        if (comp.Seed != null)
        {
            seed = comp.Seed;
            return true;
        }

        if (comp.SeedId != null
            && _prototypeManager.TryIndex(comp.SeedId, out SeedPrototype? protoSeed))
        {
            seed = protoSeed;
            return true;
        }

        seed = null;
        return false;
    }

    public void ReplacePacketSeed(SeedComponent comp, SeedData seed)
    {
        comp.Seed = seed;
    }

    private void OnSeedPrice(EntityUid uid, SeedComponent component, ref PriceCalculationEvent args)
    {
        if (!TryGetSeed(component, out var seed) || !seed.Unique)
            return;

        args.Price += GetCultivarExportBonus(seed);
    }

    private void OnProducePrice(EntityUid uid, ProduceComponent component, ref PriceCalculationEvent args)
    {
        if (!TryGetSeed(component, out var seed) || !seed.Unique)
            return;

        args.Price += GetCultivarExportBonus(seed) * 0.35;
    }

    /// <summary>
    /// Extra cargo value for a bred line. Stock packets (prototype seedId, Unique=false)
    /// stay at StaticPrice so cargo seed crates cannot be bought and sold for profit.
    /// </summary>
    public static double GetCultivarExportBonus(SeedData seed)
    {
        var bonus = Math.Max(0, seed.Potency - 10f) * 1.6;
        bonus += seed.Mutations.Count * 22;
        if (seed.Ligneous)
            bonus += 18;
        if (seed.GeneLocked)
            bonus += 25;
        if (seed.CarnivorousPestEater)
            bonus += 28;
        if (seed.CarnivorousGrab)
            bonus += 30;
        if (seed.Radioactive)
            bonus += 40;
        if (seed.Chemicals.ContainsKey("Dermaline"))
            bonus += 35;
        if (seed.Chemicals.ContainsKey("Hyronalin"))
            bonus += 40;
        return bonus;
    }

    private void OnExamined(EntityUid uid, SeedComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (!TryGetSeed(component, out var seed))
            return;

        using (args.PushGroup(nameof(SeedComponent), 1))
        {
            var name = Loc.GetString(seed.DisplayName);
            args.PushMarkup(Loc.GetString($"seed-component-description", ("seedName", name)));
            args.PushMarkup(Loc.GetString($"seed-component-plant-yield-text", ("seedYield", seed.Yield)));
            args.PushMarkup(Loc.GetString($"seed-component-plant-potency-text", ("seedPotency", seed.Potency)));
            if (seed.GeneLocked)
            {
                // Forge-Change-start
                var pinned = seed.PinnedTraits.Count > 0
                    ? string.Join(", ", seed.PinnedTraits.Select(BotanyLocalization.GetMutationName))
                    : Loc.GetString("plant-analyzer-mutation-gene-locked");
                args.PushMarkup(Loc.GetString("seed-component-gene-locked", ("traits", pinned)));
                // Forge-Change-end
            }

            // Forge-Change-start
            if (seed.CultivarJournalLocked)
                args.PushMarkup(Loc.GetString("seed-component-cultivar-journal-locked"));
            // Forge-Change-end
        }
    }

    #region SeedPrototype prototype stuff

    /// <summary>
    /// Spawns a new seed packet on the floor at a position, then tries to put it in the user's hands if possible.
    /// </summary>
    public EntityUid SpawnSeedPacket(SeedData proto, EntityCoordinates coords, EntityUid user, float? healthOverride = null)
    {
        var seed = SpawnAtPosition(proto.PacketPrototype, coords); // Frontier: Spawn<SpawnAtPosition
        var seedComp = EnsureComp<SeedComponent>(seed);
        seedComp.Seed = proto;
        seedComp.HealthOverride = healthOverride;

        var name = Loc.GetString(proto.Name);
        var noun = Loc.GetString(proto.Noun);
        var val = Loc.GetString(proto.PacketName, ("seedName", name), ("seedNoun", noun)); // Frontier: "botany-seed-packet-name"<proto.PacketName
        _metaData.SetEntityName(seed, val);

        // try to automatically place in user's other hand
        _hands.TryPickupAnyHand(user, seed);
        return seed;
    }

    // Forge-Change-start
    public IEnumerable<EntityUid> AutoHarvest(SeedData proto, EntityCoordinates position, int yieldMod = 1, EntityUid? plantholder = null)
    {
        if (position.IsValid(EntityManager) &&
            proto.ProductPrototypes.Count > 0)
            return GenerateProduct(proto, position, yieldMod, plantholder);

        return Enumerable.Empty<EntityUid>();
    }

    public IEnumerable<EntityUid> Harvest(SeedData proto, EntityUid user, int yieldMod = 1, EntityUid? plantholder = null)
    {
        if (proto.ProductPrototypes.Count == 0 || proto.Yield <= 0)
        {
            _popupSystem.PopupCursor(Loc.GetString("botany-harvest-fail-message"), user, PopupType.Medium);
            return Enumerable.Empty<EntityUid>();
        }

        var name = Loc.GetString(proto.DisplayName);
        _popupSystem.PopupCursor(Loc.GetString("botany-harvest-success-message", ("name", name)), user, PopupType.Medium);
        var position = plantholder != null
            ? Transform(plantholder.Value).Coordinates
            : Transform(user).Coordinates;
        return GenerateProduct(proto, position, yieldMod, plantholder, user);
    }

    public IEnumerable<EntityUid> GenerateProduct(SeedData proto, EntityCoordinates position, int yieldMod = 1,
        EntityUid? plantholder = null, EntityUid? user = null)
    // Forge-Change-end
    {
        var totalYield = 0;
        if (proto.Yield > -1)
        {
            if (yieldMod < 0)
                totalYield = proto.Yield;
            else
                totalYield = proto.Yield * yieldMod;

            totalYield = Math.Max(1, totalYield);
        }

        var products = new List<EntityUid>();

        if (totalYield > 1 || proto.HarvestRepeat != HarvestType.NoRepeat)
            proto.Unique = false;

        for (var i = 0; i < totalYield; i++)
        {
            var product = _robustRandom.Pick(proto.ProductPrototypes);

            var entity = SpawnAtPosition(product, position); // Frontier: Spawn<SpawnAtPosition
            _randomHelper.RandomOffset(entity, 0.25f);
            products.Add(entity);

            var produce = EnsureComp<ProduceComponent>(entity);

            produce.Seed = proto;
            ProduceGrown(entity, produce);

            _appearance.SetData(entity, ProduceVisuals.Potency, proto.Potency);

            if (proto.Mysterious)
            {
                var metaData = MetaData(entity);
                _metaData.SetEntityName(entity, metaData.EntityName + "?", metaData);
                _metaData.SetEntityDescription(entity,
                    metaData.EntityDescription + " " + Loc.GetString("botany-mysterious-description-addon"), metaData);
            }
        }

        if (proto.Ligneous)
        {
            var sheets = proto.Potency >= 70f ? 2 : 1;
            var tint = GetPlantTint(proto);
            var clothName = Loc.GetString("botany-plant-cloth-name", ("name", Loc.GetString(proto.Name)));
            for (var i = 0; i < sheets; i++)
            {
                var cloth = SpawnAtPosition("MaterialCloth1", position);
                _randomHelper.RandomOffset(cloth, 0.25f);
                ApplyPlantTint(cloth, tint, "base", "cloth");
                _metaData.SetEntityName(cloth, clothName);
                products.Add(cloth);
            }
        }

        // Forge-Change
        SpawnHarvestExtras(proto, position, products, plantholder, user);

        return products;
    }

    // Forge-Change-start
    private void SpawnHarvestExtras(SeedData proto, EntityCoordinates position, List<EntityUid> products,
        EntityUid? plantholder = null, EntityUid? user = null)
    {
        var tint = GetPlantTint(proto);
        var plantName = Loc.GetString(proto.Name);
        var spawnedDrink = false;

        if (proto.Potency >= 45f && proto.Chemicals.ContainsKey("Dermaline"))
        {
            var cream = SpawnAtPosition("AloeCream1", position);
            _randomHelper.RandomOffset(cream, 0.25f);
            products.Add(cream);
            if (proto.Potency >= 70f)
            {
                var extra = SpawnAtPosition("AloeCream1", position);
                _randomHelper.RandomOffset(extra, 0.25f);
                products.Add(extra);
            }

            if (TryPourHerbalDrinkIntoContainer(plantholder, "botany-aloe-tea-name", plantName, "Dermaline", proto.Potency, products, user))
                spawnedDrink = true;
            else
                NotifyHarvestContainerMissed(plantholder, user);
        }

        if (proto.Potency >= 45f && proto.Chemicals.ContainsKey("Hyronalin"))
        {
            if (TryPourHerbalDrinkIntoContainer(plantholder, "botany-antirad-tea-name", plantName, "Hyronalin", proto.Potency, products, user))
                spawnedDrink = true;
            else
                NotifyHarvestContainerMissed(plantholder, user);
        }

        if (!spawnedDrink && proto.Potency >= 40f && proto.Chemicals.Count > 0)
        {
            if (!TryPourJuiceIntoContainer(plantholder, proto, 30, "botany-plant-juice-name", plantName, products, user))
                NotifyHarvestContainerMissed(plantholder, user);
        }

        if (proto.Potency >= 55f)
        {
            var dried = SpawnAtPosition("BotanyDriedProduce", position);
            _randomHelper.RandomOffset(dried, 0.25f);
            FillHarvestSolution(dried, "food", proto, 16);
            ApplyPlantTint(dried, tint, "base", "dried");
            _metaData.SetEntityName(dried, Loc.GetString("botany-plant-dried-name", ("name", plantName)));
            products.Add(dried);
        }
    }

    private bool TryPourHerbalDrinkIntoContainer(EntityUid? plantholder, string nameLoc, string plantName,
        string reagent, float potency, List<EntityUid> products, EntityUid? user = null)
    {
        if (!TryGetHarvestContainer(plantholder, out var container, out var solEnt, out var sol) || sol == null || solEnt == null)
            return false;

        if (sol.AvailableVolume < 10)
            return false;

        var maxVol = Math.Min(30, sol.MaxVolume.Float());
        sol.RemoveAllSolution();
        sol.MaxVolume = maxVol;
        var medicine = FixedPoint2.New(Math.Clamp(potency / 8f, 4f, 12f));
        var total = FixedPoint2.New(maxVol);
        sol.AddReagent("Tea", total - medicine);
        sol.AddReagent(reagent, medicine);
        _solutionContainerSystem.UpdateChemicals(solEnt.Value);
        _metaData.SetEntityName(container, Loc.GetString(nameLoc, ("name", plantName)));

        if (!products.Contains(container))
            products.Add(container);

        NotifyHarvestContainerFilled(plantholder, user, plantName);
        return true;
    }

    private bool TryGetHarvestContainer(EntityUid? plantholder, out EntityUid container,
        out Entity<SolutionComponent>? solEnt, out Solution? solution)
    {
        container = default;
        solEnt = null;
        solution = null;

        if (plantholder == null)
            return false;

        var item = _itemSlots.GetItemOrNull(plantholder.Value, PlantHolderComponent.HarvestContainerSlotId);
        if (item == null)
            return false;

        if (!_solutionContainerSystem.TryGetFitsInDispenser(item.Value, out solEnt, out solution))
            return false;

        container = item.Value;
        return true;
    }

    private bool TryPourJuiceIntoContainer(EntityUid? plantholder, SeedData proto, int targetMaxVol, string nameLoc,
        string plantName, List<EntityUid> products, EntityUid? user = null)
    {
        if (!TryGetHarvestContainer(plantholder, out var container, out var solEnt, out var sol) || sol == null || solEnt == null)
            return false;

        var pourAmount = FixedPoint2.Min(FixedPoint2.New(targetMaxVol), sol.AvailableVolume);
        if (pourAmount <= 0)
            return false;

        AddHarvestReagents(sol, proto, pourAmount);
        _solutionContainerSystem.UpdateChemicals(solEnt.Value);
        _metaData.SetEntityName(container, Loc.GetString(nameLoc, ("name", plantName)));

        if (!products.Contains(container))
            products.Add(container);

        NotifyHarvestContainerFilled(plantholder, user, plantName);
        return true;
    }

    private void NotifyHarvestContainerMissed(EntityUid? plantholder, EntityUid? user)
    {
        if (plantholder is not { } tray || !tray.IsValid())
            return;

        string msg;
        if (_itemSlots.GetItemOrNull(tray, PlantHolderComponent.HarvestContainerSlotId) == null)
            msg = Loc.GetString("plant-holder-harvest-container-missing");
        else
            msg = Loc.GetString("plant-holder-harvest-container-full");

        if (user is { } recipient && recipient.IsValid())
            _popupSystem.PopupEntity(msg, tray, recipient, PopupType.Medium);
        else
            _popupSystem.PopupEntity(msg, tray, PopupType.Medium);
    }

    private void NotifyHarvestContainerFilled(EntityUid? plantholder, EntityUid? user, string plantName)
    {
        if (plantholder is not { } tray || !tray.IsValid())
            return;

        var msg = Loc.GetString("plant-holder-harvest-container-filled", ("name", plantName));
        if (user is { } recipient && recipient.IsValid())
            _popupSystem.PopupEntity(msg, tray, recipient);
        else
            _popupSystem.PopupEntity(msg, tray);
    }

    private static void AddHarvestReagents(Solution sol, SeedData proto, FixedPoint2 totalVolume)
    {
        var remaining = totalVolume;
        foreach (var (chem, quantity) in proto.Chemicals)
        {
            if (remaining <= 0)
                break;

            var amount = FixedPoint2.New(quantity.Min);
            if (quantity.PotencyDivisor > 0 && proto.Potency > 0)
                amount += FixedPoint2.New(proto.Potency / quantity.PotencyDivisor);
            amount = FixedPoint2.New(MathHelper.Clamp(amount.Float(), quantity.Min, quantity.Max));
            amount = FixedPoint2.Min(amount, remaining);
            sol.AddReagent(chem, amount);
            remaining -= amount;
        }
    }
    // Forge-Change-end

    private void FillHarvestSolution(EntityUid uid, string solutionName, SeedData proto, int maxVol)
    {
        if (!_solutionContainerSystem.EnsureSolutionEntity(uid, solutionName, out var solEnt) || solEnt == null)
            return;

        var sol = solEnt.Value.Comp.Solution;
        sol.RemoveAllSolution();
        sol.MaxVolume = maxVol;
        var remaining = FixedPoint2.New(maxVol);
        foreach (var (chem, quantity) in proto.Chemicals)
        {
            if (remaining <= 0)
                break;
            var amount = FixedPoint2.New(quantity.Min);
            if (quantity.PotencyDivisor > 0 && proto.Potency > 0)
                amount += FixedPoint2.New(proto.Potency / quantity.PotencyDivisor);
            amount = FixedPoint2.New(MathHelper.Clamp(amount.Float(), quantity.Min, quantity.Max));
            amount = FixedPoint2.Min(amount, remaining);
            sol.AddReagent(chem, amount);
            remaining -= amount;
        }

        _solutionContainerSystem.UpdateChemicals(solEnt.Value);
    }

    private static Color GetPlantTint(SeedData seed)
    {
        var hash = 17;
        foreach (var c in seed.Name)
            hash = hash * 31 + c;
        var hue = Math.Abs(hash % 360) / 360f;
        return Color.FromHsv(new Vector4(hue, 0.55f, 0.78f, 1f));
    }

    private void ApplyPlantTint(EntityUid uid, Color color, string layer, string state)
    {
        var sprite = EnsureComp<RandomSpriteComponent>(uid);
        sprite.Selected[layer] = (state, color);
        Dirty(uid, sprite);
    }

    public bool CanHarvest(SeedData proto, EntityUid? held = null)
    {
        return !proto.Ligneous || proto.Ligneous && held != null && HasComp<SharpComponent>(held);
    }

    #endregion
}
