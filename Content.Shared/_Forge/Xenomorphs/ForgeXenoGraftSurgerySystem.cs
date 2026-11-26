using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Content.Shared._Shitmed.Medical.Surgery.Steps.Parts;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Robust.Shared.Containers;

namespace Content.Shared._Forge.Xenomorphs;

/// <summary>
/// Surgical install and remove for harvested xenomorph glands. Two slots on the torso, no click-to-graft.
/// </summary>
public sealed class ForgeXenoGraftSurgerySystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SurgeryXenoGraftConditionComponent, SurgeryValidEvent>(OnValid);
        SubscribeLocalEvent<SurgeryInsertXenoGraftStepComponent, SurgeryStepEvent>(OnInsert);
        SubscribeLocalEvent<SurgeryInsertXenoGraftStepComponent, SurgeryStepCompleteCheckEvent>(OnInsertCheck);
        SubscribeLocalEvent<SurgeryAffixXenoGraftStepComponent, SurgeryStepEvent>(OnAffix);
        SubscribeLocalEvent<SurgeryAffixXenoGraftStepComponent, SurgeryStepCompleteCheckEvent>(OnAffixCheck);
        SubscribeLocalEvent<SurgeryRemoveXenoGraftStepComponent, SurgeryStepEvent>(OnRemove);
        SubscribeLocalEvent<SurgeryRemoveXenoGraftStepComponent, SurgeryStepCompleteCheckEvent>(OnRemoveCheck);
    }

    private void OnValid(Entity<SurgeryXenoGraftConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (HasComp<ForgeXenoOrganHostComponent>(args.Body)
            || !TryComp(args.Part, out BodyPartComponent? part)
            || part.Body != args.Body
            || part.PartType != BodyPartType.Torso)
        {
            args.Cancelled = true;
            return;
        }

        var count = CountGrafts(args.Part);
        if (ent.Comp.Inverse)
        {
            if (count >= ForgeXenoGraftSlots.Max)
                args.Cancelled = true;
        }
        else if (count == 0)
        {
            args.Cancelled = true;
        }
        else
        {
            RemComp<SurgeryXenoGraftExtractedComponent>(args.Part);
        }
    }

    private void OnInsert(Entity<SurgeryInsertXenoGraftStepComponent> ent, ref SurgeryStepEvent args)
    {
        if (!TryComp(args.Part, out BodyPartComponent? part)
            || part.Body != args.Body
            || HasComp<ForgeXenoOrganHostComponent>(args.Body))
            return;

        if (CountGrafts(args.Part) >= ForgeXenoGraftSlots.Max)
        {
            _popup.PopupEntity(Loc.GetString("xeno-graft-full"), args.Body, args.User);
            return;
        }

        if (FreeSlot(args.Part, part) is not { } slotId)
        {
            _popup.PopupEntity(Loc.GetString("xeno-graft-full"), args.Body, args.User);
            return;
        }

        EntityUid? gland = null;
        foreach (var tool in args.Tools)
        {
            if (HasComp<ForgeXenoOrganComponent>(tool) && HasComp<OrganComponent>(tool))
            {
                gland = tool;
                break;
            }
        }

        if (gland is not { } organUid || !TryComp(organUid, out OrganComponent? organ))
        {
            _popup.PopupEntity(Loc.GetString("xeno-graft-need-hand"), args.Body, args.User);
            return;
        }

        organ.SlotId = slotId;
        Dirty(organUid, organ);

        if (!_body.InsertOrgan(args.Part, organUid, slotId, part, organ))
        {
            _popup.PopupEntity(Loc.GetString("xeno-graft-insert-fail"), args.Body, args.User);
            return;
        }

        EnsureComp<OrganReattachedComponent>(organUid);
    }

    private void OnInsertCheck(Entity<SurgeryInsertXenoGraftStepComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (!AnyReattached(args.Part))
            args.Cancelled = true;
    }

    private void OnAffix(Entity<SurgeryAffixXenoGraftStepComponent> ent, ref SurgeryStepEvent args)
    {
        foreach (var graft in EnumerateGrafts(args.Part))
            RemComp<OrganReattachedComponent>(graft);
    }

    private void OnAffixCheck(Entity<SurgeryAffixXenoGraftStepComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (AnyReattached(args.Part))
            args.Cancelled = true;
    }

    private void OnRemove(Entity<SurgeryRemoveXenoGraftStepComponent> ent, ref SurgeryStepEvent args)
    {
        if (FindGraft(args.Part) is not { } organ)
            return;

        if (!_body.RemoveOrgan(organ))
            return;

        _hands.TryPickupAnyHand(args.User, organ);
        EnsureComp<SurgeryXenoGraftExtractedComponent>(args.Part);
    }

    private void OnRemoveCheck(Entity<SurgeryRemoveXenoGraftStepComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (!HasComp<SurgeryXenoGraftExtractedComponent>(args.Part))
            args.Cancelled = true;
    }

    private string? FreeSlot(EntityUid part, BodyPartComponent partComp)
    {
        foreach (var slotId in ForgeXenoGraftSlots.Ids)
        {
            if (!_body.TryCreateOrganSlot(part, slotId, out _, partComp))
                continue;

            Dirty(part, partComp);

            var containerId = SharedBodySystem.GetOrganContainerId(slotId);
            if (!_containers.TryGetContainer(part, containerId, out var container))
                continue;

            if (container.ContainedEntities.Count == 0)
                return slotId;
        }

        return null;
    }

    private int CountGrafts(EntityUid part)
    {
        var count = 0;
        foreach (var _ in EnumerateGrafts(part))
            count++;
        return count;
    }

    private EntityUid? FindGraft(EntityUid part)
    {
        foreach (var graft in EnumerateGrafts(part))
            return graft;
        return null;
    }

    private bool AnyReattached(EntityUid part)
    {
        foreach (var graft in EnumerateGrafts(part))
        {
            if (HasComp<OrganReattachedComponent>(graft))
                return true;
        }

        return false;
    }

    private IEnumerable<EntityUid> EnumerateGrafts(EntityUid part)
    {
        foreach (var slotId in ForgeXenoGraftSlots.Ids)
        {
            var containerId = SharedBodySystem.GetOrganContainerId(slotId);
            if (!_containers.TryGetContainer(part, containerId, out var container))
                continue;

            foreach (var contained in container.ContainedEntities)
            {
                if (HasComp<ForgeXenoOrganComponent>(contained))
                    yield return contained;
            }
        }
    }
}
