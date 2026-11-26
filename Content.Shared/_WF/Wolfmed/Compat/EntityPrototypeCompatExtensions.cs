using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Compat;

/// <summary>Onyx's RT names this TryComp; RT 277 still calls it TryGetComponent.</summary>
public static class EntityPrototypeCompatExtensions
{
    /// <summary>Gets a component from an entity prototype's component registry.</summary>
    public static bool TryComp<T>(this EntityPrototype proto, [NotNullWhen(true)] out T? component, IComponentFactory factory)
        where T : IComponent, new()
        => proto.TryGetComponent(out component, factory);
}
