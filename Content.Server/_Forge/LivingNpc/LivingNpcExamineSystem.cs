using Content.Shared._Forge.LivingNpc.Components;
using Content.Shared.Examine;

namespace Content.Server._Forge.LivingNpc;

public sealed partial class LivingNpcExamineSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LivingNpcComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<LivingNpcComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (ent.Comp.JobTitle is { } job)
        {
            args.PushMarkup(Loc.GetString("living-npc-examine-job", ("job", Loc.GetString(job))));
        }

        var mood = ent.Comp.Mood.DominantKey();
        args.PushMarkup(Loc.GetString($"living-npc-examine-mood-{mood}"));
        args.PushMarkup(Loc.GetString("living-npc-examine-intent",
            ("intent", Loc.GetString($"living-npc-intent-{ent.Comp.CurrentIntent.ToString().ToLowerInvariant()}"))));
    }
}
