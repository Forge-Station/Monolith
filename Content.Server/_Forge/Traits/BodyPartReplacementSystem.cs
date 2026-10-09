using Content.Shared._Forge.Traits;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Map;
using System.Linq;
using System.Numerics;

namespace Content.Server._Forge.Traits;

public sealed partial class BodyPartReplacementSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _bodySystem = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyPartReplacementComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(
        Entity<BodyPartReplacementComponent> ent,
        ref ComponentStartup args)
    {
        ReplaceParts(ent);
    }

    private void ReplaceParts(Entity<BodyPartReplacementComponent> ent)
    {
        if (!TryComp(ent, out BodyComponent? body))
            return;

        if (body.RootContainer.ContainedEntities.Count == 0)
            return;

        var torso = body.RootContainer.ContainedEntities.FirstOrDefault();

        if (!TryComp(torso, out BodyPartComponent? torsoPart))
            return;

        foreach (var replacement in ent.Comp.Replacements)
        {
            ReplacePart(
                replacement.Prototype,
                torso,
                replacement.Slot);
        }
    }

    private void ReplacePart(
        string partProtoId,
        EntityUid parentEntity,
        string slotId)
    {
        if (!_prototypeManager.TryIndex(partProtoId, out _))
            return;

        if (!TryComp(parentEntity, out BodyPartComponent? parentPart))
            return;

        var containerId =
            SharedBodySystem.GetPartSlotContainerId(slotId);

        if (_containerSystem.TryGetContainer(
                parentEntity,
                containerId,
                out var container))
        {
            var oldEntities =
                container.ContainedEntities.ToArray();

            foreach (var oldEntity in oldEntities)
            {
                if (TryComp(
                        oldEntity,
                        out BodyPartComponent? oldPart))
                {
                    DeleteChildParts(oldEntity, oldPart);
                }
            }

            foreach (var entity in oldEntities)
            {
                _containerSystem.Remove(entity, container);
                QueueDel(entity);
            }
        }

        var newPart = Spawn(
            partProtoId,
            new EntityCoordinates(
                parentEntity,
                Vector2.Zero));

        if (!TryComp(
                newPart,
                out BodyPartComponent? newPartComp))
        {
            QueueDel(newPart);
            return;
        }

        if (!_bodySystem.AttachPart(
                parentEntity,
                slotId,
                newPart,
                parentPart,
                newPartComp))
        {
            QueueDel(newPart);
        }
    }

    private void DeleteChildParts(
        EntityUid parent,
        BodyPartComponent part)
    {
        foreach (var (slotId, _) in part.Children)
        {
            var childContainerId =
                SharedBodySystem.GetPartSlotContainerId(slotId);

            if (_containerSystem.TryGetContainer(
                    parent,
                    childContainerId,
                    out var childContainer))
            {
                var children =
                    childContainer.ContainedEntities.ToArray();

                foreach (var child in children)
                {
                    if (TryComp(
                            child,
                            out BodyPartComponent? childPart))
                    {
                        DeleteChildParts(child, childPart);
                    }

                    _containerSystem.Remove(
                        child,
                        childContainer);

                    QueueDel(child);
                }
            }
        }
    }
}