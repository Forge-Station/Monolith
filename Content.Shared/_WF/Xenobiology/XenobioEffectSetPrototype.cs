using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenobiology;

/// <summary>
/// A named list of effects that extract reactions share, such as "spend and delete the extract".
/// </summary>
[Prototype]
public sealed partial class XenobioEffectSetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(serverOnly: true)]
    public List<EntityEffect> Effects = new();
}
