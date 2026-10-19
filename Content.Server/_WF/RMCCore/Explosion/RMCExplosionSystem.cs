using Content.Server.Explosion.EntitySystems;
using Content.Shared._WF.RMCCore.Explosion;
using Robust.Shared.Map;

namespace Content.Server._WF.RMCCore.Explosion;

// WOLFGATE: RMC's scorch decals, grenade voicelines, wall-charge stick timers, random-timer examine and the
// active-timer relay are marine content and are cut.
public sealed partial class RMCExplosionSystem : SharedRMCExplosionSystem
{
    [Dependency] private ExplosionSystem _explosion = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TriggerEvent>(OnTrigger);
    }

    private void OnTrigger(TriggerEvent ev)
    {
        var rmcEv = new RMCTriggerEvent(ev.User, ev.Handled);
        RaiseLocalEvent(ev.Triggered, ref rmcEv);
        ev.Handled = rmcEv.Handled;
    }

    public override void QueueExplosion(
        MapCoordinates epicenter,
        string typeId,
        float totalIntensity,
        float slope,
        float maxTileIntensity,
        EntityUid? cause,
        float tileBreakScale = 1f,
        int maxTileBreak = int.MaxValue,
        bool canCreateVacuum = true,
        bool addLog = true)
    {
        _explosion.QueueExplosion(
            epicenter,
            typeId,
            totalIntensity,
            slope,
            maxTileIntensity,
            cause,
            tileBreakScale,
            maxTileBreak,
            canCreateVacuum,
            addLog
        );
    }

    public override void TriggerExplosive(EntityUid uid,
        bool delete = true,
        float? totalIntensity = null,
        float? radius = null,
        EntityUid? user = null)
    {
        _explosion.TriggerExplosive(uid, null, delete, totalIntensity, radius, user);
    }
}
