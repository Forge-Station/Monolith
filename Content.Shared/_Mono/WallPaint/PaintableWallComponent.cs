using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Mono.WallPaint;

[RegisterComponent]
public sealed partial class PaintableWallComponent : Component
{
    [DataField]
    public bool ProtectTransparent;
}
