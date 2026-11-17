using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Server.Shuttles.Systems;
using Content.Shared._Forge.Bss;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;

namespace Content.Server._Forge.Bss;

public sealed class BssVoidDiskSystem : EntitySystem
{
    [Dependency] private readonly BssWorldSystem _world = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly ShuttleConsoleSystem _consoles = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BssVoidDiskComponent, UseInHandEvent>(OnUseInHand);
    }

    private void OnUseInHand(Entity<BssVoidDiskComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        var fromHint = TryGetCurrentSystem(args.User, out var current) ? current : null;
        if (!_world.TryUnlockHiddenRoute(fromHint, ent.Comp.TargetSystem, out var fromId, out var toId, out var reason))
        {
            _popup.PopupEntity(Loc.GetString(reason), args.User, args.User, PopupType.MediumCaution);
            args.Handled = true;
            return;
        }

        args.Handled = true;
        QueueDel(ent.Owner);

        var fromName = ResolveName(fromId);
        var toName = ResolveName(toId);
        _popup.PopupEntity(
            Loc.GetString("bss-void-disk-success", ("from", fromName), ("to", toName)),
            args.User,
            args.User,
            PopupType.Medium);
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("bss-void-disk-announcement", ("from", fromName), ("to", toName)),
            playSound: true,
            colorOverride: Color.FromHex("#8a6cff"));
        _consoles.RefreshShuttleConsoles();
    }

    private bool TryGetCurrentSystem(EntityUid user, out string systemId)
    {
        systemId = string.Empty;
        var mapUid = Transform(user).MapUid;
        if (mapUid == null || !TryComp<BssSectorMapComponent>(mapUid.Value, out var sector))
            return false;

        systemId = sector.Sector;
        return !string.IsNullOrEmpty(systemId);
    }

    private string ResolveName(string id)
    {
        return _world.TryGetNetwork(out var network)
            ? network.GetSystem(id)?.Name ?? id
            : id;
    }
}
