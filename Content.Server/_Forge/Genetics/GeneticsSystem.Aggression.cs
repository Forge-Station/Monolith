using Content.Server._Forge.Genetics.Components;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._Forge.Genetics;
using Content.Shared.Mind.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Forge.Genetics;

public sealed partial class GeneticsSystem
{
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;

    private static readonly ProtoId<NpcFactionPrototype> HostileFaction = "SimpleHostile";
    private const string HostileTask = "SimpleHostileCompound";
    private const float AggressionChance = 0.18f;
    private const float MorphAggressionChance = 0.4f;

    private void MaybeGoHostile(EntityUid uid, GenePrototype proto)
    {
        if (HasComp<GeneticAggressionComponent>(uid))
            return;

        if (HasComp<ActorComponent>(uid))
            return;

        if (TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind)
            return;

        var chance = proto.PolymorphPool.Count > 0 || proto.SpeciesTarget != null || proto.SpeciesForm
            ? MorphAggressionChance
            : AggressionChance;

        if (!_random.Prob(chance))
            return;

        var aggression = AddComp<GeneticAggressionComponent>(uid);
        aggression.SourceGene = proto.ID;

        var faction = EnsureComp<NpcFactionMemberComponent>(uid);
        aggression.PreviousFactions.UnionWith(faction.Factions);

        _npcFaction.ClearFactions((uid, faction), false);
        _npcFaction.AddFaction((uid, faction), HostileFaction);

        aggression.AddedHtn = !EnsureComp<HTNComponent>(uid, out var htn);
        if (!aggression.AddedHtn && htn.RootTask != null && !string.IsNullOrEmpty(htn.RootTask.Task))
            aggression.PreviousTask = htn.RootTask.Task;

        htn.RootTask = new HTNCompoundTask { Task = HostileTask };
        htn.Blackboard.SetValue(NPCBlackboard.Owner, uid);
        _npc.WakeNPC(uid, htn);
        _htn.Replan(htn);

        _popup.PopupEntity(Loc.GetString("genetics-aggression"), uid);
    }

    private void RestoreAggression(EntityUid uid, string geneId)
    {
        if (!TryComp<GeneticAggressionComponent>(uid, out var aggression))
            return;

        if (aggression.SourceGene != geneId)
            return;

        if (TryComp<NpcFactionMemberComponent>(uid, out var faction))
        {
            _npcFaction.ClearFactions((uid, faction), false);
            _npcFaction.AddFactions((uid, faction), aggression.PreviousFactions);
        }

        if (TryComp<HTNComponent>(uid, out var htn))
        {
            if (aggression.AddedHtn)
            {
                RemComp<HTNComponent>(uid);
            }
            else if (aggression.PreviousTask != null)
            {
                htn.RootTask = new HTNCompoundTask { Task = aggression.PreviousTask };
                _htn.Replan(htn);
            }
        }

        RemComp<GeneticAggressionComponent>(uid);
    }
}
