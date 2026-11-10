using System.Numerics;
using Robust.Server.Player;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    // Extra reach past the hull so edges stay loaded while a viewer closes in or the ship moves.
    private const float WolfgateShieldPvsMargin = 100f;
    private const float WolfgateShieldPvsInterval = 0.5f;

    [Dependency] private IPlayerManager _wfShieldPlayers = default!;

    private readonly Dictionary<EntityUid, WolfgateShieldPvsEntry> _wfShieldPvs = new();
    private readonly List<EntityUid> _wfShieldPvsExpired = new();
    private float _wfShieldPvsAccumulator;

    private sealed class WolfgateShieldPvsEntry(EntityUid grid)
    {
        public EntityUid Grid = grid;
        public float Radius;
        public readonly HashSet<ICommonSession> Sessions = new();
    }

    /// <summary>Sends a shield-side entity to nearby sessions on its map instead of everyone.</summary>
    private void AddWolfgateShieldPvs(EntityUid entity, EntityUid grid)
    {
        var entry = new WolfgateShieldPvsEntry(grid);
        _wfShieldPvs[entity] = entry;
        RefreshWolfgateShieldPvs(entity, entry);
    }

    /// <summary>Re-evaluates which sessions are close enough to need each tracked entity.</summary>
    private void UpdateWolfgateShieldPvs(float frameTime)
    {
        _wfShieldPvsAccumulator += frameTime;
        if (_wfShieldPvsAccumulator < WolfgateShieldPvsInterval)
            return;
        _wfShieldPvsAccumulator = 0f;

        _wfShieldPvsExpired.Clear();
        foreach (var (entity, entry) in _wfShieldPvs)
        {
            if (TerminatingOrDeleted(entity))
                _wfShieldPvsExpired.Add(entity);
            else
                RefreshWolfgateShieldPvs(entity, entry);
        }

        foreach (var entity in _wfShieldPvsExpired)
            _wfShieldPvs.Remove(entity);
    }

    private void RefreshWolfgateShieldPvs(EntityUid entity, WolfgateShieldPvsEntry entry)
    {
        if (!TryComp(entity, out TransformComponent? xform) || xform.MapUid == null)
            return;

        if (TryComp<MapGridComponent>(entry.Grid, out var mapGrid))
        {
            var box = mapGrid.LocalAABB;
            entry.Radius = (box.Center.Length() + box.Size.Length() * 0.75f + 5f * mapGrid.TileSize) + WolfgateShieldPvsMargin;
        }
        else if (entry.Radius <= 0f)
        {
            entry.Radius = WolfgateShieldPvsMargin * 2f;
        }

        var origin = _transformSystem.GetWorldPosition(xform);
        var rangeSq = entry.Radius * entry.Radius;
        foreach (var session in _wfShieldPlayers.Sessions)
        {
            var near = false;
            if (session.AttachedEntity is { } attached && TryComp(attached, out TransformComponent? viewer) &&
                viewer.MapID == xform.MapID)
            {
                near = (_transformSystem.GetWorldPosition(viewer) - origin).LengthSquared() <= rangeSq;
            }

            if (near)
            {
                if (entry.Sessions.Add(session))
                    _pvsSys.AddSessionOverride(entity, session);
            }
            else if (entry.Sessions.Remove(session))
            {
                _pvsSys.RemoveSessionOverride(entity, session);
            }
        }
    }
}
