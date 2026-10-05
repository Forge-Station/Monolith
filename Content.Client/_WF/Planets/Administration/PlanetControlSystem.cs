using Content.Shared._WF.Planets.Administration;

namespace Content.Client._WF.Planets.Administration;

/// <summary>Fetches the world list for the admin Planet Control window and sends its changes.</summary>
public sealed class PlanetControlSystem : EntitySystem
{
    public event Action<List<PlanetControlInfo>>? ListReceived;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<PlanetControlListEvent>(ev => ListReceived?.Invoke(ev.Planets));
    }

    public void RequestList()
    {
        RaiseNetworkEvent(new PlanetControlListRequestEvent());
    }

    /// <summary>The server answers every change with a fresh list.</summary>
    public void Send(PlanetControlSetEvent request)
    {
        RaiseNetworkEvent(request);
    }
}
