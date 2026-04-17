using System.Threading;
using Content.Server.Chat.Managers;
using Content.Server.RoundEnd; // Forge-Change
using Content.Server.GameTicking.Rules.Components;
using Content.Shared.CCVar; // Forge-Change
using Content.Shared.GameTicking.Components;
using Robust.Shared.Configuration; // Forge-Change
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server.GameTicking.Rules;

public sealed class MaxTimeRestartRuleSystem : GameRuleSystem<MaxTimeRestartRuleComponent>
{
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!; // Forge-Change
    [Dependency] private readonly RoundEndSystem _roundEnd = default!; // Forge-Change

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GameRunLevelChangedEvent>(RunLevelChanged);
    }

    protected override void Started(EntityUid uid, MaxTimeRestartRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if(GameTicker.RunLevel == GameRunLevel.InRound)
            RestartTimer(component);
    }

    protected override void Ended(EntityUid uid, MaxTimeRestartRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        StopTimer(component);
    }

    public void RestartTimer(MaxTimeRestartRuleComponent component)
    {
        ConfigureLobbyDuration(component); // Forge-Change
        component.TimerCancel.Cancel();
        component.TimerCancel = new CancellationTokenSource();
        // Forge-Change-start
        if (component.RoundMaxTime > TimeSpan.Zero)
        {
            Timer.Spawn(component.RoundMaxTime, () => LegacyTimerFired(component), component.TimerCancel.Token);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var cycleLeadTime = component.EvacuationCallDuration + component.PostRoundDuration + component.LobbyDuration;
        var startAt = GetNextCycleStart(now, component.RestartInterval, cycleLeadTime);
        var delay = startAt - now;

        Timer.Spawn(delay, () => TimerFired(component), component.TimerCancel.Token);
        // Forge-Change-end
    }

    public void StopTimer(MaxTimeRestartRuleComponent component)
    {
        component.TimerCancel.Cancel();
    }
    // Forge-Change-start
    private void TimerFired(MaxTimeRestartRuleComponent component)
    {
        if (GameTicker.RunLevel != GameRunLevel.InRound)
        {
            RestartTimer(component);
            return;
        }

        _roundEnd.RequestRoundEnd(component.EvacuationCallDuration, null, false, "round-end-system-shuttle-auto-called-announcement");
        _chatManager.DispatchServerAnnouncement(Loc.GetString("rule-restarting-in-seconds", ("seconds", (int) component.EvacuationCallDuration.TotalSeconds)));

        Timer.Spawn(component.EvacuationCallDuration, () => BeginPostRound(component), component.TimerCancel.Token);
    }

    private void BeginPostRound(MaxTimeRestartRuleComponent component)
    {
        if (GameTicker.RunLevel != GameRunLevel.InRound)
            return;

        _roundEnd.EndRound(component.PostRoundDuration);
    }

    private void LegacyTimerFired(MaxTimeRestartRuleComponent component)
    {
        if (GameTicker.RunLevel != GameRunLevel.InRound)
            return;

        GameTicker.EndRound(Loc.GetString("rule-time-has-run-out"));
        var delay = component.RoundEndDelay == TimeSpan.Zero ? component.PostRoundDuration : component.RoundEndDelay;
        _chatManager.DispatchServerAnnouncement(Loc.GetString("rule-restarting-in-seconds", ("seconds", (int) delay.TotalSeconds)));
        Timer.Spawn(delay, () => GameTicker.RestartRound(), component.TimerCancel.Token);
    }

    private static DateTimeOffset GetNextCycleStart(DateTimeOffset nowUtc, TimeSpan restartInterval, TimeSpan cycleLeadTime)
    {
        var intervalHours = Math.Max(1, (int) Math.Round(restartInterval.TotalHours));
        var dayStart = new DateTimeOffset(nowUtc.Year, nowUtc.Month, nowUtc.Day, 0, 0, 0, TimeSpan.Zero);
        var elapsedHours = (int) (nowUtc - dayStart).TotalHours;
        var nextSlotHours = ((elapsedHours / intervalHours) + 1) * intervalHours;
        var nextBoundary = dayStart.AddHours(nextSlotHours);

        if (nextBoundary <= nowUtc)
            nextBoundary = nextBoundary.AddHours(intervalHours);

        var cycleStart = nextBoundary - cycleLeadTime;
        if (cycleStart <= nowUtc)
            cycleStart = cycleStart.AddHours(intervalHours);

        return cycleStart;
    }

    private void ConfigureLobbyDuration(MaxTimeRestartRuleComponent component)
    {
        var current = _cfg.GetCVar(CCVars.GameLobbyDuration);
        var target = (int) component.LobbyDuration.TotalSeconds;
        if (current != target)
            _cfg.SetCVar(CCVars.GameLobbyDuration, target);
    }
    // Forge-Change-end
    private void RunLevelChanged(GameRunLevelChangedEvent args)
    {
        var query = EntityQueryEnumerator<MaxTimeRestartRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var timer, out var gameRule))
        {
            if (!GameTicker.IsGameRuleActive(uid, gameRule))
                continue; // Forge-Change

            switch (args.New)
            {
                case GameRunLevel.InRound:
                    RestartTimer(timer);
                    break;
                case GameRunLevel.PreRoundLobby:
                case GameRunLevel.PostRound:
                    StopTimer(timer);
                    break;
            }
        }
    }
}
