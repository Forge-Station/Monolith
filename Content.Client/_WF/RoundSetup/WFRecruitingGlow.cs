using System.Linq;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._WF.RoundSetup;

/// <summary>A slow pulsing border drawn over a crew entry that is recruiting.</summary>
public sealed class WFRecruitingGlow : Control
{
    /// <summary>The recruiting accent, shared with the posting card and chat line.</summary>
    public static readonly Color Accent = Color.FromHex("#7FD3C4");

    private const float Period = 2f;

    [Dependency] private IGameTiming _timing = default!;

    public WFRecruitingGlow()
    {
        IoCManager.InjectDependencies(this);
        MouseFilter = MouseFilterMode.Ignore;
        HorizontalExpand = true;
        VerticalExpand = true;
    }

    /// <summary>Whether the latest crew snapshot lists this station's crew as recruiting.</summary>
    public static bool IsRecruitingStation(NetEntity station) =>
        IoCManager.Resolve<IEntityManager>().System<WFRoundSetupClientSystem>().State?.RecruitingStations.Contains(station) == true;

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var pulse = 0.5f + 0.5f * MathF.Sin((float) _timing.RealTime.TotalSeconds * MathF.Tau / Period);
        // A button lays its children out inside its padding, so cover the parent's whole box instead of this control's.
        var origin = Parent == null ? Vector2.Zero : -(Vector2) PixelPosition;
        var size = Parent?.PixelSize ?? PixelSize;
        var box = UIBox2.FromDimensions(origin, size);
        handle.DrawRect(box, Accent.WithAlpha(0.04f + 0.08f * pulse));
        for (var inset = 0; inset < 2; inset++)
        {
            handle.DrawRect(new UIBox2(box.Left + inset, box.Top + inset, box.Right - inset, box.Bottom - inset),
                Accent.WithAlpha(0.3f + 0.55f * pulse), false);
        }
    }
}
