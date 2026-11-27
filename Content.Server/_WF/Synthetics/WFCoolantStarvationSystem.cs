using Content.Server._WF.Wolfmed.Life;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._WF.Synthetics;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._WF.Synthetics;

/// <summary>
/// A chassis drained below Wolfmed's unconscious fluid line starves its positronic core, which fails in
/// <see cref="StarveTime"/>: the machine's version of bleeding out. Refilling the fluid stops it.
/// </summary>
public sealed class WFCoolantStarvationSystem : EntitySystem
{
    /// <summary>How long a full core lasts with the fluid below the line.</summary>
    public static readonly TimeSpan StarveTime = TimeSpan.FromMinutes(8);

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WarningInterval = TimeSpan.FromSeconds(20);

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WolfmedLifeSystem _life = default!;
    [Dependency] private OrganHealthSystem _organs = default!;

    private float _outLine;
    private TimeSpan _nextTick;
    private readonly Dictionary<EntityUid, TimeSpan> _nextWarning = new();

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, WolfmedCVars.ConsciousnessBloodOut, value => _outLine = value, true);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        if (now < _nextTick)
            return;

        _nextTick = now + TickInterval;
        foreach (var gone in _nextWarning.Keys)
        {
            if (TerminatingOrDeleted(gone))
                _nextWarning.Remove(gone);
        }

        var query = EntityQueryEnumerator<WFChassisComponent, WolfmedConsciousnessComponent>();
        while (query.MoveNext(out var uid, out _, out var consciousness))
        {
            if (consciousness.BloodFraction >= _outLine || _mobState.IsDead(uid) || _life.GetBrainOrgan(uid) is not { } core)
            {
                _nextWarning.Remove(uid);
                continue;
            }

            var loss = core.Comp.MaxHealth.Float() * (float) (TickInterval / StarveTime);
            _organs.ChangeHealth(core, -loss);

            if (_nextWarning.TryGetValue(uid, out var next) && now < next)
                continue;

            _nextWarning[uid] = now + WarningInterval;
            _popup.PopupEntity(Loc.GetString("wf-synth-coolant-starving"), uid, uid, PopupType.LargeCaution);
        }
    }
}
