using Content.Server.Chat.Systems;
using Content.Shared.Chat; // Einstein Engines - Languages
using Content.Shared.Medical;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;

namespace Content.Server._Shitmed.DelayedDeath;

public partial class DelayedDeathSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private Content.Server._WF.Wolfmed.Life.WolfmedLifeSystem _wolfmedLife = default!; // WOLFGATE(Wolfmed): BRAIN
    [Dependency] private MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DelayedDeathComponent, TargetBeforeDefibrillatorZapsEvent>(OnDefibZap);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        using var query = EntityQueryEnumerator<DelayedDeathComponent, MobStateComponent>();
        while (query.MoveNext(out var ent, out var comp, out var mob))
        {
            // WOLFGATE(Wolfmed): BRAIN: a missing heart is cardiac arrest on a wound host, not a countdown to death.
            if (_wolfmedLife.OwnsDeath(ent))
                continue;

            comp.DeathTimer += frameTime;

            if (comp.DeathTimer >= comp.DeathTime && !_mobState.IsDead(ent, mob))
            {
                // go crit then dead so deathgasp can happen
                _mobState.ChangeMobState(ent, MobState.Critical, mob);
                _mobState.ChangeMobState(ent, MobState.Dead, mob);
            }
        }
    }

    private void OnDefibZap(Entity<DelayedDeathComponent> ent, ref TargetBeforeDefibrillatorZapsEvent args)
    {
        // can't defib someone without a heart or brain pal
        args.Cancel();

        _chat.TrySendInGameICMessage(args.Defib, Loc.GetString("defibrillator-missing-organs"),
            InGameICChatType.Speak, true);
    }
}
