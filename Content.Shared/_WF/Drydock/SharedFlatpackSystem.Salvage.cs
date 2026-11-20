using Content.Shared.Construction.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared.Construction;

public abstract partial class SharedFlatpackSystem
{
    /// <summary>Points a flatpack at the entity it unpacks into, with no board, and gives it a name and description.</summary>
    public void SetupSalvagedFlatpack(Entity<FlatpackComponent?> ent, EntProtoId proto, string name, string description)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        ent.Comp.Entity = proto;
        Dirty(ent.Owner, ent.Comp);

        var meta = MetaData(ent);
        _metaData.SetEntityName(ent, name, meta);
        _metaData.SetEntityDescription(ent, description, meta);
    }
}
