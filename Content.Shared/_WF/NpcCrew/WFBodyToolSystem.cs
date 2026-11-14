using Content.Shared.Mobs.Components;
using Content.Shared.Tools.Components;

namespace Content.Shared._WF.NpcCrew;

/// <summary>
/// A mob that can pry is only a tool in its own hands. Dragged onto a machine by someone else, a crewman's body
/// would otherwise pry it apart as a crowbar does.
/// </summary>
public sealed class WFBodyToolSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobStateComponent, ToolUseAttemptEvent>(OnToolUseAttempt);
    }

    private void OnToolUseAttempt(Entity<MobStateComponent> ent, ref ToolUseAttemptEvent args)
    {
        // Also raised on the target of a tool, which a mob may be.
        if (args.Tool == ent.Owner && args.User != ent.Owner)
            args.Cancel();
    }
}
