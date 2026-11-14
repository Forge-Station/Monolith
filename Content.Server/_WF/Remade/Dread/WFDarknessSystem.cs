using System.Numerics;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Monolith;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Remade;
using Content.Shared.Shuttles.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Dread;

/// <summary>What a Monolith has put out on its ship and when it may put out more.</summary>
[RegisterComponent]
public sealed partial class WFDarknessComponent : Component
{
    [ViewVariables]
    public TimeSpan Next;

    /// <summary>Bulbs earned and not yet broken, so a slow rate still breaks one now and then.</summary>
    [ViewVariables]
    public float Budget;

    /// <summary>How many bulbs it has broken in all. Tests read it.</summary>
    [ViewVariables]
    public int Broken;
}

/// <summary>
/// The dark. From Harvest a Monolith breaks the lights on its own ship and on every ship docked to it, a few at a time and
/// never more than the stage allows: a little at Harvest, a lot at Rapture, and at the Crescendo most of the ship. Lights near the
/// stone go first. It breaks bulbs with the stock light system, so they pop with the stock sound and crew can replace them, and
/// the stone breaks them again.
/// </summary>
public sealed partial class WFDarknessSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPoweredLightSystem _lights = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private WFCorruptionSystem _corruption = default!;

    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(2);

    /// <summary>The most bulbs one stone breaks in one tick, whatever it has earned.</summary>
    private const int MaxPerTick = 4;

    private readonly HashSet<EntityUid> _grids = new();
    private readonly Stack<EntityUid> _walk = new();
    private readonly List<(float Key, EntityUid Light, PoweredLightComponent Comp)> _whole = new();

    /// <summary>The share of a ship's lit fixtures that the stone keeps dark at a stage.</summary>
    public static float DarkShare(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Harvest => 0.12f,
            WFMonolithStage.Rapture => 0.45f,
            WFMonolithStage.Crescendo => 0.92f,
            _ => 0f,
        };
    }

    /// <summary>How many bulbs a second the stone may break at a stage.</summary>
    public static float BreakRate(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Harvest => 0.12f,
            WFMonolithStage.Rapture => 0.5f,
            WFMonolithStage.Crescendo => 1.5f,
            _ => 0f,
        };
    }

    /// <summary>How loosely the stone picks: 1 is almost anywhere, 0 is strictly nearest first.</summary>
    private static float Spread(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Harvest => 0.9f,
            WFMonolithStage.Rapture => 0.6f,
            _ => 0.25f,
        };
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFMonolithComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var monolith, out var xform))
        {
            if (monolith.Stage < WFMonolithStage.Harvest || xform.GridUid is not { } grid)
                continue;

            var dark = EnsureComp<WFDarknessComponent>(uid);
            if (now < dark.Next)
                continue;

            dark.Next = now + TickRate;
            Break((uid, dark), monolith.Stage, grid, _transform.GetWorldPosition(xform));
        }
    }

    /// <summary>The ship and every ship docked to it, to any depth. Unlike the shuttle system's walk it makes no FTL locks and skips no dock.</summary>
    private void CollectDocked(EntityUid home)
    {
        _grids.Clear();
        _walk.Clear();
        _walk.Push(home);
        _grids.Add(home);
        while (_walk.TryPop(out var grid))
        {
            foreach (var dock in _docking.GetDocks(grid))
            {
                if (!dock.Comp.Docked || dock.Comp.DockedWith is not { } mate)
                    continue;

                var next = _transform.GetParentUid(mate);
                if (next.IsValid() && _grids.Add(next))
                    _walk.Push(next);
            }
        }
    }

    private void Break(Entity<WFDarknessComponent> stone, WFMonolithStage stage, EntityUid home, Vector2 origin)
    {
        if (_corruption.IsSanctuaryAt(stone))
            return;

        var rate = BreakRate(stage);
        stone.Comp.Budget = Math.Min(stone.Comp.Budget + rate * (float) TickRate.TotalSeconds, Math.Max(1f, rate * 4f));

        CollectDocked(home);
        _grids.RemoveWhere(grid => _corruption.IsSanctuary(grid));

        var total = 0;
        var dark = 0;
        _whole.Clear();
        var spread = Spread(stage);
        var lights = EntityQueryEnumerator<PoweredLightComponent, TransformComponent>();
        while (lights.MoveNext(out var uid, out var light, out var xform))
        {
            if (xform.GridUid is not { } grid || !_grids.Contains(grid))
                continue;

            if (_lights.GetBulb(uid, light) is not { } bulb || !TryComp<LightBulbComponent>(bulb, out var bulbComp))
                continue;

            total++;
            if (bulbComp.State != LightBulbState.Normal)
            {
                dark++;
                continue;
            }

            // Nearest first, blurred by the spread so it isn't a clean ring.
            var distance = (_transform.GetWorldPosition(xform) - origin).Length();
            var key = distance * (1f + spread * (_random.NextFloat() * 2f - 1f)) + _random.NextFloat() * spread * 8f;
            _whole.Add((key, uid, light));
        }

        var want = (int) MathF.Ceiling(total * DarkShare(stage)) - dark;
        var count = Math.Min(Math.Min(want, Math.Min((int) stone.Comp.Budget, MaxPerTick)), _whole.Count);
        if (count <= 0)
            return;

        _whole.Sort((a, b) => a.Key.CompareTo(b.Key));
        for (var i = 0; i < count; i++)
        {
            if (!_lights.TryDestroyBulb(_whole[i].Light, _whole[i].Comp))
                continue;

            stone.Comp.Budget -= 1f;
            stone.Comp.Broken++;
        }
    }
}
