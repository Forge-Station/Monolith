using Content.Server.Shuttles.Systems;
using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking.Components;

namespace Content.Server._Forge.Bss;

public sealed class BssVoidGateRule : StationEventSystem<BssVoidGateRuleComponent>
{
    [Dependency] private readonly BssWorldSystem _world = default!;
    [Dependency] private readonly ShuttleConsoleSystem _consoles = default!;

    protected override void Started(
        EntityUid uid,
        BssVoidGateRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (!_world.TryUnlockHiddenRoute(null, null, out var fromId, out var toId, out var reason))
        {
            Sawmill.Info($"BSS void gate event skipped: {reason}");
            return;
        }

        _world.TryGetNetwork(out var network);
        var fromName = network?.GetSystem(fromId)?.Name ?? fromId;
        var toName = network?.GetSystem(toId)?.Name ?? toId;

        ChatSystem.DispatchGlobalAnnouncement(
            Loc.GetString("bss-void-gate-event-announcement", ("from", fromName), ("to", toName)),
            playSound: true,
            colorOverride: Color.FromHex("#c4453c"));
        _consoles.RefreshShuttleConsoles();
    }
}
