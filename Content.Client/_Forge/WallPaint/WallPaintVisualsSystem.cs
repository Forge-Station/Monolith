using System.Linq;
using Content.Shared._Forge.WallPaint;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Client._Forge.WallPaint;

public sealed partial class WallPaintVisualsSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;

    private const string ShaderPrototypeId = "WallPaintDarken";
    private const float OpaqueAlphaThreshold = 0.95f;

    private readonly Dictionary<EntityUid, PaintShaderState> _shaderStates = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<WallPaintComponent, ComponentStartup>(OnPaintStartup);
        SubscribeLocalEvent<WallPaintComponent, AfterAutoHandleStateEvent>(OnPaintHandleState);
        SubscribeLocalEvent<WallPaintComponent, ComponentShutdown>(OnPaintShutdown);
        SubscribeLocalEvent<PaintableWallComponent, ComponentStartup>(OnPaintableStartup);
    }

    public override void Shutdown()
    {
        foreach (var uid in _shaderStates.Keys.ToArray())
        {
            ClearShader(uid);
        }

        base.Shutdown();
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

        if (!_shaderStates.TryGetValue(uid, out var state))
        {
            state = new PaintShaderState(
                _prototype.Index<ShaderPrototype>(ShaderPrototypeId).InstanceUnique(),
                sprite.PostShader,
                sprite.GetScreenTexture,
                sprite.RaiseShaderEvent);
            _shaderStates[uid] = state;
        }

        state.Shader.SetParameter("paintColor", paint.Color);
        state.Shader.SetParameter("protectTransparency", paint.ProtectTransparent);
        state.Shader.SetParameter("opaqueAlphaThreshold", OpaqueAlphaThreshold);

        sprite.PostShader = state.Shader;
        sprite.GetScreenTexture = false;
        sprite.RaiseShaderEvent = false;
    }

    private void ClearShader(EntityUid uid)
    {
        if (!_shaderStates.Remove(uid, out var state))
            return;

        if (!TerminatingOrDeleted(uid) &&
            TryComp(uid, out SpriteComponent? sprite) &&
            sprite.PostShader == state.Shader)
        {
            sprite.PostShader = state.PreviousShader;
            sprite.GetScreenTexture = state.PreviousGetScreenTexture;
            sprite.RaiseShaderEvent = state.PreviousRaiseShaderEvent;
        }

        state.Shader.Dispose();
    }

    private sealed class PaintShaderState
    {
        public readonly ShaderInstance Shader;
        public readonly ShaderInstance? PreviousShader;
        public readonly bool PreviousGetScreenTexture;
        public readonly bool PreviousRaiseShaderEvent;

        public PaintShaderState(
            ShaderInstance shader,
            ShaderInstance? previousShader,
            bool previousGetScreenTexture,
            bool previousRaiseShaderEvent)
        {
            Shader = shader;
            PreviousShader = previousShader;
            PreviousGetScreenTexture = previousGetScreenTexture;
            PreviousRaiseShaderEvent = previousRaiseShaderEvent;
        }
    }
}
