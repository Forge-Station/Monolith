using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Server._WF.Remade.Elites;

/// <summary>Tells everyone on the Undying's grid that it has come: a sound they all hear, and a chill.</summary>
public sealed partial class WFUndyingSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFUndyingComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<WFUndyingComponent> ent, ref MapInitEvent args)
    {
        Announce(ent);
    }

    /// <summary>Plays the arrival to every player on the same grid, or the same map if it's loose in space.</summary>
    public void Announce(Entity<WFUndyingComponent> ent)
    {
        var xform = Transform(ent);
        var filter = Filter.Empty();
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } body || TerminatingOrDeleted(body))
                continue;

            var there = Transform(body);
            var near = xform.GridUid != null ? there.GridUid == xform.GridUid : there.MapID == xform.MapID;
            if (!near)
                continue;

            filter.AddPlayer(session);
            ent.Comp.HeardBy.Add(body);
            _popup.PopupEntity(Loc.GetString("wf-remade-elite-undying-arrives"), body, body, PopupType.LargeCaution);
        }

        _audio.PlayGlobal(ent.Comp.Arrival, filter, true, AudioParams.Default.WithVolume(ent.Comp.Volume));
    }
}
