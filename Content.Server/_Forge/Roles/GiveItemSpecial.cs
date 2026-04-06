using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Set;
using System.Linq;
using Content.Shared.Inventory;

/// <summary>
/// Ideally, replace it with something more interesting. For example: a company loadout.
/// </summary>
namespace Content.Server.Jobs
{
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class GiveItemSpecial : JobSpecial
    {
        [DataField("prototype", customTypeSerializer: typeof(PrototypeIdHashSetSerializer<EntityPrototype>))]
        public HashSet<String> Prototype { get; private set; } = new();

        public override void AfterEquip(EntityUid mob)
        {
            if (Prototype.Count == 0)
                return;

            var sysMan = IoCManager.Resolve<IEntitySystemManager>();

            sysMan.GetEntitySystem<InventorySystem>().SpawnItemsOnEntity(mob, Prototype.ToList());
        }
    }
}
