using System.Linq;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.Prototypes;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server.Jobs
{
    /// <summary>
    /// Grants additional access tags/groups to the player's ID card after spawn.
    /// Intended for company-based access without requiring faction jobs.
    /// </summary>
    [DataDefinition]
    public sealed partial class AddAccessSpecial : JobSpecial
    {
        [DataField("tags")]
        public HashSet<ProtoId<AccessLevelPrototype>> Tags { get; private set; } = new();

        [DataField("groups")]
        public HashSet<ProtoId<AccessGroupPrototype>> Groups { get; private set; } = new();

        public override void AfterEquip(EntityUid mob)
        {
            if (Tags.Count == 0 && Groups.Count == 0)
                return;

            var entMan = IoCManager.Resolve<IEntityManager>();
            var sysMan = IoCManager.Resolve<IEntitySystemManager>();
            var inventory = sysMan.GetEntitySystem<InventorySystem>();
            var access = sysMan.GetEntitySystem<SharedAccessSystem>();

            if (!inventory.TryGetSlotEntity(mob, "id", out var idUid))
                return;

            var target = idUid.Value;
            if (entMan.TryGetComponent(target, out PdaComponent? pda) && pda.ContainedId is { } containedId)
                target = containedId;

            if (!entMan.HasComponent<AccessComponent>(target))
                return;

            if (Groups.Count > 0)
                access.TryAddGroups(target, Groups);

            if (Tags.Count > 0)
            {
                var current = access.TryGetTags(target)?.ToHashSet() ?? new HashSet<ProtoId<AccessLevelPrototype>>();
                current.UnionWith(Tags);
                access.TrySetTags(target, current);
            }
        }
    }
}
