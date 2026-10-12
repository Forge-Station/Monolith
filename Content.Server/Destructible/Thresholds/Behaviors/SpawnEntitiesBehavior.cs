using System.Numerics;
using Content.Server._Forge.OrePipe;
using Content.Server.Forensics;
using Content.Server.Stack;
using Content.Shared._Forge.OrePipe;
using Content.Shared.Destructible.Thresholds;
using Content.Shared.Prototypes;
using Content.Shared.Stacks;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Destructible.Thresholds.Behaviors
{
    [Serializable]
    [DataDefinition]
    public sealed partial class SpawnEntitiesBehavior : IThresholdBehavior
    {
        /// <summary>
        ///     Entities spawned on reaching this threshold, from a min to a max.
        /// </summary>
        [DataField]
        public Dictionary<EntProtoId, MinMax> Spawn = new();

        [DataField("offset")]
        public float Offset { get; set; } = 0.5f;

        [DataField("transferForensics")]
        public bool DoTransferForensics;

        [DataField]
        public bool SpawnInContainer;

        public void Execute(EntityUid owner, DestructibleSystem system, EntityUid? cause = null)
        {
            // Forge-Change: ore crabs/golems killed by ship drills → abstract buffer, no Dynamic piles.
            if (TryDepositOreToDrillBuffer(owner, system, cause))
                return;

            var tSys = system.EntityManager.System<TransformSystem>();
            var position = tSys.GetMapCoordinates(owner);

            var getRandomVector = () => new Vector2(system.Random.NextFloat(-Offset, Offset), system.Random.NextFloat(-Offset, Offset));

            var executions = 1;
            if (system.EntityManager.TryGetComponent<StackComponent>(owner, out var stack))
            {
                executions = stack.Count;
            }

            foreach (var (entityId, minMax) in Spawn)
            {
                for (var execution = 0; execution < executions; execution++)
                {
                    var count = minMax.Min >= minMax.Max
                        ? minMax.Min
                        : system.Random.Next(minMax.Min, minMax.Max + 1);

                    if (count == 0)
                        continue;

                    if (EntityPrototypeHelpers.HasComponent<StackComponent>(entityId, system.PrototypeManager, system.EntityManager.ComponentFactory))
                    {
                        var spawned = SpawnInContainer
                            ? system.EntityManager.SpawnNextToOrDrop(entityId, owner)
                            : system.EntityManager.SpawnEntity(entityId, position.Offset(getRandomVector()));
                        system.StackSystem.SetCount(spawned, count);

                        TransferForensics(spawned, system, owner);
                    }
                    else
                    {
                        for (var i = 0; i < count; i++)
                        {
                            var spawned = SpawnInContainer
                                ? system.EntityManager.SpawnNextToOrDrop(entityId, owner)
                                : system.EntityManager.SpawnEntity(entityId, position.Offset(getRandomVector()));

                            TransferForensics(spawned, system, owner);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Forge-Change: when an ore crab/golem is finished by a ship drill, deposit spawn table into the drill buffer.
        /// </summary>
        private bool TryDepositOreToDrillBuffer(EntityUid owner, DestructibleSystem system, EntityUid? cause)
        {
            var entMan = system.EntityManager;
            if (!entMan.TryGetComponent(owner, out OreDrillHarvestTargetComponent? harvest))
                return false;

            EntityUid? drill = null;
            if (harvest.LastDrill is { } last && entMan.EntityExists(last) && entMan.HasComponent<OrePipeBufferComponent>(last))
                drill = last;
            else if (cause is { } c && entMan.HasComponent<OrePipeBufferComponent>(c))
                drill = c;

            if (drill == null)
                return false;

            var orePipe = entMan.System<OrePipeSystem>();

            var executions = 1;
            if (entMan.TryGetComponent<StackComponent>(owner, out var ownerStack))
                executions = ownerStack.Count;

            foreach (var (entityId, minMax) in Spawn)
            {
                for (var execution = 0; execution < executions; execution++)
                {
                    var count = minMax.Min >= minMax.Max
                        ? minMax.Min
                        : system.Random.Next(minMax.Min, minMax.Max + 1);

                    if (count <= 0)
                        continue;

                    orePipe.TryDepositOre(drill.Value, entityId, count);
                }
            }

            // Always suppress world spawn when a drill is responsible — full buffer still must not drop Dynamic ore.
            return true;
        }

        public void TransferForensics(EntityUid spawned, DestructibleSystem system, EntityUid owner)
        {
            if (!DoTransferForensics ||
                !system.EntityManager.TryGetComponent<ForensicsComponent>(owner, out var forensicsComponent))
                return;

            var comp = system.EntityManager.EnsureComponent<ForensicsComponent>(spawned);
            comp.DNAs = forensicsComponent.DNAs;

            if (!system.Random.Prob(0.4f))
                return;
            comp.Fingerprints = forensicsComponent.Fingerprints;
            comp.Fibers = forensicsComponent.Fibers;
        }
    }
}
