using Content.Server.Ghost.Roles.Components;
using Content.Server.Mind;
using Content.Shared._WF.Xenonids.JoinXeno;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._WF.Xenonids.JoinXeno;

/// <summary>Lets any ghost claim a burst larva that no queued player has been offered.</summary>
public sealed partial class XenoLarvaRoleSystem : EntitySystem
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(1);

    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private LarvaQueueSystem _larvaQueue = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly List<EntityUid> _toOpen = new();
    private readonly List<EntityUid> _toClose = new();

    private TimeSpan _nextCheck;

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckEvery;

        _toOpen.Clear();
        _toClose.Clear();
        var query = EntityQueryEnumerator<LarvaQueuedComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            var hasRole = HasComp<GhostRoleComponent>(uid);
            if (_larvaQueue.IsReserved(uid))
            {
                if (hasRole)
                    _toClose.Add(uid);

                continue;
            }

            if (!hasRole &&
                !_container.IsEntityInContainer(uid) &&
                !_mobState.IsDead(uid) &&
                _mind.GetMind(uid) == null)
            {
                _toOpen.Add(uid);
            }
        }

        foreach (var larva in _toOpen)
        {
            EnsureComp<GhostTakeoverAvailableComponent>(larva);
            var role = EnsureComp<GhostRoleComponent>(larva);
            role.RoleName = "cm-job-name-xeno-larva";
            role.RoleDescription = "wf-xeno-larva-role-description";
        }

        foreach (var larva in _toClose)
        {
            RemCompDeferred<GhostRoleComponent>(larva);
        }
    }
}
