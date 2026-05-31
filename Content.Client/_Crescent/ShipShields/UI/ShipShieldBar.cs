using System.Numerics;
using Content.Shared._Crescent.ShipShields;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._Crescent.ShipShields.UI;

/// <summary>
/// Horizontal HP bar for the ship shield emitter. Shows the shield charge percentage
/// while online, and a real-time countdown to recharge while offline.
/// </summary>
/// <remarks>
/// Forge-Change: reads <see cref="ShipShieldGridStateComponent"/> on the configured grid so the HUD
/// works even when the emitter or bubble are outside the client's PVS (e.g. another room on the same ship).
/// </remarks>
public sealed class ShipShieldBar : Control
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Font _font;

    private EntityUid? _grid;

    public ShipShieldBar()
    {
        IoCManager.InjectDependencies(this);
        _font = new VectorFont(IoCManager.Resolve<IResourceCache>().GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 10);
        MinSize = new Vector2(100, 18);
    }

    /// <summary>
    /// Sets the grid whose shield state should be displayed.
    /// </summary>
    public void SetGrid(EntityUid? grid)
    {
        _grid = grid;
    }

    /// <summary>
    /// True if the configured grid currently has a shield emitter to display.
    /// </summary>
    public bool HasShield => TryResolveGridState(out _);

    private bool TryResolveGridState(out ShipShieldGridStateComponent state)
    {
        state = default!;
        if (_grid is not { } grid || !_entManager.EntityExists(grid))
            return false;

        if (_entManager.TryGetComponent(grid, out ShipShieldGridStateComponent? comp) && comp is { HasEmitter: true })
        {
            state = comp;
            return true;
        }

        // Forge-Change: fallback when grid snapshot is not replicated yet but the emitter is in PVS.
        return TryResolveEmitterFallback(grid, out state);
    }

    private bool TryResolveEmitterFallback(EntityUid grid, out ShipShieldGridStateComponent state)
    {
        state = default!;

        ShipShieldEmitterComponent? emitter = null;
        if (_entManager.TryGetComponent(grid, out ShipShieldedComponent? shielded)
            && shielded.Source is { } src
            && _entManager.TryGetComponent(src, out ShipShieldEmitterComponent? canonical))
        {
            emitter = canonical;
        }
        else
        {
            var query = _entManager.EntityQueryEnumerator<ShipShieldEmitterComponent, TransformComponent>();
            while (query.MoveNext(out _, out var ec, out var xform))
            {
                if (xform.GridUid != grid || !xform.Anchored)
                    continue;
                emitter = ec;
                break;
            }
        }

        if (emitter == null)
            return false;

        state = new ShipShieldGridStateComponent
        {
            HasEmitter = true,
            Damage = emitter.Damage,
            DamageLimit = emitter.DamageLimit,
            Recharging = emitter.Recharging,
            Online = emitter.Online,
            RechargeEndTime = emitter.RechargeEndTime,
        };
        return true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (!TryResolveGridState(out var state))
            return;

        var background = Color.FromHex("#1a1a1a");
        var border = Color.FromHex("#444444");

        handle.DrawRect(new UIBox2(0, 0, PixelWidth, PixelHeight), background);

        var limit = state.DamageLimit > 0f ? state.DamageLimit : 1f;
        var percent = MathHelper.Clamp(1f - state.Damage / limit, 0f, 1f);

        Color barColor;
        string label;
        if (state.Online)
        {
            barColor = LerpHpColor(percent);
            label = $"{(int) MathF.Round(percent * 100f)}%";
        }
        else
        {
            barColor = Color.FromHex("#a02020");
            var remaining = state.RechargeEndTime.HasValue
                ? (state.RechargeEndTime.Value - _timing.CurTime).TotalSeconds
                : 0;
            if (remaining < 0)
                remaining = 0;
            label = FormatCountdown(remaining);
        }

        var fillWidth = PixelWidth * percent;
        if (fillWidth > 1)
            handle.DrawRect(new UIBox2(0, 0, fillWidth, PixelHeight), barColor);

        handle.DrawRect(new UIBox2(0, 0, PixelWidth, PixelHeight), border, filled: false);

        var dimensions = handle.GetDimensions(_font, label, UIScale);
        var textPos = new Vector2((PixelWidth - dimensions.X) * 0.5f, (PixelHeight - dimensions.Y) * 0.5f);
        handle.DrawString(_font, textPos, label, UIScale, Color.White);
    }

    private static Color LerpHpColor(float percent)
    {
        if (percent >= 0.5f)
        {
            var t = (percent - 0.5f) * 2f;
            return Color.InterpolateBetween(Color.FromHex("#d8a000"), Color.FromHex("#1ea35a"), t);
        }
        else
        {
            var t = percent * 2f;
            return Color.InterpolateBetween(Color.FromHex("#a02020"), Color.FromHex("#d8a000"), t);
        }
    }

    private static string FormatCountdown(double seconds)
    {
        if (seconds <= 0)
            return "0.0s";
        if (seconds < 60)
            return $"{seconds:0.0}s";
        var mins = (int) (seconds / 60);
        var secs = seconds - mins * 60;
        return $"{mins}m {secs:00}s";
    }
}
