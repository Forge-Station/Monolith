using Content.Shared._WF.CCVar;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._WF.BattleLoad;

/// <summary>Tracks where explosions are thick, so spawners can hold off near a big fight. Also holds the module's settings.</summary>
public sealed partial class WFBattleHeatSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>A cap on remembered explosions, past which the oldest are dropped.</summary>
    private const int MaxRemembered = 20000;

    private readonly Queue<(MapCoordinates At, TimeSpan Time)> _blasts = new();

    public bool MicroExplosions { get; private set; }
    public float MicroMaxTotal { get; private set; }
    public float MicroMaxTile { get; private set; }
    public float MergeDistance { get; private set; }
    public float MergeMaxTotal { get; private set; }

    private int _hotExplosions;
    private TimeSpan _hotWindow;
    private float _hotRadius;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _blasts.Clear());

        Subs.CVar(_cfg, BattleLoadCVars.MicroExplosions, value => MicroExplosions = value, true);
        Subs.CVar(_cfg, BattleLoadCVars.MicroMaxTotal, value => MicroMaxTotal = value, true);
        Subs.CVar(_cfg, BattleLoadCVars.MicroMaxTile, value => MicroMaxTile = value, true);
        Subs.CVar(_cfg, BattleLoadCVars.MergeDistance, value => MergeDistance = value, true);
        Subs.CVar(_cfg, BattleLoadCVars.MergeMaxTotal, value => MergeMaxTotal = value, true);
        Subs.CVar(_cfg, BattleLoadCVars.HotExplosions, value => _hotExplosions = value, true);
        Subs.CVar(_cfg, BattleLoadCVars.HotWindow, value => _hotWindow = TimeSpan.FromSeconds(value), true);
        Subs.CVar(_cfg, BattleLoadCVars.HotRadius, value => _hotRadius = value, true);
    }

    /// <summary>Counts an explosion at a point.</summary>
    public void Record(MapCoordinates at)
    {
        if (at.MapId == MapId.Nullspace)
            return;

        Prune();
        if (_blasts.Count >= MaxRemembered)
            _blasts.Dequeue();
        _blasts.Enqueue((at, _timing.CurTime));
    }

    /// <summary>Whether enough explosions went off near a point lately for it to count as a hot battle.</summary>
    public bool IsHot(MapCoordinates at, float extraRange = 0f)
    {
        Prune();
        if (_hotExplosions <= 0 || _blasts.Count < _hotExplosions)
            return false;

        var range = _hotRadius + extraRange;
        var rangeSquared = range * range;
        var count = 0;
        foreach (var (blast, _) in _blasts)
        {
            if (blast.MapId != at.MapId || (blast.Position - at.Position).LengthSquared() > rangeSquared)
                continue;

            if (++count >= _hotExplosions)
                return true;
        }

        return false;
    }

    private void Prune()
    {
        var cutoff = _timing.CurTime - _hotWindow;
        while (_blasts.TryPeek(out var oldest) && oldest.Time < cutoff)
        {
            _blasts.Dequeue();
        }
    }
}
