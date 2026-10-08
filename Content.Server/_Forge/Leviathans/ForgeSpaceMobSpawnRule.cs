using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Leviathans;

/// <summary>
/// Spawns an AI leviathan in space near a station, without a ghost role.
/// </summary>
[RegisterComponent]
public sealed partial class ForgeSpaceMobSpawnRuleComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Prototype;

    [DataField]
    public float SpawnDistance = 80f;
}

public sealed partial class ForgeSpaceMobSpawnRule : StationEventSystem<ForgeSpaceMobSpawnRuleComponent>
{
    [Dependency] private SharedTransformSystem _transform = default!;

    protected override void Started(
        EntityUid uid,
        ForgeSpaceMobSpawnRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (!TryGetRandomStation(out var station))
            return;

        var gridUid = StationSystem.GetLargestGrid(station.Value);
        if (gridUid == null || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var size = grid.LocalAABB.Size.Length() / 2f;
        var distance = size + component.SpawnDistance;
        var angle = RobustRandom.NextAngle();
        var xform = Transform(gridUid.Value);
        var position = _transform.GetWorldPosition(xform) + angle.ToVec() * distance;
        Spawn(component.Prototype, new MapCoordinates(position, xform.MapID));
    }
}
