using System.Numerics;
using Content.Shared._WF.CCVar;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Player;

namespace Content.Server._WF.Audio.InternetSound;

public sealed partial class InternetSoundSystem
{
    [Dependency] private SharedTransformSystem _xform = default!;

    private static readonly TimeSpan AudienceInterval = TimeSpan.FromSeconds(0.5);
    private TimeSpan _nextAudienceUpdate;

    private readonly record struct PaAudience(EntityUid Grid, MapId Map, Matrix3x2 Inverse, Box2 Bounds);

    private void UpdatePaAudience()
    {
        if (_tracks.Count == 0 || _timing.CurTime < _nextAudienceUpdate)
            return;

        _nextAudienceUpdate = _timing.CurTime + AudienceInterval;
        foreach (var track in _tracks.Values)
        {
            if (track.Transfer == null)
                continue;

            if (track.IsPa)
                SendToPaAudience(track);
            else if (track.Source != null)
                SendToSourceAudience(track);
        }
    }

    private void SendToPaAudience(Track track)
    {
        if (track.Grid is not { } grid || TerminatingOrDeleted(grid)
            || !TryComp(grid, out MapGridComponent? mapGrid)
            || !TryComp(grid, out TransformComponent? gridTransform)
            || gridTransform.MapID == MapId.Nullspace)
            return;

        // Transform into grid space so a rotated or very long ship uses its hull, not a radius at its centre.
        // Calculate once per active track, rather than per player or per speaker/player pair.
        var margin = _cfg.GetCVar(InternetSoundCVars.PaPrefetchMargin);
        margin = float.IsFinite(margin) ? Math.Clamp(margin, 0f, 1024f) : 64f;
        var audience = new PaAudience(grid, gridTransform.MapID, _xform.GetInvWorldMatrix(gridTransform),
            mapGrid.LocalAABB.Enlarged(_shipPa.GetMaximumSpeakerRange(grid) + margin));

        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.InGame || track.Recipients.Contains(session.Channel))
                continue;

            if (IsPaRecipient(session, audience))
                SendOnce(track, session);
        }
    }

    private bool IsPaRecipient(ICommonSession session, PaAudience audience)
    {
        if (IsNearShip(session.AttachedEntity, audience))
            return true;

        // Remote cameras and other server-authorized views can place the listener away from their body.
        foreach (var viewer in session.ViewSubscriptions)
        {
            if (IsNearShip(viewer, audience))
                return true;
        }

        return false;
    }

    private bool IsNearShip(EntityUid? viewer, PaAudience audience)
    {
        if (!TryComp(viewer, out TransformComponent? transform) || TerminatingOrDeleted(viewer.Value))
            return false;

        if (transform.MapID == audience.Map && transform.GridUid == audience.Grid)
            return true;

        if (Contains(transform, Vector2.Zero))
            return true;

        // Eye targets and offsets are also used for vehicles and remote control. Include both body and eye
        // to prefetch conservatively when switching views; do not trust client-supplied track requests.
        if (!TryComp(viewer, out EyeComponent? eye))
            return false;

        if (eye.Target is { } target && !TerminatingOrDeleted(target)
            && TryComp(target, out TransformComponent? targetTransform))
            transform = targetTransform;

        return Contains(transform, eye.Offset);

        bool Contains(TransformComponent position, Vector2 offset)
        {
            return position.MapID == audience.Map
                   && audience.Bounds.Contains(Vector2.Transform(_xform.GetWorldPosition(position) + offset, audience.Inverse));
        }
    }

    private void SendOnce(Track track, ICommonSession session)
    {
        if (track.Transfer is not { } transfer || !track.Recipients.Add(session.Channel))
            return;

        // Keep the asset on this connection until Drop, even if its listener leaves the audience.
        // Mark before queueing so repeated checks cannot queue duplicate downloads.
        Send(session.Channel, transfer);
    }
}
