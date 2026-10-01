// WOLFGATE: HOOK 8 body. HealingSystem.cs calls into OnWoundHostDoAfter for wound hosts instead of the flat
// DamageableComponent path; this file is that entire branch, mirroring Onyx's HealingSystem wound branches
// (hooks-a.md 5a/5b/5d). Kept out of the upstream file so it carries only the call site.

using System.Linq;
using Content.Shared.FixedPoint;
using Content.Server.Body.Components;
using Content.Server.Medical.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Database;
using Content.Shared.IdentityManagement;
using Content.Shared.Medical;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server.Medical;

public sealed partial class HealingSystem
{
    [Dependency] private WoundHealingSystem _woundHealing = default!; // WOLFGATE: HOOK 8
    [Dependency] private WoundBleedingSystem _woundBleeding = default!;
    [Dependency] private Content.Server._WF.Wolfmed.Wounds.WolfmedFluidLossSystem _wfFluidLoss = default!; // M1b
    [Dependency] private Content.Server._WF.Wolfmed.Wounds.WolfmedInfectionSystem _wfInfection = default!; // M2

    private List<ProtoId<DamageContainerPrototype>>? GetHealingContainers(HealingComponent healing) =>
        healing.DamageContainers?.Select(x => new ProtoId<DamageContainerPrototype>(x)).ToList();

    private void OnWoundHostDoAfter(Entity<DamageableComponent> entity, ref HealingDoAfterEvent args,
        HealingComponent healing)
    {
        if (args.Used is not { } used)
            return;

        EntityUid? requestedPart = null;
        if (args.RequestedPart is { } selected && !TryGetEntity(selected, out requestedPart))
            return;
        if (requestedPart is { } concretePart &&
            _woundHealing.ResolveHealingPart(entity, concretePart,
                _woundHealing.GetTreatableDamage(healing), GetHealingContainers(healing), // W0: TreatedDamageTypes
                healing.TreatmentCapabilities, healing.AllowedWoundStages, healing.BloodlossModifier,
                healing.HealWounds) != concretePart)
        {
            var message = _bodySystem.BodyHasChild(entity, concretePart)
                ? "targeting-selected-part-incompatible"
                : "targeting-selected-part-missing";
            _popupSystem.PopupEntity(Loc.GetString(message), entity, args.User);
            return;
        }

        // M1b (plan §3.7): a burn dressing marks the part's weeping burns dressed. The part is resolved the way
        // the item itself resolves it, before the healing moves the damage it picks by.
        var treatable = _woundHealing.GetTreatableDamage(healing);
        EntityUid? burnPart = treatable.DamageDict.GetValueOrDefault("Heat") < FixedPoint2.Zero
            ? requestedPart ?? _woundHealing.ResolveHealingPart(entity, null, treatable, GetHealingContainers(healing),
                healing.TreatmentCapabilities, healing.AllowedWoundStages, healing.BloodlossModifier, healing.HealWounds)
            : null;

        // M2 (P20): a suture closes what it treats, and infection reads that as a treated wound.
        EntityUid? suturedPart = HasComp<Content.Shared._WF.Wolfmed.Wounds.WolfmedSutureComponent>(used)
            ? requestedPart ?? _woundHealing.ResolveHealingPart(entity, null, treatable, GetHealingContainers(healing),
                healing.TreatmentCapabilities, healing.AllowedWoundStages, healing.BloodlossModifier, healing.HealWounds)
            : null;

        if (!_woundHealing.TryApplyHealing(entity, requestedPart, (used, healing), args.User,
                out var healed, out var stoppedBleeding))
            return;

        if (burnPart is { } dressedPart)
            _wfFluidLoss.Dress(dressedPart);

        if (suturedPart is { } closedPart)
            _wfInfection.MarkSutured(closedPart, healing.TreatedDamageTypes?.Select(type => type.Id).ToList());

        if (healing.ModifyBloodLevel != 0)
            _bloodstreamSystem.TryModifyBloodLevel(entity.Owner, healing.ModifyBloodLevel);

        if (stoppedBleeding)
        {
            if (entity.Owner == args.User)
                _popupSystem.PopupEntity(Loc.GetString("medical-item-stop-bleeding-self"), entity, args.User);
            else
                _popupSystem.PopupEntity(Loc.GetString("medical-item-stop-bleeding", ("target", Identity.Entity(entity.Owner, EntityManager))), entity, args.User);
        }

        var dontRepeat = false;
        if (TryComp<StackComponent>(used, out var stackComp))
        {
            _stacks.Use(used, 1, stackComp);

            if (_stacks.GetCount(used, stackComp) <= 0)
                dontRepeat = true;
        }
        else
        {
            QueueDel(used);
        }

        var total = healed.GetTotal();
        if (entity.Owner != args.User)
        {
            _adminLogger.Add(LogType.Healed,
                $"{EntityManager.ToPrettyString(args.User):user} healed {EntityManager.ToPrettyString(entity.Owner):target} for {total:damage} damage");
        }
        else
        {
            _adminLogger.Add(LogType.Healed,
                $"{EntityManager.ToPrettyString(args.User):user} healed themselves for {total:damage} damage");
        }

        _audio.PlayPvs(healing.HealingEndSound, entity.Owner);

        args.Repeat = !dontRepeat && IsWoundDamaged(entity, healing, requestedPart);

        // SS13-style: with this part done, carry on to the next part the same item can still treat. The user's
        // own body-part target is left where it was; only this do-after moves.
        if (!args.Repeat && !dontRepeat && requestedPart != null &&
            TryGetNextTreatablePart(entity, healing, requestedPart.Value, out var nextPart))
        {
            args.RequestedPart = GetNetEntity(nextPart);
            args.Repeat = true;
            _popupSystem.PopupEntity(Loc.GetString("wolfmed-healing-next-part",
                ("part", Identity.Entity(nextPart, EntityManager))), entity, args.User);
        }

        if (!args.Repeat && !dontRepeat)
        {
            // Say why it stopped when the part is still bleeding: the medic is otherwise left guessing.
            var hint = requestedPart is { } stillBleeding && _woundBleeding.HasUndressableBleed(stillBleeding)
                ? "wolfmed-healing-arterial-hint"
                : "medical-item-finished-using";
            _popupSystem.PopupEntity(Loc.GetString(hint, ("item", used)), entity.Owner, args.User);
        }
        args.Handled = true;
    }

    /// <summary>The next body part, after <paramref name="current"/> in body order, that this item still has work on.</summary>
    private bool TryGetNextTreatablePart(Entity<DamageableComponent> entity, HealingComponent healing, EntityUid current,
        out EntityUid next)
    {
        next = default;
        var parts = _bodySystem.GetBodyChildren(entity.Owner).Select(part => part.Id).ToList();
        var start = parts.IndexOf(current);
        for (var i = 1; i < parts.Count; i++)
        {
            var candidate = parts[(start + i) % parts.Count];
            if (_woundHealing.ResolveHealingPart(entity, candidate, _woundHealing.GetTreatableDamage(healing),
                    GetHealingContainers(healing), healing.TreatmentCapabilities, healing.AllowedWoundStages,
                    healing.BloodlossModifier, healing.HealWounds) != candidate ||
                !IsWoundDamaged(entity, healing, candidate))
                continue;

            next = candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Onyx's wound-host half of HasDamage, kept beside Wolfgate's own checks instead of folded into them.
    /// Public so a test can ask whether an item has any work on a patient without driving its do-after.
    /// </summary>
    public bool IsWoundDamaged(Entity<DamageableComponent> entity, HealingComponent healing, EntityUid? requestedPart)
    {
        if (!TryComp(entity, out WoundHostComponent? host))
            return false;

        // W0: everything below asks about the treatable half of the spec, not the whole item.
        var treatable = _woundHealing.GetTreatableDamage(healing);
        var resolve = new ResolveHealingPartEvent(entity, treatable, GetHealingContainers(healing),
            healing.TreatmentCapabilities, healing.AllowedWoundStages, healing.BloodlossModifier, requestedPart,
            healing.HealWounds);
        RaiseLocalEvent(entity.Owner, ref resolve);
        if (!resolve.Accepted)
            return false;

        // A dressing (cloth, gauze) exists to stop a bleed and heals a token amount on the side. Its work is done
        // when the bleed is, or it grinds through the whole stack half a point at a time.
        var dressingOnly = healing.BloodlossModifier < 0 && -treatable.GetTotal() < FixedPoint2.New(1.5);
        if (dressingOnly)
            return resolve.Part is { } dressedPart && _woundHealing.CanTreatBleeding(dressedPart);

        if (healing.HealDamage)
        {
            var bloodFull = IsBloodFull(entity);
            foreach (var (type, amount) in treatable.DamageDict)
            {
                // A blood pack on a full bloodstream has nothing to do, whatever bloodloss damage the body
                // is still carrying: it was being spent one pack at a time on a number it could not move.
                if (bloodFull && type == "Bloodloss")
                    continue;

                var source = host.LocalizedDamageTypes.Contains(type) ? resolve.Part : entity.Owner;
                if (amount < 0 && source is { } sourceEntity &&
                    TryComp(sourceEntity, out DamageableComponent? sourceDamage) &&
                    sourceDamage.Damage.DamageDict.GetValueOrDefault(type) > 0)
                    return true;
            }
        }

        // Wounds count as work left for every wound-healing item: once the damage is gone a topical treats the
        // wound directly (WoundHealingSystem.TryApplyHealing), so a contusion does not outlive its bruise packs.
        if (healing.HealWounds && resolve.Part is { } woundPart &&
            _woundHealing.HasTreatableWounds(woundPart, treatable, healing.AllowedWoundStages))
            return true;

        if (resolve.Part is { } bleedingPart && healing.BloodlossModifier < 0 &&
            _woundHealing.CanTreatBleeding(bleedingPart))
            return true;

        if (healing.ModifyBloodLevel > 0 && !IsBloodFull(entity))
            return true;

        return false;
    }

    /// <summary>True when the bloodstream is already at its maximum volume, so nothing can add to it.</summary>
    private bool IsBloodFull(EntityUid body)
    {
        return TryComp<BloodstreamComponent>(body, out var bloodstream) &&
               _solutionContainerSystem.ResolveSolution(body, bloodstream.BloodSolutionName,
                   ref bloodstream.BloodSolution, out var blood) &&
               blood.Volume >= blood.MaxVolume;
    }
}
