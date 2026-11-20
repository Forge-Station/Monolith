using Content.Client.Administration.Managers;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Administration;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._WF.NpcCrew;

/// <summary>Exchanges crew setup requests with the permission-checked server endpoint.</summary>
public sealed partial class WFCrewSetupClientSystem : EntitySystem
{
    private const float WindowCrewsInterval = 2f;
    private const float RadarCrewsInterval = 5f;
    private const float GridListInterval = 10f;

    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IClientAdminManager _admin = default!;

    /// <summary>The live crews from the last reply, drawn on an admin's radar.</summary>
    public List<WFCrewSetupCrew> Crews { get; private set; } = new();

    /// <summary>Radar tag text for each crew in <see cref="Crews"/>, built once per reply.</summary>
    public Dictionary<(NetEntity Grid, string Group), string> Tags { get; } = new();
    private TimeSpan _radarUntil;
    public WFCrewSetupResponse? Preview { get; private set; }
    public TimeSpan PreviewUntil { get; private set; }
    public event Action<WFCrewSetupResponse>? Received;
    private float _pollTimer;
    private float _listTimer;
    private bool _polling;
    private int _nextRequestId;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var window = Received != null;
        var radar = _timing.RealTime < _radarUntil && _admin.HasFlag(AdminFlags.Spawn);
        if (!window && !radar)
        {
            _pollTimer = 0;
            _listTimer = 0;
            _polling = false;
            Crews.Clear();
            Tags.Clear();
            return;
        }
        if (!_polling)
        {
            _polling = true;
            if (!window)
                _pollTimer = RadarCrewsInterval;
        }
        _pollTimer += frameTime;
        _listTimer += frameTime;
        if (_pollTimer < (window ? WindowCrewsInterval : RadarCrewsInterval))
            return;
        _pollTimer = 0;
        if (window && _listTimer >= GridListInterval)
        {
            _listTimer = 0;
            Send(new WFCrewSetupRequest { Action = WFCrewSetupAction.List });
        }
        else
            Send(new WFCrewSetupRequest { Action = WFCrewSetupAction.Crews });
    }

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new WFCrewSetupOverlay(this, EntityManager, _timing));
        SubscribeNetworkEvent<WFCrewSetupOpenEvent>(ev =>
        {
            var window = new WFCrewSetupWindow();
            window.SelectCrew(ev.Grid, ev.Group);
            window.OpenCentered();
        });
        SubscribeNetworkEvent<WFCrewSetupResponse>(response =>
        {
            if (response.Action == WFCrewSetupAction.Preview)
            {
                Preview = response;
                PreviewUntil = _timing.CurTime + TimeSpan.FromSeconds(30);
            }
            Crews = response.Crews;
            RebuildTags();
            Received?.Invoke(response);
        });
    }

    private void RebuildTags()
    {
        Tags.Clear();
        foreach (var crew in Crews)
        {
            // Encounter crews have generated group names; the callsign is what a person would call the ship.
            var name = crew.Settings.Callsign.Length > 0 ? crew.Settings.Callsign : crew.Group;
            Tags[(crew.Grid, crew.Group)] = Loc.GetString("wf-crew-radar-tag", ("group", name), ("activity", crew.Activity));
        }
    }

    /// <summary>Keeps the crew list fresh while a radar is drawing it. Returns false for anyone who may not see crews.</summary>
    public bool WatchRadar()
    {
        if (!_admin.HasFlag(AdminFlags.Spawn))
            return false;
        _radarUntil = _timing.RealTime + TimeSpan.FromSeconds(3);
        return true;
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<WFCrewSetupOverlay>();
        base.Shutdown();
    }

    /// <summary>Sends an operation with a unique identifier for routing its eventual response.</summary>
    public int Send(WFCrewSetupRequest request)
    {
        request.RequestId = ++_nextRequestId;
        EntityManager.System<WFCrewUiDiagnosticsSystem>().Request("crew", request.Grid);
        RaiseNetworkEvent(request);
        return request.RequestId;
    }
}
