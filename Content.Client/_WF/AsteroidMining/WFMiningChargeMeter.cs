using System.Numerics;
using Content.Shared._WF.AsteroidMining;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._WF.AsteroidMining;

/// <summary>
/// A rock's heat on a bar: cold, the green cracking window, the yellow band that cracks faster but loses ore, then the
/// red overcharge zone. A white needle shows the heat now; where a power setting will settle it is left to the gunner.
/// </summary>
public sealed class WFMiningChargeMeter : Control
{
    public float Charge;
    public float WindowLow;
    public float WindowHigh;
    public bool Overcharging;

    private static readonly Color Cold = new(0.16f, 0.2f, 0.24f);
    private static readonly Color WindowColor = new(0.15f, 0.75f, 0.3f);
    private static readonly Color Hot = new(0.9f, 0.72f, 0.12f);
    private static readonly Color Danger = new(0.85f, 0.1f, 0.08f);

    public WFMiningChargeMeter()
    {
        MinSize = new Vector2(120, 24);
        HorizontalExpand = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var size = (Vector2) PixelSize;
        var top = size.Y * 0.2f;
        var bottom = size.Y * 0.8f;

        handle.DrawRect(new UIBox2(X(0f), top, X(WindowLow), bottom), Cold);
        handle.DrawRect(new UIBox2(X(WindowLow), top, X(WindowHigh), bottom), WindowColor);
        handle.DrawRect(new UIBox2(X(WindowHigh), top, X(WFAsteroidHeat.OverchargeLine), bottom), Hot.WithAlpha(0.8f));
        handle.DrawRect(new UIBox2(X(WFAsteroidHeat.OverchargeLine), top, X(WFAsteroidHeat.MeterScale), bottom), Danger.WithAlpha(Overcharging ? 1f : 0.7f));
        handle.DrawRect(new UIBox2(0f, top, size.X, bottom), Color.White.WithAlpha(0.35f), false);

        var needle = X(Charge);
        handle.DrawRect(new UIBox2(needle - 1.5f * UIScale, 0f, needle + 1.5f * UIScale, size.Y), Color.White);

        float X(float value)
        {
            return size.X * Math.Clamp(value / WFAsteroidHeat.MeterScale, 0f, 1f);
        }
    }
}
