using Content.Shared._WF.Xenonids;
using Content.Shared._WF.Xenonids.Hive;

namespace Content.Server._WF.Xenonids.Hive;

/// <summary>
/// Gives every xeno a hive. RMC-14 created its hive from the distress-signal round rule, which Wolfgate doesn't have,
/// so the default hive is created the first time a hiveless xeno spawns and whenever a command asks for it.
/// </summary>
public sealed class WFXenoHiveBootstrapSystem : EntitySystem
{
    [Dependency] private XenoHiveSystem _hive = default!;

    public const string DefaultHiveName = "xenonid hive";

    public override void Initialize()
    {
        SubscribeLocalEvent<HiveMemberComponent, MapInitEvent>(OnMemberMapInit);
    }

    private void OnMemberMapInit(Entity<HiveMemberComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Hive != null || !HasComp<XenoComponent>(ent))
            return;

        _hive.SetHive(ent.AsNullable(), EnsureDefaultHive());
    }

    /// <summary>The hive named <see cref="DefaultHiveName"/>, created if it doesn't exist yet.</summary>
    public Entity<HiveComponent> EnsureDefaultHive()
    {
        if (_hive.GetHiveByName(DefaultHiveName) is { } hive)
            return hive;

        var uid = _hive.CreateHive(DefaultHiveName);
        return (uid, Comp<HiveComponent>(uid));
    }
}
