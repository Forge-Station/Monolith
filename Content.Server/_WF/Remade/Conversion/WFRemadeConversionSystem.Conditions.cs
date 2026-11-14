using System.Linq;
using Content.Shared._WF.Remade;
using Content.Shared.Access.Systems;
using Content.Shared.Armor;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Remade.Conversion;

/// <summary>
/// What a corpse can turn into. A mob's conversion conditions (sex, armour and legs, poison, job) come from DS13-1.0's
/// conversion.dm: a female body can be a spitter, a male one an exploder, a body in light armour with both legs a leaper, a
/// poisoned one a puker, and command, engineering, medical, mining, science and security staff a divider.
/// </summary>
public sealed partial class WFRemadeConversionSystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedIdCardSystem _idCards = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    [Dependency] private SharedMindSystem _minds = default!;

    /// <summary>DS13 counts armour past 15 in melee or bullet as heavy: a fifth of the damage off is that here.</summary>
    private const float HeavyArmour = 0.85f;

    private const string SuitSlot = "outerClothing";
    private static readonly string[] ArmourTypes = { "Blunt", "Slash", "Piercing" };

    /// <summary>What a corpse can turn into, and the weight of each. Mobs it can't turn into aren't listed.</summary>
    public Dictionary<WFRemadeMobPrototype, float> Weights(EntityUid corpse)
    {
        var compatibility = Compatibility(corpse);
        var sex = CompOrNull<HumanoidAppearanceComponent>(corpse)?.Sex ?? Sex.Unsexed;
        var lightArmour = IsLightlyArmoured(corpse);
        var legs = HasBothLegs(corpse);
        var poison = CompOrNull<DamageableComponent>(corpse)?.Damage.DamageDict.GetValueOrDefault("Poison").Float() ?? 0f;
        var departments = Departments(corpse);

        var weights = new Dictionary<WFRemadeMobPrototype, float>();
        foreach (var mob in _proto.EnumeratePrototypes<WFRemadeMobPrototype>())
        {
            if (mob.ConversionWeight <= 0f
                || mob.ConversionSex is { } wanted && sex != Sex.Unsexed && sex != wanted
                || mob.LightArmourAndLegs && !(lightArmour && legs)
                || mob.MinPoison > 0f && poison < mob.MinPoison)
            {
                continue;
            }

            var weight = mob.ConversionWeight;
            if (mob.Departments.Count > 0)
            {
                if (departments == null)
                    weight = mob.UnknownJobWeight;
                else if (!mob.Departments.Any(department => departments.Contains(department)))
                    continue;
            }

            if (weight > 0f)
                weights[mob] = weight * MathF.Pow(compatibility, mob.CompatibilityPower);
        }

        return weights;
    }

    /// <summary>Whether the outer clothing, if any, turns less than 15 percent off blunt, slash and piercing.</summary>
    private bool IsLightlyArmoured(EntityUid corpse)
    {
        if (!_inventory.TryGetSlotEntity(corpse, SuitSlot, out var suit) || !TryComp<ArmorComponent>(suit, out var armour))
            return true;

        foreach (var type in ArmourTypes)
        {
            if (armour.Modifiers.Coefficients.TryGetValue(type, out var coefficient) && coefficient < HeavyArmour)
                return false;
        }

        return true;
    }

    private bool HasBothLegs(EntityUid corpse)
    {
        return TryComp<BodyComponent>(corpse, out var body) && _body.GetBodyChildrenOfType(corpse, BodyPartType.Leg, body).Count() >= 2;
    }

    /// <summary>The departments of the corpse's job, from its ID card or else its mind. Null when neither says.</summary>
    private HashSet<ProtoId<DepartmentPrototype>>? Departments(EntityUid corpse)
    {
        if (_idCards.TryFindIdCard(corpse, out var card) && card.Comp.JobDepartments.Count > 0)
            return card.Comp.JobDepartments.ToHashSet();

        if (!_minds.TryGetMind(corpse, out var mind, out _) || !_jobs.MindTryGetJobId(mind, out var job) || job is not { } id)
            return null;

        var departments = new HashSet<ProtoId<DepartmentPrototype>>();
        foreach (var department in _proto.EnumeratePrototypes<DepartmentPrototype>())
        {
            if (department.Roles.Contains(id))
                departments.Add(department.ID);
        }

        return departments.Count > 0 ? departments : null;
    }
}
