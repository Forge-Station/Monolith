using Content.Shared._WF.Xenonids.Hive;
using Content.Shared.Popups;
using Content.Shared.UserInterface;

namespace Content.Shared._WF.Xenonids.Sensor;

/// <summary>Keeps a resin sensor's mass scanner closed to anyone but xenos of its hive.</summary>
public sealed partial class XenoSensorSystem : EntitySystem
{
    [Dependency] private SharedXenoHiveSystem _hive = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<XenoSensorComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
    }

    private void OnOpenAttempt(Entity<XenoSensorComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (CanUse(ent.Owner, args.User))
            return;

        args.Cancel();
        _popup.PopupClient(Loc.GetString("wf-xeno-sensor-not-hive"), ent, args.User);
    }

    /// <summary>Whether the user is a xeno of the sensor's hive.</summary>
    public bool CanUse(EntityUid sensor, EntityUid user)
    {
        return HasComp<XenoComponent>(user) && _hive.FromSameHive(user, sensor);
    }
}
