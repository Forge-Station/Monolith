using System;
using Content.Shared.FixedPoint;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._WF.Xenonids.Construction.Tunnel;

/// <summary>
/// All xenos of the same hive of the tunnel may select a different tunnel, that is also from their hive,
/// to go to, everyone else are sent to a random tunnel 
/// </summary>
[RegisterComponent]
public sealed partial class XenoTunnelComponent : Component
{
    public const string ContainedMobsContainerId = "rmc_xeno_tunnel_mob_container";

    [DataField]
    public int MaxMobs = 3;

    /// How long it takes to enter this tunnel

    [DataField]
    public TimeSpan SmallXenoEnterDelay = TimeSpan.FromSeconds(1);

    [DataField]
    public TimeSpan StandardXenoEnterDelay = TimeSpan.FromSeconds(4);

    [DataField]
    public TimeSpan LargeXenoEnterDelay = TimeSpan.FromSeconds(12);

    /// How long it takes to move from this tunnel to another one

    [DataField]
    public TimeSpan SmallXenoMoveDelay = TimeSpan.FromSeconds(1);

    [DataField]
    public TimeSpan StandardXenoMoveDelay = TimeSpan.FromSeconds(2);

    [DataField]
    public TimeSpan LargeXenoMoveDelay = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Total explosion damage at the tunnel's tile that collapses it. A C4 deals over ten thousand there and a
    /// grenade's centre several hundred, so only the stray edge of a blast falls under it.
    /// </summary>
    [DataField]
    public FixedPoint2 ExplosionCollapseDamage = 200;

    /// <summary>How long a shovel or entrenching tool takes to fill the tunnel, unless the tool says otherwise.</summary>
    [DataField]
    public TimeSpan ShovelFillDelay = TimeSpan.FromSeconds(30);
}
