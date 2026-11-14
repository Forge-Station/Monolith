using System.Linq;
using Content.Server.Atmos.Components;
using Content.Server.Hands.Systems;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Preferences.Loadouts.Effects;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFCrashScenarioSystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private StorageSystem _storage = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private MetaDataSystem _metadata = default!;

    /// <summary>Issues emergency EVA protection and species-appropriate survival supplies without deleting personal gear.</summary>
    public void GiveEmergencyEquipment(EntityUid body)
    {
        var coordinates = Transform(body).Coordinates;
        var bag = Spawn("ClothingBackpackDuffel", coordinates);
        _metadata.SetEntityName(bag, Loc.GetString("wf-round-setup-emergency-kit"));
        _metadata.SetEntityDescription(bag, Loc.GetString("wf-round-setup-emergency-kit-description"));
        var survival = MatchesBreather(body, "NitrogenBreatherNF") ? "BoxSurvivalMonoNitrogenExtended"
            : MatchesBreather(body, "WaterBreather") ? "BoxSurvivalMonoVaporExtended" : "BoxSurvivalMonoOxygenExtended";
        Stow(Spawn(survival, coordinates));
        foreach (var supply in new[] { "MedkitFilled", "HandheldHealthAnalyzer", "WelderMini", "Screwdriver", "Wrench", "Wirecutter", "FireExtinguisherMini",
                     "FlashlightLantern", "DrinkWaterBottleFull", "FoodSnackEnergyBar", "HarmonicaInstrument" })
            Stow(Spawn(supply, coordinates));
        var hasSuit = _inventory.TryGetSlotEntity(body, "outerClothing", out var outer);
        if (!hasSuit || !HasComp<PressureProtectionComponent>(outer))
        {
            if (hasSuit && _inventory.TryUnequip(body, "outerClothing", silent: true, force: true))
                Stow(outer!.Value);
            var suit = Spawn("ClothingOuterSuitEmergency", coordinates);
            if (!_inventory.TryEquip(body, suit, "outerClothing", silent: true, force: true))
                Stow(suit);
        }
        if (!_inventory.TryGetSlotEntity(body, "mask", out _))
        {
            var mask = Spawn("ClothingMaskBreath", coordinates);
            if (!_inventory.TryEquip(body, mask, "mask", silent: true, force: true))
                Stow(mask);
        }
        else
            Stow(Spawn("ClothingMaskBreath", coordinates));
        if (!_inventory.TryEquip(body, bag, "back", silent: true, force: true))
            _hands.TryPickupAnyHand(body, bag);

        void Stow(EntityUid item)
        {
            if (!_storage.Insert(bag, item, out _, playSound: false))
                _hands.TryPickupAnyHand(body, item);
        }
    }

    private bool MatchesBreather(EntityUid body, string group) =>
        TryComp<HumanoidAppearanceComponent>(body, out var appearance) &&
        _prototypes.Index<LoadoutEffectGroupPrototype>(group).Effects.OfType<SpeciesLoadoutEffect>()
            .Any(effect => effect.Species.Contains(appearance.Species));
}
