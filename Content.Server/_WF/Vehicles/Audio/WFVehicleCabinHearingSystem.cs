using Content.Shared._WF.Vehicles.Core;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._WF.Vehicles.Audio;

/// <summary>
/// Keeps the world round a vehicle in view of the players in its cabin, so their client has the sounds out there to
/// play through the hull (WFVehicleCabinAudioSystem).
/// </summary>
public sealed class WFVehicleCabinHearingSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private ViewSubscriberSystem _views = default!;

    private const float Interval = 0.5f;

    private float _accumulator;
    private readonly Dictionary<ICommonSession, EntityUid> _listening = new();
    private readonly List<ICommonSession> _gone = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _accumulator += frameTime;
        if (_accumulator < Interval)
            return;
        _accumulator = 0f;

        foreach (var session in _players.Sessions)
        {
            EntityUid? vehicle = session.AttachedEntity is { } body && TryComp(body, out VehicleInteriorOccupantComponent? occupant)
                && Exists(occupant.Vehicle) ? occupant.Vehicle : null;
            _listening.TryGetValue(session, out var current);
            if (vehicle == current)
                continue;

            if (current.IsValid())
                Stop(session, current);
            if (vehicle is { } inside)
            {
                _views.AddViewSubscriber(inside, session);
                _listening[session] = inside;
            }
        }

        _gone.Clear();
        foreach (var (session, vehicle) in _listening)
        {
            if (session.Status == SessionStatus.Disconnected || !Exists(vehicle))
                _gone.Add(session);
        }
        foreach (var session in _gone)
            Stop(session, _listening[session]);
    }

    private void Stop(ICommonSession session, EntityUid vehicle)
    {
        _listening.Remove(session);
        if (Exists(vehicle))
            _views.RemoveViewSubscriber(vehicle, session);
    }
}
