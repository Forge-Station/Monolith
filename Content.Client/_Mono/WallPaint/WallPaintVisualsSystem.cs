using Content.Shared._Mono.WallPaint;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Client._Mono.WallPaint;

public sealed partial class WallPaintVisualsSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;

    private const string ShaderPrototypeId = "WallPaintDarken";
    private const float OpaqueAlphaThreshold = 0.95f;

    private readonly Dictionary<EntityUid, ShaderInstance> _shaderInstances = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<WallPaintComponent, ComponentStartup>(OnPaintStartup);
        SubscribeLocalEvent<WallPaintComponent, AfterAutoHandleStateEvent>(OnPaintHandleState);
        SubscribeLocalEvent<WallPaintComponent, ComponentShutdown>(OnPaintShutdown);
        SubscribeLocalEvent<PaintableWallComponent, ComponentStartup>(OnPaintableStartup);
    }

    private void OnPaintStartup(EntityUid uid, WallPaintComponent component, ComponentStartup args)
    {
        UpdateShader(uid, component);
    }

    private void OnPaintHandleState(EntityUid uid, WallPaintComponent component, ref AfterAutoHandleStateEvent args)
    {
        UpdateShader(uid, component);
    }

    private void OnPaintShutdown(EntityUid uid, WallPaintComponent component, ComponentShutdown args)
    {
        ClearShader(uid);
    }

    private void OnPaintableStartup(EntityUid uid, PaintableWallComponent component, ComponentStartup args)
    {
        if (TryComp(uid, out WallPaintComponent? paint))
            UpdateShader(uid, paint);
    }

    private void UpdateShader(EntityUid uid, WallPaintComponent paint, SpriteComponent? sprite = null)
    {
        if (!Resolve(uid, ref sprite, false))
            return;

        if (!_shaderInstances.TryGetValue(uid, out var shader))
        {
            shader = _prototype.Index<ShaderPrototype>(ShaderPrototypeId).InstanceUnique();
            _shaderInstances[uid] = shader;
        }

        shader.SetParameter("paintColor", paint.Color);
        shader.SetParameter("protectTransparency", TryComp(uid, out PaintableWallComponent? paintable) && paintable.ProtectTransparent);
        shader.SetParameter("opaqueAlphaThreshold", OpaqueAlphaThreshold);

        sprite.PostShader = shader;
        sprite.GetScreenTexture = false;
        sprite.RaiseShaderEvent = false;
    }

    private void ClearShader(EntityUid uid)
    {
        if (!_shaderInstances.Remove(uid, out var shader))
            return;

        if (!TerminatingOrDeleted(uid) &&
            TryComp(uid, out SpriteComponent? sprite) &&
            sprite.PostShader == shader)
        {
            sprite.PostShader = null;
            sprite.GetScreenTexture = false;
            sprite.RaiseShaderEvent = false;
        }

        shader.Dispose();
    }
}
