using System.Numerics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    /// <summary>Measures equally sized scenario cards.</summary>
    private sealed class ScenarioColumns : Container
    {
        private const float Gap = 8;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            if (ChildCount == 0)
                return Vector2.Zero;
            var cardWidth = Math.Max(0, (availableSize.X - Gap * (ChildCount - 1)) / ChildCount);
            var height = 0f;
            for (var i = 0; i < ChildCount; i++)
            {
                Children[i].Measure(new Vector2(cardWidth, availableSize.Y));
                height = Math.Max(height, Children[i].DesiredSize.Y);
            }
            return new Vector2(availableSize.X, height);
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            if (ChildCount == 0)
                return finalSize;
            var cardWidth = Math.Max(0, (finalSize.X - Gap * (ChildCount - 1)) / ChildCount);
            var left = 0f;
            for (var i = 0; i < ChildCount; i++)
            {
                var width = cardWidth;
                Children[i].Arrange(UIBox2.FromDimensions(new Vector2(left, 0), new Vector2(width, finalSize.Y)));
                left += width + Gap;
            }
            return finalSize;
        }
    }
}
