using Content.Server._WF.Wolfmed.Life; // WOLFGATE(Wolfmed): GAMEPLAY: wound-host overheating below.
using Content.Server.Temperature.Components;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Damage.Components;

namespace Content.Server._Goobstation.Temperature;

public sealed partial class KillOnOverheatSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mob = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WolfmedOverheatSystem _wolfmedOverheat = default!; // WOLFGATE(Wolfmed): GAMEPLAY

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<KillOnOverheatComponent, TemperatureComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out var comp, out var temp, out var mob))
        {
            if (mob.CurrentState == MobState.Dead
                || temp.CurrentTemperature < comp.OverheatThreshold
                || HasComp<GodmodeComponent>(uid))
                continue;

            // WOLFGATE(Wolfmed): GAMEPLAY: a wound host cannot die of a damage total, so overheating burns it
            // through the wound model instead of setting MobState.Dead here.
            if (_wolfmedOverheat.TryOverheat(uid, comp.OverheatPopup))
                continue;

            var msg = Loc.GetString(comp.OverheatPopup, ("name", Identity.Name(uid, EntityManager)));
            _popup.PopupEntity(msg, uid, PopupType.LargeCaution);
            _mob.ChangeMobState(uid, MobState.Dead, mob);
        }
    }
}
