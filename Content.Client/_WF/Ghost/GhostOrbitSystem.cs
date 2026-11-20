using Content.Shared._WF.Ghost;
using Content.Shared.Chat;
using Content.Shared.Ghost;
using Robust.Client.Player;

namespace Content.Client._WF.Ghost;

/// <summary>
/// Network side of the ghost orbit menu, plus the orbit links in chat.
/// </summary>
public sealed partial class GhostOrbitSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;

    public event Action<GhostOrbitTargetsEvent>? TargetsReceived;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<GhostOrbitTargetsEvent>(ev => TargetsReceived?.Invoke(ev));
    }

    public void RequestTargets()
    {
        EntityManager.System<Content.Client._WF.NpcCrew.WFCrewUiDiagnosticsSystem>().Request("ghost");
        RaiseNetworkEvent(new GhostOrbitRequestEvent());
    }

    public void Orbit(NetEntity target)
    {
        RaiseNetworkEvent(new GhostOrbitWarpEvent(target));
    }

    /// <summary>
    /// Puts an orbit link in front of a chat message with a source entity, while the local player is a ghost.
    /// </summary>
    public void AddChatLink(ChatMessage msg)
    {
        if (!msg.SenderEntity.Valid
            || _player.LocalEntity is not { } local
            || !HasComp<GhostComponent>(local)
            || GetNetEntity(local) == msg.SenderEntity)
            return;

        msg.WrappedMessage = $"[{GhostOrbitLinkTag.TagName}={msg.SenderEntity.Id}/] {msg.WrappedMessage}";
    }
}
