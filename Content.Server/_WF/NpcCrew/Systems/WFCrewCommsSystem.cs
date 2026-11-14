using Content.Shared._WF.NpcCrew;
using System.Linq;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Radio;
using Content.Server.Radio.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radio.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Shares sightings only with crew whose equipped radios actually receive the report.</summary>
public sealed partial class WFCrewCommsSystem : EntitySystem
{
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    private (EntityUid Sender, EntityUid Target, EntityUid Radio, WFRadioLine? Incident)? _report;
    private readonly Dictionary<(EntityUid Grid, string Group, WFRadioLine? Kind, EntityUid Subject), TimeSpan> _incidents = new();
    private readonly List<EntityUid> _stale = new();
    private TimeSpan _nextPrune;

    /// <summary>The crews' own channel: what they tell each other stays off the channels players listen to.</summary>
    public const string Intercom = "WFCrewIntercom";

    /// <summary>The radio channel a company's ships keep their own traffic on, if it has one.</summary>
    public static string? FactionChannel(string? company)
    {
        // TSF Comms for the Federation; the Dynasty's people carry the Vanguard channel.
        return WFCompanyFamily.Of(company) switch
        {
            "TSF" => "Nfsd",
            "PDV" => "Freelance",
            "USSP" => "Ussp",
            _ => null,
        };
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCrewRadioComponent, RadioReceiveEvent>(OnReceive);
    }

    /// <summary>Whether a recent sighting reached this crew member over radio.</summary>
    public bool Knows(EntityUid uid, EntityUid target) => TryComp<WFCrewComponent>(uid, out var crew)
        && crew.RadioSightings.TryGetValue(target, out var until) && _timing.CurTime < until;

    /// <summary>
    /// Relays a witnessed contact or incident over the crew intercom. One line goes out per crew, kind and subject every ten
    /// seconds; true when this call sent it or an earlier witness already had, false when this crewman could not.
    /// </summary>
    public bool Report(EntityUid sender, EntityUid target, WFRadioLine? incident = null)
    {
        if (!TryComp<WFCrewComponent>(sender, out var crew) || !crew.ShareAlerts || !_mobs.IsAlive(sender)
            || incident == null && (_timing.CurTime < crew.NextReport
                || !EntityManager.System<WFCrewSecuritySystem>().IsBoardingCandidate(target))
            || !_inventory.TryGetSlotEntity(sender, "ears", out var headset)
            || !TryComp<HeadsetComponent>(headset, out var transmitter) || !transmitter.Enabled
            || !HasComp<WFCrewRadioComponent>(headset) || !TryComp<ActiveRadioComponent>(headset, out var active)
            || !active.Channels.Contains(Intercom))
            return false;
        // A crew that doesn't call for help keeps its losses off the air.
        if (!crew.CallsForHelp && incident is WFRadioLine.CaptainDown or WFRadioLine.HelmDown)
            return false;
        var key = (HomeGrid(sender, crew), crew.Group, incident, target);
        if (_incidents.TryGetValue(key, out var until) && _timing.CurTime < until)
            return true;
        _incidents[key] = _timing.CurTime + TimeSpan.FromSeconds(10);
        crew.NextReport = _timing.CurTime + TimeSpan.FromSeconds(10);
        var previous = _report;
        _report = (sender, target, headset.Value, incident);
        try
        {
            // Such a crew passes an attack on as a plain contact, never as a cry of hostile fire.
            var line = incident switch
            {
                WFRadioLine.Mayday when crew.CallsForHelp => "wf-crew-radio-contact-under-fire",
                WFRadioLine.CaptainDown or WFRadioLine.HelmDown => "wf-crew-radio-contact-down",
                _ => "wf-crew-radio-contact",
            };
            _radio.SendRadioMessage(sender, Loc.GetString(line, ("target", Name(target))), Intercom, headset.Value);
        }
        finally
        {
            _report = previous;
        }
        return true;
    }

    private EntityUid HomeGrid(EntityUid uid, WFCrewComponent crew) => crew.Post?.EntityId ?? Transform(uid).GridUid ?? EntityUid.Invalid;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextPrune)
            return;
        _nextPrune = now + TimeSpan.FromSeconds(1);
        foreach (var key in _incidents.Where(entry => entry.Value <= now).Select(entry => entry.Key).ToArray())
            _incidents.Remove(key);
        var query = EntityQueryEnumerator<WFCrewComponent>();
        while (query.MoveNext(out _, out var crew))
        {
            if (crew.RadioSightings.Count == 0)
                continue;
            _stale.Clear();
            foreach (var (seen, until) in crew.RadioSightings)
            {
                if (now >= until || TerminatingOrDeleted(seen))
                    _stale.Add(seen);
            }
            foreach (var seen in _stale)
                crew.RadioSightings.Remove(seen);
        }
    }

    private void OnReceive(Entity<WFCrewRadioComponent> ent, ref RadioReceiveEvent args)
    {
        if (_report is not { } report || args.MessageSource != report.Sender || args.RadioSource != report.Radio
            || args.Channel.ID != Intercom
            || !TryComp<HeadsetComponent>(ent, out var receiver) || !receiver.Enabled
            || !TryComp<WFCrewComponent>(report.Sender, out var sender))
            return;
        var wearer = Transform(ent).ParentUid;
        if (!TryComp<WFCrewComponent>(wearer, out var crew) || !crew.ShareAlerts || !_mobs.IsAlive(wearer)
            || crew.Group != sender.Group
            || HomeGrid(wearer, crew) != HomeGrid(report.Sender, sender)
            || !_inventory.TryGetSlotEntity(wearer, "ears", out var equipped) || equipped != ent.Owner)
            return;
        if (report.Incident is { } incident)
            EntityManager.System<WFRadioOperatorSystem>().ReceiveIncident(wearer, report.Target, incident);
        if (report.Incident is WFRadioLine.CaptainDown or WFRadioLine.HelmDown)
            return;
        crew.RadioSightings[report.Target] = _timing.CurTime + TimeSpan.FromSeconds(30);
        EntityManager.System<WFCrewSecuritySystem>().ReceiveSighting(wearer, report.Target);
    }
}
