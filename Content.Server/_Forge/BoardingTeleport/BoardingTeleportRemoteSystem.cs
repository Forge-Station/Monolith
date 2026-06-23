using Content.Server.Administration.Logs;
using Content.Server.DeviceLinking.Components;
using Content.Server.DeviceLinking.Systems;
using Content.Shared._Forge.BoardingTeleport.Components;
using Content.Shared.Database;
using Content.Shared.DeviceNetwork;
using Content.Shared.Interaction.Events;
using Robust.Shared.Player;

namespace Content.Server._Forge.BoardingTeleport;

/// <summary>
/// Passes the acting player through device-link payloads when a boarding remote is used.
/// </summary>
public sealed class BoardingTeleportRemoteSystem : EntitySystem
{
    public const string UserPayloadKey = "boardingTeleportUser";

    [Dependency] private readonly DeviceLinkSystem _link = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BoardingTeleportRemoteComponent, UseInHandEvent>(OnUseInHand, before: [typeof(SignallerSystem)]);
    }

    private void OnUseInHand(EntityUid uid, BoardingTeleportRemoteComponent _, UseInHandEvent args)
    {
        if (args.Handled || !TryComp<SignallerComponent>(uid, out var signaller))
            return;

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(args.User):actor} triggered boarding teleport remote {ToPrettyString(uid):tool}");

        var payload = new NetworkPayload
        {
            [UserPayloadKey] = GetNetEntity(args.User),
        };

        _link.InvokePort(uid, signaller.Port, payload);
        args.Handled = true;
    }
}
