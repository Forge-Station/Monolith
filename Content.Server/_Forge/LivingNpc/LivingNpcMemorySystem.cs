using Content.Shared._Forge.LivingNpc;
using Content.Shared._Forge.LivingNpc.Components;
using Content.Shared.IdentityManagement;
using Robust.Shared.Timing;

namespace Content.Server._Forge.LivingNpc;

public sealed partial class LivingNpcMemorySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public LivingNpcMemoryEntry GetOrCreate(LivingNpcMemoryComponent memory, EntityUid other)
    {
        if (memory.Entries.TryGetValue(other, out var entry))
            return entry;

        while (memory.Entries.Count >= LivingNpcMemoryComponent.MaxEntries)
            EvictOldest(memory);

        entry = new LivingNpcMemoryEntry
        {
            Entity = other,
            LastSeen = _timing.CurTime,
        };
        memory.Entries[other] = entry;
        return entry;
    }

    public void NoteSeen(EntityUid npc, LivingNpcMemoryComponent memory, EntityUid other)
    {
        if (npc == other || TerminatingOrDeleted(other))
            return;

        var entry = GetOrCreate(memory, other);
        entry.LastSeen = _timing.CurTime;
        entry.RememberedName ??= Identity.Name(other, EntityManager, npc);
    }

    public void NoteTalk(EntityUid npc, LivingNpcMemoryComponent memory, EntityUid other)
    {
        var entry = GetOrCreate(memory, other);
        entry.LastTalked = _timing.CurTime;
        entry.TimesTalked++;
        entry.RememberedName ??= Identity.Name(other, EntityManager, npc);
        // Talking slightly warms the relationship unless they already hurt us a lot.
        if (entry.Reputation > -0.6f)
            entry.Reputation = Math.Clamp(entry.Reputation + 0.04f, -1f, 1f);
    }

    public void NoteHurt(EntityUid npc, LivingNpcMemoryComponent memory, EntityUid other)
    {
        var entry = GetOrCreate(memory, other);
        entry.HurtMe = true;
        entry.LastSeen = _timing.CurTime;
        entry.Reputation = Math.Clamp(entry.Reputation - 0.45f, -1f, 1f);
    }

    public void NoteHelp(EntityUid npc, LivingNpcMemoryComponent memory, EntityUid other)
    {
        var entry = GetOrCreate(memory, other);
        entry.HelpedMe = true;
        entry.Reputation = Math.Clamp(entry.Reputation + 0.35f, -1f, 1f);
    }

    public float GetReputation(LivingNpcMemoryComponent memory, EntityUid other)
    {
        return memory.Entries.TryGetValue(other, out var entry) ? entry.Reputation : 0f;
    }

    public bool IsKnown(LivingNpcMemoryComponent memory, EntityUid other)
    {
        return memory.Entries.ContainsKey(other);
    }

    private static void EvictOldest(LivingNpcMemoryComponent memory)
    {
        EntityUid? oldest = null;
        var oldestTime = TimeSpan.MaxValue;
        foreach (var (uid, entry) in memory.Entries)
        {
            if (entry.LastSeen < oldestTime)
            {
                oldestTime = entry.LastSeen;
                oldest = uid;
            }
        }

        if (oldest != null)
            memory.Entries.Remove(oldest.Value);
    }
}
