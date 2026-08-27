using Content.Server.Administration;
using Content.Shared._Forge.LivingNpc.Components;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Player;

namespace Content.Server._Forge.LivingNpc;

[AdminCommand(AdminFlags.Debug)]
public sealed partial class LivingNpcCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;

    public string Command => "livingnpc";
    public string Description => "Inspect a Forge living NPC.";
    public string Help => "livingnpc [info] [netEntity]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            DumpNearby(shell);
            return;
        }

        if (args[0] is not ("info" or "dump") || args.Length < 2)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!NetEntity.TryParse(args[1], out var net) || !_entities.TryGetEntity(net, out var uid) || uid == null)
        {
            shell.WriteError("Unknown entity.");
            return;
        }

        if (!_entities.TryGetComponent(uid.Value, out LivingNpcComponent? npc))
        {
            shell.WriteError("That entity is not a living NPC.");
            return;
        }

        shell.WriteLine(Describe(uid.Value, npc));
    }

    private void DumpNearby(IConsoleShell shell)
    {
        if (shell.Player is not { AttachedEntity: { Valid: true } player })
        {
            shell.WriteError("Run this from a player session, or pass a net entity.");
            return;
        }

        var count = 0;
        var query = _entities.EntityQueryEnumerator<LivingNpcComponent, TransformComponent>();
        var origin = _entities.System<SharedTransformSystem>().GetWorldPosition(player);
        while (query.MoveNext(out var uid, out var npc, out var xform))
        {
            var dist = (_entities.System<SharedTransformSystem>().GetWorldPosition(xform) - origin).Length();
            if (dist > 24f)
                continue;

            shell.WriteLine($"{uid} ({dist:F1}m) {Describe(uid, npc)}");
            count++;
        }

        if (count == 0)
            shell.WriteLine("No living NPCs nearby.");
    }

    private string Describe(EntityUid uid, LivingNpcComponent npc)
    {
        var name = _entities.GetComponent<MetaDataComponent>(uid).EntityName;
        var mood = npc.Mood;
        return
            $"{name} intent={npc.CurrentIntent} asleep={npc.Asleep} " +
            $"mood(h={mood.Happiness:F2} f={mood.Fear:F2} a={mood.Anger:F2} e={mood.Energy:F2} s={mood.SocialNeed:F2}) " +
            $"speech={npc.QueuedSpeech ?? "-"}";
    }
}
