using Content.Shared._WF.Xenonids.Egg;
using Content.Shared._WF.Xenonids.Pheromones;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Player;

namespace Content.Shared._WF.Xenonids.Parasite;

// WOLFGATE: RMC's parasite AI (idle/active/dying, trap leaps, cannibalising) drives an NPC module that is not ported;
// the components stay as data and this part keeps only what players use: taking over a parasite and its flags.
public abstract partial class SharedXenoParasiteSystem
{
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    public void IntializeAI()
    {
        SubscribeLocalEvent<XenoParasiteComponent, PlayerAttachedEvent>(OnPlayerAdded);
        SubscribeLocalEvent<XenoParasiteComponent, PlayerDetachedEvent>(OnPlayerRemoved);
        SubscribeLocalEvent<ParasiteAIDelayAddComponent, ComponentStartup>(OnAIDelayAdded);

        SubscribeLocalEvent<XenoParasiteComponent, GetVerbsEvent<ActivationVerb>>(OnGetVerbs);

        SubscribeLocalEvent<ParasiteTiredOutComponent, MapInitEvent>(OnParasiteAIMapInit);
        SubscribeLocalEvent<ParasiteTiredOutComponent, UpdateMobStateEvent>(OnParasiteAIUpdateMobState,
            after: [typeof(MobThresholdSystem), typeof(SharedXenoPheromonesSystem)]);
    }

    private void OnPlayerAdded(Entity<XenoParasiteComponent> para, ref PlayerAttachedEvent args)
    {
        RemCompDeferred<ParasiteAIComponent>(para);
        RemCompDeferred<ParasiteAIDelayAddComponent>(para);
    }

    private void OnPlayerRemoved(Entity<XenoParasiteComponent> para, ref PlayerDetachedEvent args)
    {
        if (!TerminatingOrDeleted(para))
            EnsureComp<ParasiteAIDelayAddComponent>(para);
    }

    private void OnAIDelayAdded(Entity<ParasiteAIDelayAddComponent> para, ref ComponentStartup args)
    {
        para.Comp.TimeToAI = _timing.CurTime + para.Comp.DelayTime;
    }

    private void OnParasiteAIMapInit(Entity<ParasiteTiredOutComponent> dead, ref MapInitEvent args)
    {
        if (TryComp(dead, out MobStateComponent? mobState))
            _mobState.UpdateMobState(dead, mobState);
    }

    private void OnParasiteAIUpdateMobState(Entity<ParasiteTiredOutComponent> dead, ref UpdateMobStateEvent args)
    {
        args.State = MobState.Dead;
    }

    private void OnGetVerbs(Entity<XenoParasiteComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        var uid = args.User;

        // if it doesn't have an actor and we can't reach it then don't add the verb
        if (!HasComp<ActorComponent>(uid) || !HasComp<GhostComponent>(uid))
            return;

        if (!_mobState.IsAlive(ent) || _container.IsEntityInContainer(ent) ||
            !(HasComp<ParasiteAIComponent>(ent) || HasComp<ParasiteAIDelayAddComponent>(ent)))
            return;

        var parasiteVerb = new ActivationVerb
        {
            Text = Loc.GetString("rmc-xeno-egg-ghost-verb"),
            Act = () =>
            {
                _ui.TryOpenUi(ent.Owner, XenoParasiteGhostUI.Key, uid);
            },

            Impact = LogImpact.High,
        };

        args.Verbs.Add(parasiteVerb);
    }
}
