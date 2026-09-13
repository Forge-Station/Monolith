using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Surgery;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Genitals.Systems;

/// <summary>Anatomy surgery gates: the patient condition for the surgery list and steps, and the surgeon check raised by the IsSurgeryValid hook.</summary>
/// <remarks>Both sides run the checks; only the server shows the refusal popup and writes the admin log.</remarks>
public sealed partial class GenitalSurgeryConditionSystem : EntitySystem
{
    [Dependency] private GenitalConsentSystem _consent = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ISharedAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;

    /// <summary>A surgeon sees the refusal popup at most once per interval.</summary>
    private static readonly TimeSpan RefusalPopupInterval = TimeSpan.FromSeconds(2);

    /// <summary>The hook runs when a step is chosen and again when it completes, so one log per surgeon, patient and surgery per interval.</summary>
    private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);

    /// <summary>Above this many entries, expired ones are dropped before adding another.</summary>
    private const int SweepThreshold = 64;

    private readonly Dictionary<EntityUid, TimeSpan> _lastRefusal = new();
    private readonly Dictionary<(EntityUid Surgeon, EntityUid Patient, EntityUid Surgery), TimeSpan> _lastLog = new();
    private readonly List<EntityUid> _expiredRefusals = new();
    private readonly List<(EntityUid Surgeon, EntityUid Patient, EntityUid Surgery)> _expiredLogs = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SurgeryGenitalConsentConditionComponent, SurgeryValidEvent>(OnPatientValid);
        SubscribeLocalEvent<GenitalSurgeryComponent, SurgeryUserValidEvent>(OnSurgeonValid);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _lastRefusal.Clear();
        _lastLog.Clear();
    }

    /// <summary>The patient needs anatomy (only eligible bodies have it) and strict master consent; otherwise the surgery is not listed and its steps fail.</summary>
    private void OnPatientValid(Entity<SurgeryGenitalConsentConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (!_consent.AnatomyEnabled
            || !HasComp<GenitalsComponent>(args.Body)
            || !_consent.HasMaster(args.Body))
        {
            args.Cancelled = true;
        }
    }

    /// <summary>Refuses surgeons that CanOperate rejects. The server tells a refused surgeon and logs an accepted procedure.</summary>
    private void OnSurgeonValid(Entity<GenitalSurgeryComponent> ent, ref SurgeryUserValidEvent args)
    {
        if (args.Cancelled)
            return;

        if (!_consent.CanOperate(args.User, args.Body))
        {
            args.Cancelled = true;
            if (_net.IsServer)
                PopupRefusal(args.User);

            return;
        }

        if (_net.IsServer)
            LogProcedure(ent.Owner, args.User, args.Body);
    }

    /// <summary>One neutral popup to the surgeon only, rate-limited per surgeon.</summary>
    private void PopupRefusal(EntityUid surgeon)
    {
        var now = _timing.CurTime;
        if (_lastRefusal.TryGetValue(surgeon, out var last) && now - last < RefusalPopupInterval)
            return;

        Sweep(_lastRefusal, _expiredRefusals, now, RefusalPopupInterval);
        _lastRefusal[surgeon] = now;
        _popup.PopupEntity(Loc.GetString("wf-anatomy-surgery-refused"), surgeon, surgeon, PopupType.SmallCaution);
    }

    /// <summary>Shitmed does not log surgeries, so anatomy procedures are logged here, deduplicated per surgeon, patient and surgery.</summary>
    private void LogProcedure(EntityUid surgery, EntityUid surgeon, EntityUid patient)
    {
        var now = _timing.CurTime;
        var key = (surgeon, patient, surgery);
        if (_lastLog.TryGetValue(key, out var last) && now - last < LogInterval)
            return;

        Sweep(_lastLog, _expiredLogs, now, LogInterval);
        _lastLog[key] = now;

        var procedure = MetaData(surgery).EntityPrototype?.ID ?? Name(surgery);
        _adminLog.Add(LogType.WFAnatomy, LogImpact.Medium,
            $"{ToPrettyString(surgeon):surgeon} performs {procedure} on {ToPrettyString(patient):patient}");
    }

    /// <summary>Drops entries older than the interval once the map has grown past SweepThreshold.</summary>
    private static void Sweep<TKey>(Dictionary<TKey, TimeSpan> map, List<TKey> scratch, TimeSpan now, TimeSpan interval)
        where TKey : notnull
    {
        if (map.Count < SweepThreshold)
            return;

        foreach (var (key, time) in map)
        {
            if (now - time >= interval)
                scratch.Add(key);
        }

        foreach (var key in scratch)
        {
            map.Remove(key);
        }

        scratch.Clear();
    }
}
