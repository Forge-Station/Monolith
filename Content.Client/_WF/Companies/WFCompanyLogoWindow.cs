using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Linq;
using Content.Client._WF.UserInterface.Controls;
using Content.Shared._WF.Companies;
using Content.Shared.StatusIcon;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.Client.Utility;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;

namespace Content.Client._WF.Companies;

/// <summary>Draws or imports company art: the 32x32 logo or an 8x8 rank icon.</summary>
public sealed class WFCompanyLogoWindow : DefaultWindow
{
    [Dependency] private IFileDialogManager _dialog = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IEntityManager _entities = default!;

    /// <summary>Largest file the importer will read.</summary>
    private const int MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>Largest image side the importer will decode.</summary>
    private const int MaxImportSide = 2048;

    private static readonly string[] Palette =
    {
        "#000000", "#FFFFFF", "#7F7F7F", "#C3C3C3", "#880015", "#ED1C24", "#FF7F27", "#FFF200",
        "#22B14C", "#00A2E8", "#3F48CC", "#A349A4", "#B97A57", "#FFAEC9", "#7FD3C4", "#E3B65C",
    };

    private readonly WFCompanyLogoCanvas _canvas;
    private readonly WolfgateColorPicker _colour = new() { HorizontalExpand = true, SelectorType = ColorSelectorSliders.ColorSelectorType.Hsv };
    private readonly Label _status = new();
    private readonly int _size;
    private readonly Action<byte[]?> _save;

    public WFCompanyLogoWindow(string title, int size, byte[]? pixels, Action<byte[]?> save)
    {
        IoCManager.InjectDependencies(this);
        _size = size;
        _save = save;
        _canvas = new WFCompanyLogoCanvas(size);
        Title = title;
        MinSize = new Vector2(780, 470);
        if (pixels?.Length == size * size * 4)
            _canvas.Pixels = (byte[]) pixels.Clone();

        var tools = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6, HorizontalExpand = true };
        var modes = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        var group = new ButtonGroup();
        foreach (var mode in Enum.GetValues<WFCompanyLogoCanvas.Tool>())
        {
            var button = new Button { Text = Loc.GetString("wf-company-logo-tool-" + mode.ToString().ToLowerInvariant()), ToggleMode = true,
                Group = group, Pressed = mode == _canvas.Mode };
            button.OnPressed += _ => _canvas.Mode = mode;
            modes.AddChild(button);
        }
        tools.AddChild(modes);
        tools.AddChild(new Label { Text = Loc.GetString("wf-company-logo-help-short") });

        var palette = new GridContainer { Columns = 8 };
        foreach (var hex in Palette)
        {
            var swatch = new Button { MinSize = new Vector2(28, 28), ModulateSelfOverride = Color.FromHex(hex), ToolTip = hex };
            swatch.OnPressed += _ => SetBrush(Color.FromHex(hex));
            palette.AddChild(swatch);
        }
        tools.AddChild(palette);
        _colour.Color = _canvas.Brush;
        _colour.OnColorChanged += colour => _canvas.Brush = colour;
        _canvas.ColourPicked += SetBrush;
        tools.AddChild(_colour);

        var import = new Button { Text = Loc.GetString("wf-company-logo-import") };
        import.OnPressed += _ => Import();
        var clear = new Button { Text = Loc.GetString("wf-company-logo-clear") };
        clear.OnPressed += _ => Array.Clear(_canvas.Pixels);
        var saveButton = new Button { Text = Loc.GetString("wf-company-logo-save") };
        saveButton.AddStyleClass("ButtonAccept");
        saveButton.OnPressed += _ => Save(false);
        var remove = new Button { Text = Loc.GetString("wf-company-logo-remove") };
        remove.OnPressed += _ => Save(true);
        var actions = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        actions.AddChild(import);
        actions.AddChild(clear);
        actions.AddChild(remove);
        actions.AddChild(saveButton);
        tools.AddChild(actions);
        tools.AddChild(_status);
        if (size == WFCompanyPolicy.IconSize)
            tools.AddChild(BuildPresets());

        var layout = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 12, Margin = new Thickness(8) };
        layout.AddChild(_canvas);
        layout.AddChild(tools);
        Contents.AddChild(layout);
    }

    /// <summary>Every job icon of the right size, to start a rank icon from.</summary>
    private Control BuildPresets()
    {
        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4, VerticalExpand = true };
        box.AddChild(new Label { Text = Loc.GetString("wf-company-icon-presets") });
        var grid = new GridContainer { Columns = 8 };
        var sprites = _entities.System<SpriteSystem>();
        foreach (var icon in _prototypes.EnumeratePrototypes<JobIconPrototype>().OrderBy(icon => icon.ID))
        {
            if (icon.Icon is not SpriteSpecifier.Rsi)
                continue;
            var button = new Button { MinSize = new Vector2(30, 30), ToolTip = icon.ID };
            button.AddChild(new TextureRect { Texture = sprites.Frame0(icon.Icon), Stretch = TextureRect.StretchMode.KeepAspectCentered,
                TextureScale = new Vector2(2, 2), MouseFilter = MouseFilterMode.Ignore });
            button.OnPressed += _ => OnPreset(_entities.System<WFCompanyClientSystem>().ReadIconPreset(icon.Icon));
            grid.AddChild(button);
        }
        box.AddChild(new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, MinHeight = 140, Children = { grid } });
        return box;
    }

    /// <summary>Copies a job icon the server read onto the canvas.</summary>
    private void OnPreset(byte[]? pixels)
    {
        if (pixels?.Length != _size * _size * 4)
        {
            _status.Text = Loc.GetString("wf-company-icon-preset-failed");
            return;
        }
        _canvas.Pixels = (byte[]) pixels.Clone();
        _status.Text = Loc.GetString("wf-company-icon-preset-loaded");
    }

    private void SetBrush(Color colour)
    {
        _canvas.Brush = colour;
        _colour.Color = colour;
    }

    private void Save(bool remove)
    {
        _save(remove ? null : (byte[]) _canvas.Pixels.Clone());
        Close();
    }

    /// <summary>Loads a PNG of any size, fitted into the canvas and averaged down to its pixels.</summary>
    private async void Import()
    {
        await using var file = await _dialog.OpenFile(new FileDialogFilters(new FileDialogFilters.Group("png")));
        if (file == null)
            return;
        if (file.Length > MaxFileBytes)
        {
            _status.Text = Loc.GetString("wf-company-logo-import-failed");
            return;
        }
        var bytes = new byte[file.Length];
        var read = 0;
        while (read < bytes.Length)
        {
            var count = await file.ReadAsync(bytes.AsMemory(read));
            if (count == 0)
                break;
            read += count;
        }
        try
        {
            // Read the size from the PNG header first, so a huge image never gets its pixels allocated.
            if (bytes.Length < 24 || bytes[12] != 'I' || bytes[13] != 'H' || bytes[14] != 'D' || bytes[15] != 'R' ||
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16)) is 0 or > MaxImportSide ||
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20)) is 0 or > MaxImportSide)
            {
                _status.Text = Loc.GetString("wf-company-logo-import-failed");
                return;
            }
            using var image = Image.Load<Rgba32>(new MemoryStream(bytes, false));
            _canvas.Pixels = Downsample(image, _size);
            _status.Text = Loc.GetString("wf-company-logo-imported");
        }
        catch (Exception)
        {
            _status.Text = Loc.GetString("wf-company-logo-import-failed");
        }
    }

    /// <summary>Averages blocks of the image into the logo, keeping its aspect ratio with transparent margins.</summary>
    private static byte[] Downsample(Image<Rgba32> image, int size)
    {
        var result = new byte[size * size * 4];
        var scale = Math.Max(image.Width, image.Height) / (float) size;
        var offsetX = (size - image.Width / scale) / 2f;
        var offsetY = (size - image.Height / scale) / 2f;
        var source = image.GetPixelSpan();
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var x0 = (int) ((x - offsetX) * scale);
                var y0 = (int) ((y - offsetY) * scale);
                var x1 = Math.Max(x0 + 1, (int) ((x + 1 - offsetX) * scale));
                var y1 = Math.Max(y0 + 1, (int) ((y + 1 - offsetY) * scale));
                float r = 0, g = 0, b = 0, a = 0, samples = 0;
                for (var sy = Math.Max(0, y0); sy < Math.Min(image.Height, y1); sy++)
                {
                    for (var sx = Math.Max(0, x0); sx < Math.Min(image.Width, x1); sx++)
                    {
                        var pixel = source[sy * image.Width + sx];
                        var alpha = pixel.A / 255f;
                        r += pixel.R * alpha;
                        g += pixel.G * alpha;
                        b += pixel.B * alpha;
                        a += alpha;
                        samples++;
                    }
                }
                if (samples == 0 || a <= 0)
                    continue;
                var i = (y * size + x) * 4;
                result[i] = (byte) Math.Clamp(r / a, 0, 255);
                result[i + 1] = (byte) Math.Clamp(g / a, 0, 255);
                result[i + 2] = (byte) Math.Clamp(b / a, 0, 255);
                result[i + 3] = (byte) Math.Clamp(a / samples * 255, 0, 255);
            }
        }
        return result;
    }
}
