using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server._WF.Shuttles;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;

namespace Content.Server._Mono.Detection;

/// <summary>Copies a dock host's IFF flags onto a ship and takes back only the flags that host added.</summary>
public sealed class ApplyIFFFlagsToDockedShipsSystem : EntitySystem
{
    [Dependency] private SharedShuttleSystem _shuttle = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DockEvent>(OnDock);
        SubscribeLocalEvent<UndockEvent>(OnUndock);
    }

    private void OnDock(DockEvent args)
    {
        // The host can be either side: docking orders the pair by entity id.
        if (TryComp<ApplyIFFFlagsToDockedShipsComponent>(args.GridBUid, out var hostB))
            ApplyFlags(args.GridAUid, hostB, true);

        if (!TryComp<ApplyIFFFlagsToDockedShipsComponent>(args.GridAUid, out var hostA))
            return;

        ApplyFlags(args.GridBUid, hostA, true);
    }

    private void OnUndock(UndockEvent args)
    {
        if (TryComp<ApplyIFFFlagsToDockedShipsComponent>(args.GridBUid, out var hostB))
            ApplyFlags(args.GridAUid, hostB, false);

        if (!TryComp<ApplyIFFFlagsToDockedShipsComponent>(args.GridAUid, out var hostA))
            return;

        ApplyFlags(args.GridBUid, hostA, false);
    }

    /// <summary>
    /// Adds the host's flags, or on undock removes the ones no host the ship is still docked to still provides.
    /// Flags the ship had before docking stay.
    /// </summary>
    public void ApplyFlags(EntityUid gridUid, ApplyIFFFlagsToDockedShipsComponent host, bool applying = true)
    {
        if (applying)
        {
            var before = CompOrNull<IFFComponent>(gridUid)?.Flags ?? IFFFlags.None;
            _shuttle.AddIFFFlag(gridUid, host.Flags);
            var gained = (CompOrNull<IFFComponent>(gridUid)?.Flags ?? IFFFlags.None) & ~before;
            if (gained != IFFFlags.None)
                EnsureComp<DockedHostIffFlagsComponent>(gridUid).Added |= gained;
            return;
        }

        if (!TryComp<DockedHostIffFlagsComponent>(gridUid, out var tracked))
            return;

        var removed = tracked.Added & ~FlagsFromDockedHosts(gridUid);
        if (removed == IFFFlags.None)
            return;

        _shuttle.RemoveIFFFlag(gridUid, removed);
        tracked.Added &= ~removed;
        if (tracked.Added == IFFFlags.None)
            RemComp<DockedHostIffFlagsComponent>(gridUid);
    }

    /// <summary>Flags of every host the grid is still docked to, through any port.</summary>
    private IFFFlags FlagsFromDockedHosts(EntityUid gridUid)
    {
        var flags = IFFFlags.None;
        var query = EntityQueryEnumerator<DockingComponent, TransformComponent>();
        while (query.MoveNext(out _, out var dock, out var xform))
        {
            if (xform.GridUid != gridUid ||
                dock.DockedWith is not { } other ||
                !TryComp(other, out TransformComponent? otherXform) ||
                otherXform.GridUid is not { } hostGrid ||
                !TryComp<ApplyIFFFlagsToDockedShipsComponent>(hostGrid, out var host))
            {
                continue;
            }

            flags |= host.Flags;
        }

        return flags;
    }
}
