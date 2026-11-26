using Content.Shared.Examine;

namespace Content.Shared._WF.Remade;

/// <summary>Makes a Remade playing dead examine the way a corpse does, from any range, so crew find nothing odd about it.</summary>
public sealed class WFRemadeAppearsDeadSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFRemadeAppearsDeadComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<WFRemadeAppearsDeadComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup($"[color=red]{Loc.GetString("comp-mind-examined-dead", ("ent", ent.Owner))}[/color]");
    }
}
