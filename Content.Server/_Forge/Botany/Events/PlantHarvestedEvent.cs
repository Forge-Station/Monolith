using Content.Server.Botany;

namespace Content.Server._Forge.Botany.Events;

public sealed class PlantHarvestedEvent(EntityUid uid, SeedData seedData, EntityUid? plantholder, IEnumerable<EntityUid> products)
{
    public EntityUid Uid = uid;
    public SeedData SeedData = seedData;
    public EntityUid? Plantholder = plantholder;
    public IEnumerable<EntityUid> Products = products;
}
