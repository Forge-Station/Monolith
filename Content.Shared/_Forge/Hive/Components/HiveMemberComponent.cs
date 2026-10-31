using Content.Shared._Starlight.CollectiveMind;
using Content.Shared.Damage;
using Content.Shared.Store;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Hive.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HiveMemberComponent : Component
{
    #region Магазин

    [DataField]
    public ProtoId<CurrencyPrototype> Currency = "HiveDNA";

    [DataField]
    public int StartingDna = 20;

    [DataField]
    public List<ProtoId<StoreCategoryPrototype>> StoreCategories = new() { "HiveAbilities" };

    [DataField]
    public LocId StoreName = "hive-store-name";

    [DataField]
    public bool RefundAllowed;

    #endregion

    #region Разум Улья

    [DataField]
    public ProtoId<CollectiveMindPrototype> HivemindChannel = "Hive";

    #endregion

    #region Действия

    [DataField]
    public EntProtoId ShopAction = "ActionHiveShop";

    [DataField, AutoNetworkedField]
    public EntityUid? ShopActionEntity;

    [DataField]
    public EntProtoId DevourAction = "ActionHiveDevour";

    [DataField, AutoNetworkedField]
    public EntityUid? DevourActionEntity;

    #endregion

    #region Пожирание

    [DataField]
    public TimeSpan DevourTime = TimeSpan.FromSeconds(10);

    [DataField]
    public int DnaReward = 10;

    [DataField]
    public int MindlessDnaReward = 2;

    [DataField]
    public bool AllowCritical = true;

    [DataField]
    public bool CanDevourHiveMembers;

    [DataField]
    public DamageSpecifier DevourDamage = new()
    {
        DamageDict = new()
        {
            { "Slash", 40 },
            { "Piercing", 20 },
        },
    };

    [DataField]
    public SoundSpecifier? DevourStartSound = new SoundCollectionSpecifier("gib", AudioParams.Default.WithVolume(-3f));

    [DataField]
    public SoundSpecifier? DevourEndSound = new SoundPathSpecifier("/Audio/Effects/demon_consume.ogg", AudioParams.Default.WithVolume(-3f));

    [DataField]
    public LocId UnrevivableReason = "hive-devoured-unrevivable";

    #endregion

    [DataField, AutoNetworkedField]
    public int DevouredCount;
}
