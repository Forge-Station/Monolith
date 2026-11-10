using System.Numerics;
using Content.Shared._WF.Encounters;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Client._WF.Encounters;

/// <summary>Keeps the sector's encounter markers for the radar to draw.</summary>
public sealed partial class WFEncounterMarkerClientSystem : EntitySystem
{
    /// <summary>The longest a marker is moved on by its velocity; the server updates every two seconds.</summary>
    private const float MaxExtrapolation = 10f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public List<WFEncounterMarker> Markers { get; private set; } = new();

    private TimeSpan _received;

    /// <summary>The zone names, looked up once since the radar draws them every frame.</summary>
    public string WarnLabel { get; private set; } = string.Empty;
    public string AttackLabel { get; private set; } = string.Empty;

    /// <summary>Seconds since the markers arrived, to move them on by their velocity.</summary>
    public float Elapsed => (float) (_timing.RealTime - _received).TotalSeconds;

    public override void Initialize()
    {
        base.Initialize();
        WarnLabel = Loc.GetString("wf-encounter-zone-warn-label");
        AttackLabel = Loc.GetString("wf-encounter-zone-attack-label");
        SubscribeNetworkEvent<WFEncounterMarkersEvent>(ev =>
        {
            Markers = ev.Markers;
            _received = _timing.RealTime;
        });
    }

    /// <summary>
    /// Where a marker's ship is in map space: its centre of mass as the client has it when the grid is known, so zones
    /// and hull agree, else where the last update put it, moved on by its velocity.
    /// </summary>
    public Vector2 GetPosition(WFEncounterMarker marker)
    {
        if (TryGetEntity(marker.Grid, out var grid)
            && !TerminatingOrDeleted(grid)
            && TryComp<TransformComponent>(grid, out var xform)
            && xform.MapID == marker.Map
            && TryComp<PhysicsComponent>(grid, out var body))
        {
            return _transform.ToMapCoordinates(new EntityCoordinates(grid.Value, body.LocalCenter)).Position;
        }

        return marker.Position + marker.Velocity * MathF.Min(Elapsed, MaxExtrapolation);
    }
}
