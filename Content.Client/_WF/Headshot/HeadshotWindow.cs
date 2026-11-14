using System.Numerics;
using Content.Shared._WF.Headshot;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;

namespace Content.Client._WF.Headshot;

/// <summary>Edits a profile's headshot URL, with a preview the server fetches exactly as examiners will see it.</summary>
public sealed partial class HeadshotWindow : DefaultWindow
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entMan = default!;

    private readonly HeadshotSystem _headshot;
    private readonly LineEdit _edit;
    private readonly Button _previewButton;
    private readonly HeadshotPortrait _preview;
    private readonly Label _status;
    private Texture? _previewTexture;
    private string? _previewUrl;

    /// <summary>Raised on every edit with the cleaned URL, empty for none or when invalid.</summary>
    public Action<string>? OnUrlChanged;

    public HeadshotWindow(string url)
    {
        IoCManager.InjectDependencies(this);
        _headshot = _entMan.System<HeadshotSystem>();

        Title = Loc.GetString("wf-headshot-window-title");
        MinSize = new Vector2(460, 0);

        var hint = new RichTextLabel { HorizontalExpand = true };
        hint.SetMessage(Loc.GetString("wf-headshot-window-hint", ("size", HeadshotRules.ImageSize)));

        var hostList = _cfg.GetCVar(HeadshotCVars.AllowedHosts)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hosts = new RichTextLabel { HorizontalExpand = true };
        hosts.SetMessage(FormattedMessage.FromUnformatted(hostList.Length == 0
            ? Loc.GetString("wf-headshot-window-hosts-any")
            : Loc.GetString("wf-headshot-window-hosts", ("hosts", string.Join(", ", hostList)))));

        _edit = new LineEdit
        {
            HorizontalExpand = true,
            Text = url,
            PlaceHolder = Loc.GetString("wf-headshot-window-placeholder"),
            IsValid = text => text.Length <= HeadshotRules.MaxUrlLength,
        };
        _edit.OnTextChanged += _ => OnEdited();

        _previewButton = new Button { Text = Loc.GetString("wf-headshot-window-preview") };
        _previewButton.OnPressed += _ => RequestPreview();

        var clear = new Button { Text = Loc.GetString("wf-headshot-window-clear") };
        clear.OnPressed += _ =>
        {
            _edit.Text = string.Empty;
            OnEdited();
        };

        _preview = new HeadshotPortrait { HorizontalAlignment = HAlignment.Center };

        _status = new Label { ClipText = true };

        Contents.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            // Fixed, so a long URL scrolls inside the box instead of widening the window to fit it.
            SetWidth = 460,
            Margin = new Thickness(8),
            SeparationOverride = 6,
            Children =
            {
                hint,
                hosts,
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    SeparationOverride = 4,
                    Children = { _edit, _previewButton, clear },
                },
                _preview,
                _status,
            },
        });

        _headshot.PreviewReceived += OnPreviewReceived;
        OnClose += () =>
        {
            _headshot.PreviewReceived -= OnPreviewReceived;
            SetPreview(null);
        };

        UpdateStatus();
        if (_edit.Text.Length > 0 && HeadshotRules.IsValid(_edit.Text.Trim(), out _))
            RequestPreview();
    }

    private void OnEdited()
    {
        SetPreview(null);
        OnUrlChanged?.Invoke(UpdateStatus());
    }

    /// <summary>Shows whether the URL is usable. Returns the URL to store.</summary>
    private string UpdateStatus()
    {
        var url = _edit.Text.Trim();
        _status.StyleClasses.Remove("Danger");
        _previewButton.Disabled = true;

        if (url.Length == 0)
        {
            _status.Text = Loc.GetString("wf-headshot-status-none");
            return string.Empty;
        }

        if (!HeadshotRules.IsValid(url, out var reason))
        {
            _status.Text = Loc.GetString(reason);
            _status.StyleClasses.Add("Danger");
            return string.Empty;
        }

        _previewButton.Disabled = false;
        _status.Text = Loc.GetString("wf-headshot-status-unchecked");
        return url;
    }

    private void RequestPreview()
    {
        _previewUrl = _edit.Text.Trim();
        _previewButton.Disabled = true;
        _status.StyleClasses.Remove("Danger");
        _status.Text = Loc.GetString("wf-headshot-status-loading");
        _headshot.RequestPreview(_previewUrl);
    }

    private void OnPreviewReceived(HeadshotPreviewResultEvent ev)
    {
        if (ev.Url != _previewUrl || ev.Url != _edit.Text.Trim())
            return;

        _previewButton.Disabled = false;
        if (ev.Png != null && _headshot.LoadTexture(ev.Png) is { } texture)
        {
            SetPreview(texture);
            _status.Text = Loc.GetString("wf-headshot-status-ok");
            return;
        }

        SetPreview(null);
        _status.Text = Loc.GetString(ev.Error ?? "wf-headshot-error-format");
        _status.StyleClasses.Add("Danger");
    }

    private void SetPreview(Texture? texture)
    {
        _preview.Texture = texture;
        (_previewTexture as IDisposable)?.Dispose();
        _previewTexture = texture;
    }
}
