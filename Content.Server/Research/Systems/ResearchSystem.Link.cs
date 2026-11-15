using System.Linq;
using Content.Server.Power.EntitySystems;
using Content.Shared.Access.Components;
using Content.Shared.Research.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.UserInterface;

namespace Content.Server.Research.Systems;

public sealed partial class ResearchSystem
{
    private void InitializeLink()
    {
        SubscribeLocalEvent<ResearchServerComponent, BeforeActivatableUIOpenEvent>(OnLinkBeforeUiOpened);
        SubscribeLocalEvent<ResearchServerComponent, ResearchServerAuthorizeClientMessage>(OnAuthorizeClient);
        SubscribeLocalEvent<ResearchServerComponent, ResearchServerRevokeClientMessage>(OnRevokeClient);
    }

    private void OnLinkBeforeUiOpened(EntityUid uid, ResearchServerComponent component, BeforeActivatableUIOpenEvent args)
    {
        UpdateLinkInterface(uid, component);
    }

    private void OnAuthorizeClient(EntityUid uid, ResearchServerComponent component, ResearchServerAuthorizeClientMessage args)
    {
        if (!this.IsPowered(uid, EntityManager))
            return;

        if (TryComp<AccessReaderComponent>(uid, out var access) && !_accessReader.IsAllowed(args.Actor, uid, access))
        {
            _popup.PopupEntity(Loc.GetString("research-console-no-access-popup"), args.Actor);
            return;
        }

        if (IsServerShipLocked(uid))
        {
            _popup.PopupEntity(Loc.GetString("shuttle-console-locked"), args.Actor);
            return;
        }

        if (!TryGetEntity(args.Client, out var clientUid) || !HasComp<ResearchClientComponent>(clientUid))
            return;

        if (!CanClientAccessServer(clientUid.Value, uid, checkAuthorization: false))
            return;

        AuthorizeClient(uid, clientUid.Value, component);
        RegisterClient(clientUid.Value, uid, serverComponent: component);
        UpdateLinkInterface(uid, component);
    }

    private void OnRevokeClient(EntityUid uid, ResearchServerComponent component, ResearchServerRevokeClientMessage args)
    {
        if (!this.IsPowered(uid, EntityManager))
            return;

        if (TryComp<AccessReaderComponent>(uid, out var access) && !_accessReader.IsAllowed(args.Actor, uid, access))
        {
            _popup.PopupEntity(Loc.GetString("research-console-no-access-popup"), args.Actor);
            return;
        }

        if (IsServerShipLocked(uid))
        {
            _popup.PopupEntity(Loc.GetString("shuttle-console-locked"), args.Actor);
            return;
        }

        if (!TryGetEntity(args.Client, out var clientUid))
            return;

        RevokeClient(uid, clientUid.Value, component);
        UpdateLinkInterface(uid, component);
    }

    /// <summary>
    /// Deeded ships lock this console together with the helm and gunnery consoles.
    /// </summary>
    private bool IsServerShipLocked(EntityUid uid)
    {
        return TryComp<ShuttleConsoleLockComponent>(uid, out var lockComp)
               && _shuttleLock.GetEffectiveLockState(uid, lockComp);
    }

    public void AuthorizeClient(EntityUid server, EntityUid client, ResearchServerComponent? serverComponent = null)
    {
        if (!Resolve(server, ref serverComponent))
            return;

        serverComponent.AuthorizedClients.Add(client);
    }

    public void RevokeClient(EntityUid server, EntityUid client, ResearchServerComponent? serverComponent = null)
    {
        if (!Resolve(server, ref serverComponent))
            return;

        serverComponent.AuthorizedClients.Remove(client);

        if (serverComponent.Clients.Contains(client))
            UnregisterClient(client, server, serverComponent: serverComponent);
    }

    public bool IsClientAuthorized(EntityUid server, EntityUid client, ResearchServerComponent? serverComponent = null)
    {
        if (!Resolve(server, ref serverComponent, false))
            return false;

        if (!serverComponent.RestrictClientConnections)
            return true;

        return serverComponent.AuthorizedClients.Contains(client);
    }

    private void UpdateLinkInterface(EntityUid uid, ResearchServerComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        if (!_uiSystem.HasUi(uid, ResearchServerLinkUiKey.Key))
            return;

        var entries = new List<ResearchServerLinkEntry>();
        foreach (var client in GetLinkableClients(uid))
        {
            var authorized = IsClientAuthorized(uid, client, component);
            var connected = component.Clients.Contains(client);
            entries.Add(new ResearchServerLinkEntry(
                GetNetEntity(client),
                Name(client),
                authorized,
                connected));
        }

        entries = entries.OrderByDescending(e => e.Authorized).ThenBy(e => e.Name).ToList();
        _uiSystem.SetUiState(uid, ResearchServerLinkUiKey.Key, new ResearchServerLinkBoundInterfaceState(entries));
    }

    private List<EntityUid> GetLinkableClients(EntityUid server)
    {
        var result = new List<EntityUid>();
        var serverStation = _station.GetOwningStation(server);
        var serverGrid = Transform(server).GridUid;

        var query = EntityQueryEnumerator<ResearchClientComponent, TransformComponent>();
        while (query.MoveNext(out var clientUid, out _, out var xform))
        {
            if (serverStation != null)
            {
                if (_station.GetOwningStation(clientUid) != serverStation)
                    continue;
            }
            else if (serverGrid == null || xform.GridUid != serverGrid)
            {
                continue;
            }

            result.Add(clientUid);
        }

        return result;
    }
}
