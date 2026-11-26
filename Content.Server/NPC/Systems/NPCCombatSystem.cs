using Content.Server.Interaction;
using Content.Server.NPC.Components;
using Content.Server.Storage.Components;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Server.Containers;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.NPC.Systems;

/// <summary>
/// Handles combat for NPCs.
/// </summary>
public sealed partial class NPCCombatSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private GunSystem _gun = default!;
    [Dependency] private InteractionSystem _interaction = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ContainerSystem _container = default!;

    /// <summary>
    /// If disabled we'll move into range but not attack.
    /// </summary>
    public bool Enabled = true;

    /// <summary>
    /// The crate is the target only when this NPC watched that living mob climb into it.
    /// </summary>
    public EntityUid GetAttackTarget(EntityUid npc, EntityUid target)
    {
        if (TryGetHidingStorage(target, out var storage) && SawEnterStorage(npc, storage))
            return storage;

        return target;
    }

    /// <summary>
    /// A living mob shut in storage this NPC did not see them enter. They stay hidden until they come out.
    /// </summary>
    public bool IsHiddenFrom(EntityUid npc, EntityUid target)
    {
        return TryGetHidingStorage(target, out var storage) && !SawEnterStorage(npc, storage);
    }

    public bool SawEnterStorage(EntityUid npc, EntityUid storage)
    {
        return TryComp<NpcHidingMemoryComponent>(npc, out var memory) && memory.WitnessedContainers.Contains(storage);
    }

    private bool TryGetHidingStorage(EntityUid target, out EntityUid storage)
    {
        storage = default;

        if (!TryComp<MobStateComponent>(target, out var mob) || mob.CurrentState != MobState.Alive)
            return false;

        if (!_container.TryGetContainingContainer(target, out var container) ||
            !HasComp<EntityStorageComponent>(container.Owner))
            return false;

        storage = container.Owner;
        return true;
    }

    public override void Initialize()
    {
        base.Initialize();
        InitializeMelee();
        InitializeRanged();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateMelee(frameTime);
        UpdateRanged(frameTime);
    }
}
