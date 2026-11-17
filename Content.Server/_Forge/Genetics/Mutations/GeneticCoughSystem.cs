using Content.Server.Chat.Systems;
using Content.Shared._Forge.Genetics.Mutations;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Genetics.Mutations;

public sealed class GeneticCoughSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticCoughComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<GeneticCoughComponent> ent, ref ComponentStartup args)
    {
        ScheduleNext(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<GeneticCoughComponent>();
        while (query.MoveNext(out var uid, out var cough))
        {
            if (cough.NextIncident > _timing.CurTime)
                continue;

            _chat.TryEmoteWithChat(uid, cough.Emote, ignoreActionBlocker: true, forceEmote: true);
            ScheduleNext((uid, cough));
        }
    }

    private void ScheduleNext(Entity<GeneticCoughComponent> ent)
    {
        var delay = _random.NextFloat(ent.Comp.MinInterval, ent.Comp.MaxInterval);
        ent.Comp.NextIncident = _timing.CurTime + TimeSpan.FromSeconds(delay);
    }
}
