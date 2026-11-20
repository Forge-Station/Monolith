using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Research.Systems;
using Content.Shared._WF.Xenobiology.Bounties;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.IdentityManagement;
using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Xenobiology;

/// <summary>
/// Xenobiology bounty consoles: each keeps its own board of bounties, refreshed on a timer, and pays research points to
/// its research server when one is handed in.
/// </summary>
public sealed partial class XenobiologyBountyConsoleSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private ResearchSystem _research = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<XenobiologyBountyConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<XenobiologyBountyConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<XenobiologyBountyConsoleComponent, XenobiologyBountyFulfillMessage>(OnFulfill);
        SubscribeLocalEvent<XenobiologyBountyConsoleComponent, XenobiologyBountySkipMessage>(OnSkip);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<XenobiologyBountyConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            if (console.NextRefresh > now)
                continue;

            console.NextRefresh = now + console.RefreshDelay;
            console.Bounties.Clear();
            FillBounties(console);
            UpdateState((uid, console));
        }
    }

    private void OnMapInit(Entity<XenobiologyBountyConsoleComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextRefresh = _timing.CurTime + ent.Comp.RefreshDelay;
        FillBounties(ent.Comp);
    }

    private void OnOpened(Entity<XenobiologyBountyConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateState(ent);
    }

    private void OnFulfill(Entity<XenobiologyBountyConsoleComponent> ent, ref XenobiologyBountyFulfillMessage args)
    {
        if (!TryGetBounty(ent.Comp, args.BountyId, out var bounty)
            || !ProtoMan.TryIndex(bounty.Bounty, out var proto)
            || proto.PointsAwarded <= 0)
            return;

        if (!IsBountyComplete(args.Actor, proto.Entries, out var handedIn)
            || !_research.TryGetClientServer(ent, out var server, out var serverComp))
        {
            Deny(ent);
            return;
        }

        foreach (var item in handedIn)
        {
            QueueDel(item);
        }

        _research.ModifyServerPoints(server.Value, (int) proto.PointsAwarded, serverComp);
        RemoveBounty(ent.Comp, bounty, XenobiologyBountyResult.Completed, args.Actor);
        _audio.PlayPvs(ent.Comp.FulfillSound, ent);
        UpdateState(ent);
    }

    private void OnSkip(Entity<XenobiologyBountyConsoleComponent> ent, ref XenobiologyBountySkipMessage args)
    {
        if (_timing.CurTime < ent.Comp.NextSkipTime || !TryGetBounty(ent.Comp, args.BountyId, out var bounty))
            return;

        if (TryComp<AccessReaderComponent>(ent, out var reader) && !_access.IsAllowed(args.Actor, ent, reader))
        {
            Deny(ent);
            return;
        }

        RemoveBounty(ent.Comp, bounty, XenobiologyBountyResult.Skipped, args.Actor);
        FillBounties(ent.Comp);
        ent.Comp.NextSkipTime = _timing.CurTime + ent.Comp.SkipDelay;
        UpdateState(ent);
    }

    private void Deny(Entity<XenobiologyBountyConsoleComponent> ent)
    {
        if (_timing.CurTime < ent.Comp.NextDenySoundTime)
            return;

        ent.Comp.NextDenySoundTime = _timing.CurTime + ent.Comp.DenySoundDelay;
        _audio.PlayPvs(ent.Comp.DenySound, ent);
    }

    /// <summary>
    /// Adds one of every bounty prototype the console doesn't already show.
    /// </summary>
    private void FillBounties(XenobiologyBountyConsoleComponent console)
    {
        foreach (var proto in ProtoMan.EnumeratePrototypes<XenobiologyBountyPrototype>())
        {
            if (console.Bounties.Any(b => b.Bounty == proto.ID))
                continue;

            int number;
            do
            {
                number = _random.Next(1000);
            } while (console.Bounties.Any(b => b.Id == $"{proto.IdPrefix}{number:D3}"));

            console.Bounties.Add(new XenobiologyBountyData(proto, number));
        }

        console.Bounties = console.Bounties
            .OrderBy(b => ProtoMan.TryIndex(b.Bounty, out var p) ? p.PointsAwarded : 0)
            .ToList();
    }

    private void RemoveBounty(XenobiologyBountyConsoleComponent console, XenobiologyBountyData bounty,
        XenobiologyBountyResult result, EntityUid actor)
    {
        var identity = new TryGetIdentityShortInfoEvent(null, actor);
        RaiseLocalEvent(identity);
        console.History.Add(new XenobiologyBountyHistoryData(bounty, result, _timing.CurTime, identity.Title));
        console.Bounties.Remove(bounty);
    }

    private static bool TryGetBounty(XenobiologyBountyConsoleComponent console, string id,
        [NotNullWhen(true)] out XenobiologyBountyData? bounty)
    {
        bounty = console.Bounties.FirstOrDefault(b => b.Id == id);
        return bounty != null;
    }

    /// <summary>
    /// Whether the actor carries everything the bounty asks for, and which items those are.
    /// </summary>
    public bool IsBountyComplete(EntityUid actor, IEnumerable<XenobiologyBountyItemEntry> entries, out HashSet<EntityUid> handedIn)
    {
        var available = new HashSet<EntityUid>();
        CollectContents(actor, available);
        available.Remove(actor);

        handedIn = new HashSet<EntityUid>();
        foreach (var entry in entries)
        {
            var count = 0;
            var picked = new List<EntityUid>();
            foreach (var item in available)
            {
                if (!_whitelist.IsValid(entry.Whitelist, item)
                    || entry.Blacklist != null && _whitelist.IsValid(entry.Blacklist, item))
                    continue;

                count += CompOrNull<StackComponent>(item)?.Count ?? 1;
                picked.Add(item);
                if (count >= entry.Amount)
                    break;
            }

            if (count < entry.Amount)
                return false;

            foreach (var item in picked)
            {
                available.Remove(item);
                handedIn.Add(item);
            }
        }

        return true;
    }

    private void CollectContents(EntityUid uid, HashSet<EntityUid> into)
    {
        into.Add(uid);
        if (!TryComp<ContainerManagerComponent>(uid, out var containers))
            return;

        foreach (var container in containers.Containers.Values)
        {
            foreach (var child in container.ContainedEntities)
            {
                CollectContents(child, into);
            }
        }
    }

    private void UpdateState(Entity<XenobiologyBountyConsoleComponent> ent)
    {
        var now = _timing.CurTime;
        _ui.SetUiState(ent.Owner, XenobiologyBountyUiKey.Key, new XenobiologyBountyConsoleState(
            ent.Comp.Bounties, ent.Comp.History, ent.Comp.NextSkipTime - now, ent.Comp.NextRefresh - now));
    }
}
