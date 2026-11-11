using System.Linq;
using System.Numerics;
using Content.Shared._Shitmed.Body.Part;
using Content.Shared._WF.Augments;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Augments;

/// <summary>
/// Swaps a body's parts and organs for the augments its profile picked. The old part or organ is cut loose from
/// the body before it is deleted, so its loss doesn't read as an injury; a hand keeps what it holds.
/// </summary>
public sealed partial class WFAugmentSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BodyComponent, WFApplyAugmentsEvent>(OnApplyAugments);
    }

    private void OnApplyAugments(Entity<BodyComponent> ent, ref WFApplyAugmentsEvent args)
    {
        if (LifeStage(ent) < EntityLifeStage.MapInitialized || TerminatingOrDeleted(ent))
            return;

        var digitigrade = WFAugmentRules.Digitigrade(args.Profile, _proto);
        foreach (var (slot, choice) in args.Profile.Augments)
        {
            if (!_proto.TryIndex(choice.Augment, out var augment))
                continue;

            switch (augment.Kind)
            {
                case WFAugmentKind.Limb when augment.Part is { } part:
                    if (ReplaceLimb(ent, slot, part) is { } limb && choice.Style is { } style && _proto.TryIndex(style, out var styleProto))
                        StyleLimb(ent, limb, styleProto, digitigrade);
                    break;
                case WFAugmentKind.Missing:
                    if (FindLimb(ent, slot) is { } missing)
                        CutLoose(ent, missing, keepHands: false);
                    break;
                case WFAugmentKind.Organ when augment.Organ is { } organ:
                    ReplaceOrgan(ent, slot, organ);
                    break;
            }
        }
    }

    private (EntityUid Id, BodyPartComponent Component)? FindLimb(Entity<BodyComponent> body, WFAugmentSlot slot)
    {
        var (type, symmetry) = slot switch
        {
            WFAugmentSlot.LeftArm => (BodyPartType.Arm, BodyPartSymmetry.Left),
            WFAugmentSlot.RightArm => (BodyPartType.Arm, BodyPartSymmetry.Right),
            WFAugmentSlot.LeftLeg => (BodyPartType.Leg, BodyPartSymmetry.Left),
            WFAugmentSlot.RightLeg => (BodyPartType.Leg, BodyPartSymmetry.Right),
            _ => (BodyPartType.Other, BodyPartSymmetry.None),
        };
        if (type == BodyPartType.Other)
            return null;

        foreach (var part in _body.GetBodyChildrenOfType(body, type, body.Comp, symmetry))
            return part;
        return null;
    }

    /// <summary>Replaces the slot's limb with a fresh one of the given prototype; the new limb, or null if it failed.</summary>
    private EntityUid? ReplaceLimb(Entity<BodyComponent> body, WFAugmentSlot slot, EntProtoId proto)
    {
        var slotId = WFAugmentRules.PartSlot(slot);
        EntityUid? parent;
        if (FindLimb(body, slot) is { } old)
        {
            if (MetaData(old.Id).EntityPrototype?.ID == proto.Id)
                return old.Id;

            parent = _body.GetParentPartOrNull(old.Id);
            slotId = old.Component.ParentSlot?.Id ?? slotId;
            CutLoose(body, old, keepHands: true);
        }
        else
        {
            parent = _body.GetBodyChildrenOfType(body, BodyPartType.Torso, body.Comp).FirstOrDefault().Id;
        }

        if (parent is not { Valid: true } parentUid || !TryComp(parentUid, out BodyPartComponent? parentPart))
            return null;

        var limb = Spawn(proto, new EntityCoordinates(body, Vector2.Zero));
        if (!TryComp(limb, out BodyPartComponent? limbPart)
            || !_body.AttachPart(parentUid, slotId, limb, parentPart, limbPart))
        {
            Del(limb);
            return null;
        }

        return limb;
    }

    /// <summary>Gives a limb and the parts under it the style's sprites, so a severed one keeps its look.</summary>
    private void StyleLimb(EntityUid body, EntityUid limb, WFAugmentStylePrototype style, bool digitigrade)
    {
        TryComp(body, out HumanoidAppearanceComponent? humanoid);
        foreach (var (part, comp) in _body.GetBodyPartChildren(limb))
        {
            if (comp.ToHumanoidLayers() is not { } layer)
                continue;

            var under = humanoid == null ? null : WFAugmentRules.Underlying(humanoid, layer, _proto);
            if (WFAugmentRules.Sprite(style, layer, digitigrade, under, _proto) is not { } sprite)
                continue;

            var appearance = EnsureComp<BodyPartAppearanceComponent>(part);
            appearance.Type = layer;
            appearance.ID = sprite;
            appearance.Color = null;
            Dirty(part, appearance);
        }
    }

    /// <summary>Takes a limb off without it reading as an injury. A replaced limb's hand keeps what it holds.</summary>
    private void CutLoose(Entity<BodyComponent> body, (EntityUid Id, BodyPartComponent Component) limb, bool keepHands)
    {
        if (!keepHands)
        {
            foreach (var (_, comp) in _body.GetBodyPartChildren(limb.Id, limb.Component))
            {
                if (comp.PartType == BodyPartType.Hand && comp.ParentSlot is { } handSlot)
                    _hands.RemoveHand(body, SharedBodySystem.GetPartSlotContainerId(handSlot.Id));
            }
        }

        _body.WfCutLoose(body, limb.Id, body.Comp, limb.Component);
    }

    private void ReplaceOrgan(Entity<BodyComponent> body, WFAugmentSlot slot, EntProtoId proto)
    {
        if (WFAugmentRules.OrganSlot(slot) is not { } organSlot)
            return;

        var partType = WFAugmentRules.PartSlot(slot) == "head" ? BodyPartType.Head : BodyPartType.Torso;
        if (_body.GetBodyChildrenOfType(body, partType, body.Comp).FirstOrDefault() is not { Id.Valid: true } part
            || !_containers.TryGetContainer(part.Id, SharedBodySystem.GetOrganContainerId(organSlot), out var container))
            return;

        foreach (var old in container.ContainedEntities.ToList())
        {
            if (MetaData(old).EntityPrototype?.ID == proto.Id)
                return;

            _body.WfDeleteOrgan(body, old);
        }

        var organ = Spawn(proto, new EntityCoordinates(body, Vector2.Zero));
        if (!_body.InsertOrgan(part.Id, organ, organSlot, part.Component))
            Del(organ);
    }

}
