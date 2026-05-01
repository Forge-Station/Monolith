using System.Threading;

namespace Content.Server.GameTicking.Rules.Components;

/// <summary>
/// Configures the <see cref="InactivityTimeRestartRuleSystem"/> game rule.
/// </summary>
[RegisterComponent]
public sealed partial class MaxTimeRestartRuleComponent : Component
{
    /// <summary>
    /// Forge-Change-start
    /// Legacy relative timer: if non-zero, restart sequence starts after this delay.
    /// </summary>
    [DataField("roundMaxTime")]
    public TimeSpan RoundMaxTime = TimeSpan.Zero;

    /// <summary>
    /// Legacy post-round delay used by older tests and prototypes.
    /// </summary>
    [DataField("roundEndDelay")]
    public TimeSpan RoundEndDelay = TimeSpan.Zero;

    /// <summary>
    /// Restart interval, aligned to UTC hour boundaries.
    /// </summary>
    [DataField("restartInterval", required: true)]
    public TimeSpan RestartInterval = TimeSpan.FromHours(8);

    /// <summary>
    /// Time from evacuation call to round end.
    /// </summary>
    [DataField("evacuationCallDuration", required: true)]
    public TimeSpan EvacuationCallDuration = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Time spent in post-round before switching to lobby.
    /// </summary>
    [DataField("postRoundDuration", required: true)]
    public TimeSpan PostRoundDuration = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Time spent in lobby before the next round starts.
    /// </summary>
    [DataField("lobbyDuration", required: true)]
    public TimeSpan LobbyDuration = TimeSpan.FromMinutes(3);

    /// <summary>
    /// UTC hour modulo <see cref="RestartInterval"/> that anchors restart boundaries.
    /// For an 8-hour interval, <c>7</c> yields slots at 07:00, 15:00, and 23:00 UTC.
    /// </summary>
    [DataField]
    public int UtcSlotAnchorHour = 7;
    /// Forge-Change-end

    public CancellationTokenSource TimerCancel = new();
}
