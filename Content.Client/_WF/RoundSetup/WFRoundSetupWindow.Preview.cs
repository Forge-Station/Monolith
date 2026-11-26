using System.Numerics;
using Content.Client._WF.ShipPreview;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    private RoundSetupShipPreview? _shipPreviewPanel;

    /// <summary>Reuses the Wolfgate previewer while the ship page is refreshed or resized.</summary>
    private RoundSetupShipPreview GetShipPreview()
    {
        if (_shipPreviewPanel == null)
        {
            _shipPreviewPanel = new RoundSetupShipPreview();
            OnClose += ReleaseShipPreview;
        }
        _shipPreviewPanel.Orphan();
        return _shipPreviewPanel;
    }

    private void AddShipPreviewLayout(BoxContainer details, RoundSetupShipPreview preview)
    {
        _shipPage.AddChild(new ShipPreviewColumns(Panel(details), preview) { VerticalExpand = true });
    }

    private void ReleaseShipPreview() => _shipPreviewPanel?.Release();

    protected override void FrameUpdate(FrameEventArgs args)
    {
        if (_layoutRefreshPending)
        {
            _layoutRefreshPending = false;
            Redraw();
        }
        if (_postingReadyAt is { } ready && _watchTiming.CurTime >= ready)
        {
            _postingReadyAt = null;
            _deferredRefresh = true;
        }
        if (_deferredRefresh && !IsEditingControls())
            Redraw();
        base.FrameUpdate(args);
        KeepWatching();
        // A same-page redraw reparents the preview synchronously; a page change leaves it detached.
        if (_shipPreviewPanel is { IsInsideTree: false } preview)
            preview.Release();
    }

    [Obsolete("Controls should only be removed from UI tree instead of being disposed")]
    protected override void Dispose(bool disposing)
    {
        if (disposing && _shipPreviewPanel != null)
        {
            _shipPreviewPanel.Release();
            _shipPreviewPanel.Orphan();
            _shipPreviewPanel = null;
        }
        base.Dispose(disposing);
    }

    /// <summary>Owns the existing preview control and releases its queued work and map together.</summary>
    private sealed class RoundSetupShipPreview : BoxContainer
    {
        private readonly ShipPreviewControl _preview;
        private readonly RichTextLabel _info;
        private VesselPrototype? _vessel;
        private bool _released = true;

        public RoundSetupShipPreview()
        {
            Orientation = LayoutOrientation.Vertical;
            SeparationOverride = 6;
            HorizontalExpand = true;
            AddChild(Heading("preview-title"));
            _preview = new ShipPreviewControl
            {
                Name = "RoundSetupShipPreview",
                HorizontalExpand = true,
                VerticalExpand = true,
                MinHeight = 120,
                ToolTip = Loc.GetString("wf-ship-preview-hint"),
            };
            var viewport = Panel(_preview);
            viewport.VerticalExpand = true;
            AddChild(viewport);
            _info = PlainDescription(string.Empty);
            _info.HorizontalExpand = true;
            var reset = new Button
            {
                Name = "RoundSetupPreviewReset",
                Text = Loc.GetString("wf-ship-preview-reset-view"),
                ToolTip = Loc.GetString("wf-ship-preview-hint"),
                MinHeight = 32,
            };
            reset.OnPressed += _ => _preview.FitToGrid();
            AddChild(Row(_info, reset));
            _preview.PreviewUpdated += UpdateInfo;
        }

        public void SetVessel(VesselPrototype? vessel)
        {
            var changed = _vessel != vessel || _released;
            _vessel = vessel;
            _released = false;
            _preview.SetVessel(vessel);
            if (changed)
                _info.SetMessage(string.Empty);
            else
                UpdateInfo();
        }

        public void Release()
        {
            if (_released)
                return;
            _released = true;
            _vessel = null;
            _preview.SetVessel(null);
            _preview.Release();
        }

        private void UpdateInfo()
        {
            _info.SetMessage(_preview.HasPreview
                ? Loc.GetString("wf-ship-preview-info", ("width", _preview.GridSize.X),
                    ("height", _preview.GridSize.Y), ("count", _preview.TileCount))
                : string.Empty);
        }

        [Obsolete("Controls should only be removed from UI tree instead of being disposed")]
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _preview.PreviewUpdated -= UpdateInfo;
                Release();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>Reserves readable ship information beside the embedded preview under constrained widths.</summary>
    private sealed class ShipPreviewColumns : Container
    {
        private const float Gap = 12;
        private readonly Control _details;
        private readonly Control _preview;

        public ShipPreviewColumns(Control details, Control preview)
        {
            HorizontalExpand = true;
            _details = details;
            _preview = preview;
            AddChild(details);
            AddChild(preview);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var width = Math.Max(0, availableSize.X - Gap);
            _details.Measure(new Vector2(width * 0.45f, availableSize.Y));
            _preview.Measure(new Vector2(width * 0.55f, availableSize.Y));
            return Vector2.Zero;
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var width = Math.Max(0, finalSize.X - Gap);
            var detailsWidth = width * 0.45f;
            _details.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(detailsWidth, finalSize.Y)));
            _preview.Arrange(UIBox2.FromDimensions(new Vector2(detailsWidth + Gap, 0), new Vector2(width - detailsWidth, finalSize.Y)));
            return finalSize;
        }
    }
}
