using System.Linq;
using Content.Shared._WF.Hailing;
using Content.Shared.Shuttles.Components;

namespace Content.Server._WF.Hailing;

/// <summary>
/// Hail services: UIs on the other ship's grid, relayed onto the caller's helm and driven by their own state and messages.
/// A hail responder offers them as answers to pick.
/// </summary>
public sealed partial class HailingSystem
{
    private void InitializeServices()
    {
        SubscribeLocalEvent<HailRelayComponent, BoundUserInterfaceMessageAttempt>(OnRelayAttempt);
        SubscribeLocalEvent<HailRelayComponent, BoundUIClosedEvent>(OnRelayClosed);
    }

    /// <summary>
    /// Opens a service's UI on a helm for the person on its call, if the service is on the ship at the other end.
    /// </summary>
    public bool OpenService(EntityUid helm, EntityUid service, EntityUid actor)
    {
        if (!TryComp<HailConsoleComponent>(helm, out var hail) || hail.State != HailState.Connected ||
            hail.OperatorUid != actor || hail.PeerGrid is not { } peerGrid || Transform(service).GridUid != peerGrid ||
            !TryComp<HailServiceComponent>(service, out var serviceComp) ||
            !_ui.TryGetInterfaceData(service, serviceComp.Key, out var serviceData) ||
            !TryComp<UserInterfaceComponent>(helm, out var helmUi))
        {
            return false;
        }

        var relay = EnsureComp<HailRelayComponent>(helm);

        // Never shadow one of the helm's own interfaces.
        var key = serviceComp.Key;
        if (_ui.HasUi(helm, key, helmUi) && !relay.RelayKeys.Contains(key))
            return false;

        if (relay.Links.TryGetValue(key, out var linked) && linked != service)
            _ui.CloseUi((helm, helmUi), key);

        // Inherits the helm's interaction range, so the window closes when the caller walks away.
        var data = new InterfaceData(serviceData);
        if (_ui.TryGetInterfaceData((helm, helmUi), ShuttleConsoleUiKey.Key, out var helmData))
            data.InteractionRange = helmData.InteractionRange;

        _ui.SetUi((helm, helmUi), key, data);
        relay.RelayKeys.Add(key);
        relay.Links[key] = service;
        relay.Mirrored.Remove(key);

        // Per-caller services build their state for this actor.
        if (serviceComp.PerCaller)
        {
            var ev = new HailServiceOpenedEvent(helm, Transform(helm).GridUid, actor);
            RaiseLocalEvent(service, ref ev);
        }
        else
        {
            RaiseLocalEvent(service, new BoundUIOpenedEvent(key, service, actor));
            MirrorStates((helm, relay));
        }

        _ui.OpenUi((helm, helmUi), key, actor);
        return true;
    }

    /// <summary>Whether a helm has a service window open over its call.</summary>
    public bool HasOpenService(EntityUid helm)
    {
        return TryComp<HailRelayComponent>(helm, out var relay) && relay.Links.Count > 0;
    }

    /// <summary>Has the far end of a helm's connected call speak a line to it. Returns false if there is no live call.</summary>
    public bool SayToCaller(EntityUid helm, string text)
    {
        if (!TryComp<HailConsoleComponent>(helm, out var hail) || hail.State != HailState.Connected ||
            hail.Peer is not { } peer || !TryComp<HailConsoleComponent>(peer, out var peerHail) ||
            peerHail.OperatorUid is not { } caller)
        {
            return false;
        }

        return Say(peer, caller, text);
    }

    /// <summary>Mirrors every relayed service's state onto its helm.</summary>
    private void UpdateRelays()
    {
        var query = EntityQueryEnumerator<HailRelayComponent>();

        while (query.MoveNext(out var uid, out var relay))
        {
            MirrorStates((uid, relay));
        }
    }

    /// <summary>Closes every relayed window on the helm.</summary>
    private void CloseRelays(Entity<HailRelayComponent> helm)
    {
        foreach (var key in helm.Comp.Links.Keys.ToList())
        {
            _ui.CloseUi(helm.Owner, key);
        }

        helm.Comp.Links.Clear();
        helm.Comp.Mirrored.Clear();
    }

    /// <summary>Copies each relayed service's current state onto the helm when it changes.</summary>
    private void MirrorStates(Entity<HailRelayComponent> helm)
    {
        foreach (var (key, service) in helm.Comp.Links)
        {
            if (TryComp<HailServiceComponent>(service, out var serviceComp) && serviceComp.PerCaller)
                continue;

            _ui.TryGetUiState<BoundUserInterfaceState>(service, key, out var state);
            if (helm.Comp.Mirrored.TryGetValue(key, out var mirrored) && ReferenceEquals(mirrored, state))
                continue;

            helm.Comp.Mirrored[key] = state;
            _ui.SetUiState(helm.Owner, key, state);
        }
    }

    /// <summary>Forwards a message sent to a relayed key to its service instead of the helm.</summary>
    private void OnRelayAttempt(Entity<HailRelayComponent> ent, ref BoundUserInterfaceMessageAttempt args)
    {
        if (!ent.Comp.RelayKeys.Contains(args.UiKey))
            return;

        if (!ent.Comp.Links.TryGetValue(args.UiKey, out var service) || TerminatingOrDeleted(service))
        {
            args.Cancel();
            return;
        }

        if (args.Message is OpenBoundInterfaceMessage)
            return;

        args.Cancel();
        var message = args.Message;
        message.Actor = args.Actor;
        message.Entity = GetNetEntity(service);
        message.UiKey = args.UiKey;
        RaiseLocalEvent(service, (object) message, true);
    }

    /// <summary>Tells the service its window closed, and drops the link once nobody has it open.</summary>
    private void OnRelayClosed(Entity<HailRelayComponent> ent, ref BoundUIClosedEvent args)
    {
        if (!ent.Comp.Links.TryGetValue(args.UiKey, out var service))
            return;

        if (!TerminatingOrDeleted(service))
            RaiseLocalEvent(service, new BoundUIClosedEvent(args.UiKey, service, args.Actor));

        if (_ui.IsUiOpen(ent.Owner, args.UiKey))
            return;

        ent.Comp.Links.Remove(args.UiKey);
        ent.Comp.Mirrored.Remove(args.UiKey);
    }
}
