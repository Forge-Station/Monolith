using System.Linq;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Systems;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._WF.Administration.GridPower;
using Content.Shared.Administration;
using Content.Shared.Power.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Server._WF.Administration.Systems;

/// <summary>
/// SS13's "Make all areas powered/unpowered" admin buttons, targeted at one grid or every grid.
/// </summary>
public sealed partial class GridPowerSystem : EntitySystem
{
    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private ApcSystem _apc = default!;

    private static readonly SoundSpecifier PowerOffSound = new SoundPathSpecifier("/Audio/Announcements/power_off.ogg");
    private static readonly SoundSpecifier PowerOnSound = new SoundPathSpecifier("/Audio/Announcements/power_on.ogg");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<GridPowerListRequestEvent>(OnListRequest);
    }

    private void OnListRequest(GridPowerListRequestEvent ev, EntitySessionEventArgs args)
    {
        if (_adminManager.HasAdminFlag(args.SenderSession, AdminFlags.Admin))
            SendList(args.SenderSession);
    }

    public void SendList(ICommonSession session)
    {
        RaiseNetworkEvent(new GridPowerListEvent(BuildList()), Filter.SinglePlayer(session));
    }

    /// <summary>
    /// Every grid with an APC or network battery, with breaker and charge totals.
    /// </summary>
    public List<GridPowerInfo> BuildList()
    {
        var grids = new Dictionary<EntityUid, GridPowerInfo>();

        var apcs = AllEntityQuery<ApcComponent, TransformComponent>();
        while (apcs.MoveNext(out _, out var apc, out var xform))
        {
            if (GetInfo(grids, xform) is not { } info)
                continue;

            info.Apcs++;
            if (apc.MainBreakerEnabled)
                info.ApcsOn++;
        }

        var batteries = AllEntityQuery<PowerNetworkBatteryComponent, BatteryComponent, TransformComponent>();
        while (batteries.MoveNext(out _, out _, out var battery, out var xform))
        {
            if (GetInfo(grids, xform) is not { } info)
                continue;

            info.Batteries++;
            info.Charge += battery.CurrentCharge;
            info.MaxCharge += battery.MaxCharge;
        }

        return grids.Values.ToList();
    }

    private GridPowerInfo? GetInfo(Dictionary<EntityUid, GridPowerInfo> grids, TransformComponent xform)
    {
        if (xform.GridUid is not { } grid)
            return null;

        if (grids.TryGetValue(grid, out var info))
            return info;

        info = new GridPowerInfo
        {
            Grid = GetNetEntity(grid),
            Name = Name(grid),
            Map = xform.MapUid is { } map ? Name(map) : string.Empty,
        };
        grids[grid] = info;
        return info;
    }

    /// <summary>
    /// Unpowered drains every SMES, substation and APC battery and switches APC breakers off, so crews can recover by
    /// flipping breakers like SS13's SMES output. Powered fills them, turns breakers on and re-enables output.
    /// </summary>
    /// <param name="powered">True for "all areas powered".</param>
    /// <param name="target">One grid, or null for every grid.</param>
    /// <param name="announce">Send the SS13 announcement to the people affected.</param>
    /// <returns>How many grids were changed.</returns>
    public int SetPower(bool powered, EntityUid? target, bool announce)
    {
        var affected = new HashSet<EntityUid>();

        var batteries = AllEntityQuery<PowerNetworkBatteryComponent, BatteryComponent, TransformComponent>();
        while (batteries.MoveNext(out var uid, out var netBattery, out var battery, out var xform))
        {
            if (!IsTarget(xform, target, out var grid))
                continue;

            _battery.SetCharge(uid, powered ? battery.MaxCharge : 0f, battery);
            if (powered)
                netBattery.CanDischarge = true;

            affected.Add(grid);
        }

        var apcs = AllEntityQuery<ApcComponent, PowerNetworkBatteryComponent, TransformComponent>();
        while (apcs.MoveNext(out var uid, out var apc, out var netBattery, out var xform))
        {
            if (!IsTarget(xform, target, out var grid))
                continue;

            // Set directly: ApcToggleBreaker would play a click on every APC at once.
            apc.MainBreakerEnabled = powered;
            netBattery.CanDischarge = powered;
            _apc.UpdateUIState(uid, apc, netBattery);

            affected.Add(grid);
        }

        if (announce && affected.Count > 0)
            Announce(powered, target, affected);

        return affected.Count;
    }

    private static bool IsTarget(TransformComponent xform, EntityUid? target, out EntityUid grid)
    {
        grid = xform.GridUid ?? EntityUid.Invalid;
        return xform.GridUid != null && (target == null || xform.GridUid == target);
    }

    /// <summary>
    /// SS13's power failure and restore announcements. Everyone hears the all-grids version; a single grid only tells people aboard.
    /// </summary>
    private void Announce(bool powered, EntityUid? target, HashSet<EntityUid> grids)
    {
        var place = target is { } grid ? Name(grid) : Loc.GetString("wf-grid-power-target-all");
        var message = Loc.GetString(powered ? "wf-grid-power-on-announcement" : "wf-grid-power-off-announcement", ("target", place));
        var sender = Loc.GetString(powered ? "wf-grid-power-on-sender" : "wf-grid-power-off-sender");

        var filter = target == null
            ? Filter.Broadcast()
            : Filter.Empty().AddWhere(session =>
                session.AttachedEntity is { } entity
                && Transform(entity).GridUid is { } onGrid
                && grids.Contains(onGrid));

        _chat.DispatchFilteredAnnouncement(filter, message, sender: sender, playSound: false);
        _audio.PlayGlobal(powered ? PowerOnSound : PowerOffSound, filter, true, AudioParams.Default.WithVolume(-4f));
    }
}
