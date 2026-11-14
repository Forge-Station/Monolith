using Content.Server._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>Keeps the server half of the explanation card current on every unconscious wound host.</summary>
// It holds a coarse bar of the brain's remaining rescue window (tenths, never seconds), whether somebody is doing
// CPR, whether an analyzer just read the body, and when a timed faint ends. The client draws the card from this and
// the cause prototype.
public sealed class WolfmedCardSystem : EntitySystem
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private WolfmedConsciousnessSystem _consciousness = default!; // playtest 3
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private WolfmedLifeSystem _life = default!;
    [Dependency] private WolfmedOverheatSystem _overheat = default!; // M4

    private TimeSpan _next;
    private readonly List<EntityUid> _stale = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + TickInterval;

        var query = EntityQueryEnumerator<WolfmedConsciousnessComponent>();
        while (query.MoveNext(out var body, out var consciousness))
        {
            if (consciousness.State == WolfmedConsciousness.Unconscious && !_mobState.IsDead(body))
                Refresh(body);
        }

        // A card left over on somebody awake or dead says nothing any more.
        _stale.Clear();
        var cards = EntityQueryEnumerator<WolfmedCardComponent, WolfmedConsciousnessComponent>();
        while (cards.MoveNext(out var body, out _, out var consciousness))
        {
            if (consciousness.State != WolfmedConsciousness.Unconscious || _mobState.IsDead(body))
                _stale.Add(body);
        }

        foreach (var body in _stale)
            RemComp<WolfmedCardComponent>(body);
    }

    /// <summary>Rereads the bar, CPR and the analyzer for one body. Public so a test can refresh it on demand.</summary>
    public void Refresh(EntityUid body)
    {
        var card = EnsureComp<WolfmedCardComponent>(body);
        var cpr = _life.InCpr(body);
        var examined = card.LastExamined is { } last && _timing.CurTime - last <
                       TimeSpan.FromSeconds(MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.CardExaminedSeconds)));

        sbyte reserve = -1;
        if (_life.InArrest(body))
        {
            var seconds = _life.GetBrainDeathSeconds(body);
            if (seconds is { } left)
            {
                // The bar is a share of what the brain had when this arrest was first read; CPR can fill it back up.
                if (card.Window <= 0f)
                    card.Window = MathF.Max(1f, left);

                reserve = (sbyte) Math.Clamp((int) MathF.Ceiling(10f * left / card.Window), 0, 10);
            }
            else
            {
                reserve = (sbyte) (_life.GetBrainActivity(body) > 0f ? 10 : 0);
            }
        }
        else
        {
            card.Window = 0f;

            // M4 (plan §3.11): in thermal shutdown the bar is what is left of the core.
            if (_overheat.InThermalShutdown(body) && _life.GetBrainOrgan(body) is { } core)
                reserve = (sbyte) Math.Clamp((int) MathF.Ceiling(10f * core.Comp.Fraction), 0, 10);
        }

        // Playtest 3: the countdown, from the window the faint alert's cooldown uses.
        var wake = _consciousness.GetWakeWindow(body);
        if (card.Reserve == reserve && card.Cpr == cpr && card.Examined == examined &&
            card.WakeStart == wake?.Start && card.WakeEnd == wake?.End)
            return;

        card.Reserve = reserve;
        card.Cpr = cpr;
        card.Examined = examined;
        card.WakeStart = wake?.Start;
        card.WakeEnd = wake?.End;
        Dirty(body, card);
    }

    /// <summary>An analyzer (or the pod's panel) has just read this body: the card says a medic is examining you.</summary>
    public void MarkExamined(EntityUid body)
    {
        if (!TryComp(body, out WolfmedConsciousnessComponent? consciousness) ||
            consciousness.State != WolfmedConsciousness.Unconscious || _mobState.IsDead(body))
            return;

        EnsureComp<WolfmedCardComponent>(body).LastExamined = _timing.CurTime;
        Refresh(body);
    }
}
