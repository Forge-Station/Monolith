using Robust.Shared.GameStates;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;

namespace Content.Shared._Mono.WallPaint;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WallPaintComponent : Component
{
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public Color Color = Color.White;
}
