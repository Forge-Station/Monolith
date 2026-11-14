using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._WF.Drydock;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

/// <summary>
/// Hull plating refits (<see cref="WFHullPlatingRefit"/>): each stock wall replaced in place by its plated wall. Each
/// wall is a unit. The repair snapshot records the plated wall, so the ship repair tool and drydock repairs rebuild it.
/// </summary>
public sealed partial class WFHullPlatingRefitSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFRefitSurveyEvent<WFHullPlatingRefit>>(OnSurvey);
        SubscribeLocalEvent<WFRefitProspectEvent<WFHullPlatingRefit>>(OnProspect);
        SubscribeLocalEvent<WFRefitApplyEvent<WFHullPlatingRefit>>(OnApply);
    }

    /// <summary>Counts the ship's plated walls, and lists its stock ones.</summary>
    private void OnSurvey(WFRefitSurveyEvent<WFHullPlatingRefit> args)
    {
        var survey = args.Survey;
        var plating = args.Effect.Plating;
        var children = Transform(survey.Ship).ChildEnumerator;
        while (children.MoveNext(out var uid))
        {
            var xform = Transform(uid);
            if (!xform.Anchored || MetaData(uid).EntityPrototype is not { } proto)
                continue;

            if (plating.ContainsKey(proto.ID))
            {
                var target = new WFRefitTarget(uid, xform.LocalPosition, survey.Tier);
                target.Lacking[0] = 1;
                survey.Below.Add(target);
            }
            else if (plating.ContainsValue(proto.ID))
            {
                survey.UnitsAtTier++;
            }
        }
    }

    /// <summary>A rebuilt stock wall comes back unplated.</summary>
    private void OnProspect(WFRefitProspectEvent<WFHullPlatingRefit> args)
    {
        if (args.Effect.Plating.ContainsKey(args.Prospect.Prototype))
            args.Prospect.Lacking[0]++;
    }

    /// <summary>Replaces a stock wall with its plated wall, anchored and turned as it was.</summary>
    private void OnApply(WFRefitApplyEvent<WFHullPlatingRefit> args)
    {
        var wall = args.Target;
        if (TerminatingOrDeleted(wall) ||
            MetaData(wall).EntityPrototype is not { } proto ||
            !args.Effect.Plating.TryGetValue(proto.ID, out var plated))
            return;

        var xform = Transform(wall);
        if (!xform.Anchored || xform.GridUid is not { } grid)
            return;

        var coordinates = xform.Coordinates;
        var rotation = xform.LocalRotation;
        var spawned = Spawn(plated, coordinates);
        _transform.SetLocalRotation(spawned, rotation);
        if (!Transform(spawned).Anchored)
            _transform.AnchorEntity(spawned);

        Record(grid, coordinates, wall, spawned, plated);
        Del(wall);
    }

    /// <summary>Records a replaced wall's replacement and its prototype in the repair snapshot, in place of the old wall.</summary>
    private void Record(EntityUid grid, EntityCoordinates coordinates, EntityUid old, EntityUid replacement, EntProtoId plated)
    {
        if (!TryComp<ShipRepairDataComponent>(grid, out var data) || !TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        // Chunks are keyed by floored tile index, as SharedShipRepairSystem keys them.
        var tile = _map.LocalToTile(grid, gridComp, coordinates);
        var size = data.ChunkSize;
        var chunkIndices = new Vector2i((int) MathF.Floor(tile.X / (float) size), (int) MathF.Floor(tile.Y / (float) size));
        if (!data.Chunks.TryGetValue(chunkIndices, out var chunk))
            return;

        var oldNet = GetNetEntity(old);
        foreach (var spec in chunk.Entities.Values)
        {
            if (spec.OriginalEntity != oldNet)
                continue;

            var index = data.EntityPalette.IndexOf(plated);
            if (index == -1)
            {
                data.EntityPalette.Add(plated);
                index = data.EntityPalette.Count - 1;
            }

            spec.ProtoIndex = index;
            spec.OriginalEntity = GetNetEntity(replacement);
            Dirty(grid, data);
            return;
        }
    }
}
