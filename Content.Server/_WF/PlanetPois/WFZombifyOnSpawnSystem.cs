using System.Linq;
using Content.Server.Zombies;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.PlanetPois;

/// <summary>
/// A body that rises as an inert zombie as soon as it exists, such as a wrecked ship's crew. It takes its wreck's side
/// and counts zombies of any other source as friends, so it never goes for one (<c>addFriendlyFactions</c> in its prototype).
/// </summary>
[RegisterComponent]
public sealed partial class WFZombifyOnSpawnComponent : Component
{
    /// <summary>
    /// The sides it takes once risen, in place of the zombies': the wildlife it shares its wreck or tunnel with, so the
    /// two hunt the living rather than each other.
    /// </summary>
    [DataField]
    public List<ProtoId<NpcFactionPrototype>> Factions = new() { "SimpleHostile" };
}

/// <summary>A zombie that only bites: it infects nobody and a body it leaves incapacitated does not rise.</summary>
[RegisterComponent]
public sealed partial class WFInertZombieComponent : Component;

/// <summary>Turns <see cref="WFZombifyOnSpawnComponent"/> bodies a tick after map init, once their gear is on.</summary>
public sealed class WFZombifyOnSpawnSystem : EntitySystem
{
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private ZombieSystem _zombies = default!;

    private readonly List<EntityUid> _pending = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFZombifyOnSpawnComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<WFZombifyOnSpawnComponent> ent, ref MapInitEvent args)
    {
        _pending.Add(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_pending.Count == 0)
            return;

        foreach (var uid in _pending.ToArray())
        {
            if (TerminatingOrDeleted(uid) || !TryComp(uid, out WFZombifyOnSpawnComponent? comp))
                continue;
            var factions = comp.Factions;
            RemComp(uid, comp);
            _zombies.ZombifyEntity(uid);
            EnsureComp<NonSpreaderZombieComponent>(uid);
            EnsureComp<WFInertZombieComponent>(uid);
            _factions.ClearFactions(uid, dirty: false);
            _factions.AddFactions(uid, factions.ToHashSet());
        }
        _pending.Clear();
    }
}
