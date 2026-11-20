using Content.Shared._WF.Barricades;
using Content.Shared._WF.RMCCore.Explosion;
using Content.Shared.Burial.Components;
using Content.Shared.Item.ItemToggle.Components;

namespace Content.Shared._WF.Xenonids.Construction.Tunnel;

public sealed partial class XenoTunnelSystem
{
    private const string DiggingQuality = "Digging";

    /// <summary>Whether both entities sit on the same grid.</summary>
    public bool SameGrid(EntityUid a, EntityUid b)
    {
        return Transform(a).GridUid is { } grid && Transform(b).GridUid == grid;
    }

    /// <summary>The hive's named tunnels on the origin tunnel's grid, other than the origin itself.</summary>
    public Dictionary<string, EntityUid> GetTravelDestinations(EntityUid origin)
    {
        var result = new Dictionary<string, EntityUid>();
        if (_hive.GetHive(origin) is not { } hive)
            return result;

        foreach (var (name, tunnel) in hive.Comp.HiveTunnels)
        {
            if (tunnel != origin && !TerminatingOrDeleted(tunnel) && SameGrid(origin, tunnel))
                result.Add(name, tunnel);
        }

        return result;
    }

    private void OnTunnelExplosion(Entity<XenoTunnelComponent> tunnel, ref ExplosionReceivedEvent args)
    {
        if (_net.IsClient || TerminatingOrDeleted(tunnel) || args.Damage.GetTotal() < tunnel.Comp.ExplosionCollapseDamage)
            return;

        CollapseTunnel(tunnel, "wf-xeno-tunnel-announce-blown", "wf-xeno-tunnel-blown-xeno-drop");
    }

    /// <summary>Whether this tool fills tunnels, and how long that takes. Only humans fill; an unfolded entrenching tool is needed.</summary>
    private bool TryGetFillDelay(Entity<XenoTunnelComponent> tunnel, EntityUid tool, EntityUid user, out TimeSpan delay)
    {
        delay = tunnel.Comp.ShovelFillDelay;

        if (HasComp<XenoComponent>(user))
            return false;

        TryComp(tool, out XenoTunnelFillerComponent? filler);
        if (filler == null && !HasComp<ShovelComponent>(tool) && !HasComp<EntrenchingToolComponent>(tool) &&
            !_tool.HasQuality(tool, DiggingQuality))
        {
            return false;
        }

        if (TryComp(tool, out ItemToggleComponent? toggle) && !toggle.Activated)
            return false;

        if (filler != null)
            delay = filler.FillDelay;

        return true;
    }
}
