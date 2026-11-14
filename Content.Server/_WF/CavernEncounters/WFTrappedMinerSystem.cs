using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Server.Stack;
using Content.Shared.Chat;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.CavernEncounters;

/// <summary>Frees a trapped miner when a rock of his collapse is dug out: he thanks his rescuers, leaves them spesos and walks off.</summary>
public sealed partial class WFTrappedMinerSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private WFCavernWalkerSystem _walker = default!;

    /// <summary>Nobody within this many tiles lets a freed miner vanish.</summary>
    private const float WatchRange = 20f;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        // While the rock still stands where it was; by its components' shutdown it has left the map.
        SubscribeLocalEvent<WFCavernCollapseRockComponent, EntityTerminatingEvent>(OnRockTerminating);
    }

    private void OnRockTerminating(Entity<WFCavernCollapseRockComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.Miner is not { } miner
            || TerminatingOrDeleted(miner)
            || !TryComp<WFTrappedMinerComponent>(miner, out var comp)
            || Transform(ent).MapUid is not { } map
            || TerminatingOrDeleted(map))
            return;

        comp.Rocks.Remove(ent);
        Free((miner, comp));
    }

    /// <summary>Frees a miner: once only, and only while he lives.</summary>
    public void Free(Entity<WFTrappedMinerComponent> miner)
    {
        if (miner.Comp.Freed || !_mobState.IsAlive(miner))
            return;

        miner.Comp.Freed = true;
        miner.Comp.GoneAt = _timing.CurTime + miner.Comp.LeaveTime;

        foreach (var rock in miner.Comp.Rocks)
        {
            if (TryComp<WFCavernCollapseRockComponent>(rock, out var stone))
                stone.Miner = null;
        }

        miner.Comp.Rocks.Clear();

        _chat.TrySendInGameICMessage(miner, Loc.GetString("wf-cavern-trapped-miner-thanks"), InGameICChatType.Speak,
            hideChat: false, hideLog: true, ignoreActionBlocker: true);

        if (miner.Comp.Reward > 0)
            _stack.SpawnMultiple("SpaceCash", miner.Comp.Reward, Transform(miner).Coordinates);

        var angle = _random.NextFloat(MathF.Tau);
        _walker.WalkTo(miner, Transform(miner).Coordinates.Offset(new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * miner.Comp.LeaveDistance));
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFTrappedMinerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var miner, out var xform))
        {
            if (miner.GoneAt is not { } gone || now < gone)
                continue;

            miner.GoneAt = now + TimeSpan.FromSeconds(5);
            if (!AnyPlayerNear(xform, WatchRange))
                QueueDel(uid);
        }
    }

    /// <summary>Whether a player stands within a range of a spot, on its map.</summary>
    private bool AnyPlayerNear(TransformComponent xform, float range)
    {
        var position = _transform.GetWorldPosition(xform);
        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out _, out _, out var other))
        {
            if (other.MapUid == xform.MapUid && Vector2.Distance(_transform.GetWorldPosition(other), position) <= range)
                return true;
        }

        return false;
    }
}
