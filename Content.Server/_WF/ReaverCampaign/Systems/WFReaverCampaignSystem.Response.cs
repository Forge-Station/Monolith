using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.ReaverCampaign.Components;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    /// <summary>How soon a stronghold may send another sortie pack after one.</summary>
    private static readonly TimeSpan SortieCooldown = TimeSpan.FromMinutes(5);

    /// <summary>How far from its stronghold a sortie pack or a reinforcement that broke away may chase before it is called in.</summary>
    private const float SortieLeash = 6000f;

    /// <summary>
    /// A stronghold's answer to a fresh hit from a player ship: the patrols within two cells are called back to circle
    /// it, and with no reinforcement docked to break away a sortie pack is sent against the ship that hit it hardest,
    /// one at a time, five minutes apart.
    /// </summary>
    private void RespondToHit(Entity<WFReaverStrongholdComponent, WFEncounterComponent> stronghold, IReadOnlyCollection<EntityUid> fresh)
    {
        var (uid, comp, encounter) = stronghold;
        if (RecallPatrols(stronghold) > 0)
            comp.Recalled = true;

        var now = _timing.CurTime;
        if (!Running || Held || comp.Reinforcements.Count > 0 || comp.Sortie != null || now < comp.NextSortie
            || Campaign is not { } campaign || !encounter.Ships.TryGetValue(comp.Lead, out var lead) || TerminatingOrDeleted(lead.Grid)
            || PickAttacker(comp, encounter, lead.Grid, fresh) is not { } attacker)
            return;

        var at = _transform.GetWorldPosition(attacker);
        if (Vector2.Distance(at, _transform.GetWorldPosition(lead.Grid)) > AttackerLeash)
            return;

        comp.NextSortie = now + PatrolRetry;
        if (!TryLaunchPatrol(uid, out var pack) || !TryComp<WFReaverPatrolComponent>(pack, out var patrol)
            || !TryComp<WFEncounterComponent>(pack, out var packEncounter))
            return;

        patrol.Sortie = true;
        patrol.Route = new List<Vector2>();
        if (!StartIntercept(campaign, (pack, patrol, packEncounter), attacker, at, new List<Vector2>()))
        {
            _encounters.End(pack);
            return;
        }

        comp.Sortie = pack;
        comp.NextSortie = now + SortieCooldown;
        Log.Info($"Reaver stronghold {ToPrettyString(uid)} sent a sortie pack {ToPrettyString(pack)} against {ToPrettyString(attacker)}.");
    }
}
