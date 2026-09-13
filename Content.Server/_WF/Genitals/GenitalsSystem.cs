using Content.Shared._Common.Consent;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Systems;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.Genitals;

/// <summary>Server side of anatomy: owner request rate limit (latest wins for arousal), deferred consent reactions and the privacy filter.</summary>
public sealed partial class GenitalsSystem : SharedGenitalsSystem
{
    [Dependency] private IPlayerManager _player = default!;

    /// <summary>Bodies whose anatomy toggles changed; GenitalConsentChangedEvent is raised for them on the next update.</summary>
    private readonly HashSet<EntityUid> _pendingConsent = new();

    private readonly List<EntityUid> _consentBatch = new();

    /// <summary>Owner request budgets per player, by request type and slot: tokens left and when they were counted.</summary>
    private readonly Dictionary<NetUserId, Dictionary<(Type Request, int Slot), (double Tokens, TimeSpan Counted)>> _requestBudgets = new();

    /// <summary>Latest arousal value per player that the rate limit refused; Update applies it once the budget allows.</summary>
    private readonly Dictionary<NetUserId, byte> _deferredArousal = new();

    private readonly List<NetUserId> _arousalBatch = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ConsentComponent, EntityConsentToggleUpdatedEvent>(OnConsentToggleUpdated);
        _player.PlayerStatusChanged += OnPlayerStatusChanged;
        InitializePrivacy();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _player.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        ApplyDeferredArousal();

        if (_pendingConsent.Count == 0)
            return;

        // Copy first: a handler that changes consent again queues its body for the next update.
        _consentBatch.Clear();
        _consentBatch.AddRange(_pendingConsent);
        _pendingConsent.Clear();

        foreach (var body in _consentBatch)
        {
            if (TerminatingOrDeleted(body))
                continue;

            var ev = new GenitalConsentChangedEvent(body);
            RaiseLocalEvent(ref ev);
        }
    }

    private void OnConsentToggleUpdated(Entity<ConsentComponent> ent, ref EntityConsentToggleUpdatedEvent args)
    {
        // UpdateConsent assigns the new settings after raising this, so every reaction waits for the next update, except
        // the privacy check: its resend must land before the first PVS pass that allows a new viewer.
        var settings = Settings;
        if (args.ConsentToggleProtoId == settings.MasterConsent)
            OnMasterToggleUpdated(ent.Owner, args.NewState == "on");

        if (args.ConsentToggleProtoId == settings.MasterConsent
            || args.ConsentToggleProtoId == settings.StripConsent
            || args.ConsentToggleProtoId == settings.SurgeryConsent)
        {
            _pendingConsent.Add(ent.Owner);
        }
    }

    /// <summary>
    /// Token bucket per player, request type and slot: RequestBurst requests at once, then one per RequestInterval. Late
    /// packets are dispatched together, so a strict gap would drop an honest client's last request, usually the final one.
    /// </summary>
    protected override bool AllowRequest(ICommonSession session, Type request, int slot)
    {
        if (!_requestBudgets.TryGetValue(session.UserId, out var budgets))
        {
            budgets = new Dictionary<(Type, int), (double, TimeSpan)>();
            _requestBudgets[session.UserId] = budgets;
        }

        var settings = Settings;
        var burst = Math.Max(1, settings.RequestBurst);
        var now = Timing.CurTime;
        var key = (request, slot);

        double tokens = burst;
        if (budgets.TryGetValue(key, out var budget) && settings.RequestInterval > 0f)
            tokens = Math.Min(burst, budget.Tokens + (now - budget.Counted).TotalSeconds / settings.RequestInterval);

        if (tokens < 1)
        {
            budgets[key] = (tokens, now);
            return false;
        }

        budgets[key] = (tokens - 1, now);
        return true;
    }

    /// <summary>Latest wins: a value the rate limit refuses waits for the budget instead of being dropped, replacing any older one.</summary>
    protected override void HandleArousalRequest(ICommonSession session, byte value)
    {
        if (!AnatomyEnabled)
            return;

        if (!AllowRequest(session, typeof(AnatomySetArousalRequestEvent), NoRequestSlot))
        {
            _deferredArousal[session.UserId] = value;
            return;
        }

        _deferredArousal.Remove(session.UserId);
        SetOwnArousal(session, value);
    }

    /// <summary>Applies each waiting arousal value once its player's budget allows, with the same checks as a request.</summary>
    private void ApplyDeferredArousal()
    {
        if (_deferredArousal.Count == 0)
            return;

        _arousalBatch.Clear();
        _arousalBatch.AddRange(_deferredArousal.Keys);
        foreach (var userId in _arousalBatch)
        {
            if (!_player.TryGetSessionById(userId, out var session))
            {
                _deferredArousal.Remove(userId);
                continue;
            }

            if (!AllowRequest(session, typeof(AnatomySetArousalRequestEvent), NoRequestSlot))
                continue;

            var value = _deferredArousal[userId];
            _deferredArousal.Remove(userId);
            SetOwnArousal(session, value);
        }
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Disconnected)
            return;

        _requestBudgets.Remove(args.Session.UserId);
        _deferredArousal.Remove(args.Session.UserId);
        _anatomyViewers.Remove(args.Session.UserId);
    }
}
