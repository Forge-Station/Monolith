using Content.Client.Interactable.Components;
using Content.Client.StatusIcon;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client.Stealth;

public sealed partial class StealthSystem : SharedStealthSystem
{
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SpriteSystem _sprite = default!; // WOLFGATE: post-shader flags are set through SpriteSystem since engine 291

    private ShaderInstance _shader = default!;

    public override void Initialize()
    {
        base.Initialize();

        _shader = _protoMan.Index<ShaderPrototype>("Stealth").InstanceUnique();

        SubscribeLocalEvent<StealthComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<StealthComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<StealthComponent, BeforePostShaderRenderEvent>(OnShaderRender);
    }

    public override void SetEnabled(EntityUid uid, bool value, StealthComponent? component = null)
    {
        if (!Resolve(uid, ref component) || component.Enabled == value)
            return;

        base.SetEnabled(uid, value, component);
        SetShader(uid, value, component);
    }

    private void SetShader(EntityUid uid, bool enabled, StealthComponent? component = null, SpriteComponent? sprite = null)
    {
        if (!Resolve(uid, ref component, ref sprite, false))
            return;

        sprite.Color = Color.White;
        // WOLFGATE START: engine 291 ignores the sprite-wide GetScreenTexture/RaiseShaderEvent; they are per post-shader now
        // upstream original: sprite.PostShader = enabled ? _shader : null; sprite.GetScreenTexture = enabled; sprite.RaiseShaderEvent = enabled;
        sprite.PostShader = null;
        if (enabled)
            _sprite.SetPostShader(sprite, new SpriteComponent.PostShaderArgs("Stealth", _shader) { GetScreenTexture = true, RaiseShaderEvent = true });
        // WOLFGATE END

        if (!enabled)
        {
            if (component.HadOutline && !TerminatingOrDeleted(uid))
                EnsureComp<InteractionOutlineComponent>(uid);
            return;
        }

        if (TryComp(uid, out InteractionOutlineComponent? outline))
        {
            RemCompDeferred(uid, outline);
            component.HadOutline = true;
        }
    }

    private void OnStartup(EntityUid uid, StealthComponent component, ComponentStartup args)
    {
        SetShader(uid, component.Enabled, component);
    }

    private void OnShutdown(EntityUid uid, StealthComponent component, ComponentShutdown args)
    {
        if (!Terminating(uid))
            SetShader(uid, false, component);
    }

    private void OnShaderRender(EntityUid uid, StealthComponent component, BeforePostShaderRenderEvent args)
    {
        // Distortion effect uses screen coordinates. If a player moves, the entities appear to move on screen. this
        // makes the distortion very noticeable.

        // So we need to use relative screen coordinates. The reference frame we use is the parent's position on screen.
        // this ensures that if the Stealth is not moving relative to the parent, its relative screen position remains
        // unchanged.
        var parent = Transform(uid).ParentUid;
        if (!parent.IsValid())
            return; // should never happen, but lets not kill the client.
        var parentXform = Transform(parent);
        var reference = args.Viewport.WorldToLocal(_transformSystem.GetWorldPosition(parentXform));
        reference.X = -reference.X;
        var visibility = GetVisibility(uid, component);

        // Goobstation - Proper invisibility: changes -1 to -1.5
        // actual visual visibility effect is limited to -1.5 to 1.
        visibility = Math.Clamp(visibility, -1.5f, 1f);

        _shader.SetParameter("reference", reference);
        _shader.SetParameter("visibility", visibility);
        // WOLFGATE(Stealth) START: a glowing visor keeps its own colour; the blue tint would blacken it.
        var wfGlow = TryComp<Content.Shared._WF.Stealth.WFStealthGlowComponent>(uid, out var glow) ? glow.Strength : 0f;
        _shader.SetParameter("glow", wfGlow);
        if (wfGlow > 0f)
        {
            args.Sprite.Color = Color.White;
            return;
        }
        // WOLFGATE END

        visibility = MathF.Max(0, visibility);
        args.Sprite.Color = new Color(visibility, visibility, 1, 1);
    }
}
