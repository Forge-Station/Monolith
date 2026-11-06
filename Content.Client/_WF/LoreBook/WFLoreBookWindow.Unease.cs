using System.Linq;
using System.Text;
using Content.Shared.Guidebook;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._WF.LoreBook;

/// <summary>
/// Some pages should not be read too closely. Each page's <see cref="GuideEntry.Unease"/> builds up while this book is
/// open: past a little, a few letters on each page come out wrong as it is turned to, and now and then a word is
/// written in glyphs; further on the book adds a quiet line of its own between paragraphs, and whispers play under it;
/// deep in, its title and page numbers slip too. A page's letters stay as they were drawn until it is turned again, but
/// its glyph words, in red, never sit still: their letters trade places, faster the more unsettled the book is, and
/// since a word only reorders its own letters it keeps its width and the lines don't move. Closing the book and
/// opening it again puts everything back.
/// </summary>
public sealed partial class WFLoreBookWindow
{
    /// <summary>How unsettled this copy of the book is, 0 to 1. A new window starts clean.</summary>
    private float _unease;

    /// <summary>Unease past which a word now and then is written in glyphs.</summary>
    private const float GlyphUnease = 0.3f;

    /// <summary>Unease past which the book writes lines of its own into pages.</summary>
    private const float WhisperUnease = 0.35f;

    /// <summary>Unease past which the title, chapter trail and page numbers go wrong too.</summary>
    private const float FrameUnease = 0.6f;

    private const int WhisperLines = 10;

    /// <summary>What a slipped letter turns into: nothing the page markup reads, and no heading marks.</summary>
    private const string Slips = "%*?~^!$";

    /// <summary>The glyph font words turn into.</summary>
    private const string GlyphFont = "WFLoreGlyphs";

    /// <summary>The glyphs fill their whole height, so this size stands them as tall as the page's capitals.</summary>
    private const int GlyphSize = 10;

    private const string GlyphColor = "#c43434";

    /// <summary>Seconds between glyph shifts at the first glyphs and at full unease.</summary>
    private const float ShiftSlow = 0.6f;
    private const float ShiftFast = 0.07f;

    /// <summary>The page's paragraphs that hold glyph words: the label, its message, and which nodes are glyph text.</summary>
    private readonly List<(RichTextLabel Label, MarkupNode[] Nodes, int[] Glyphs)> _glyphs = new();

    private float _nextShift;

    private readonly Random _slip = new();

    /// <summary>The chapter trail and page number as written, so each page slips them afresh.</summary>
    private string _trailText = string.Empty;
    private string _folioText = string.Empty;

    /// <summary>Unease past which whispers come from under the pages.</summary>
    private const float WhisperSoundUnease = 0.1f;

    /// <summary>The whispers' loudness at the first unease and at full, in decibels: always under everything else.</summary>
    private const float WhisperQuiet = -26f;
    private const float WhisperLoud = -12f;

    private static readonly SoundSpecifier Whispers = new SoundCollectionSpecifier("WFLoreBookWhispers");
    private static readonly SoundSpecifier PageTurn = new SoundCollectionSpecifier("WFLoreBookPageTurn");

    private EntityUid? _whisper;
    private float _nextWhisper = 2f;

    /// <summary>Reads a page into the book's unease and returns its markup as this copy of the book shows it.</summary>
    private string Unsettle(string markup, GuideEntryPrototype entry)
    {
        _unease = Math.Clamp(_unease + entry.Unease, 0f, 1f);
        _trailText = ChapterTrail.Text ?? string.Empty;
        _folioText = Folio.Text ?? string.Empty;
        UnsettleFrame();
        return Corrupt(markup);
    }

    /// <summary>The page as the book shows it now: slipped letters, traded letters, glyphs and whispers, by unease.</summary>
    private string Corrupt(string markup)
    {
        if (_unease <= 0f)
            return markup;

        var slip = 0.01f + 0.18f * MathF.Pow(_unease, 1.5f);
        var glyph = _unease >= GlyphUnease ? 0.01f + 0.05f * _unease + 0.1f * Math.Max(0f, (_unease - FrameUnease) / (1f - FrameUnease)) : 0f;
        var text = Distort(_slip, markup, slip, 0f, glyph);
        return _unease >= WhisperUnease ? Whisper(text) : text;
    }

    /// <summary>
    /// Goes word by word through the text outside tags: a word may turn to glyphs whole, have two letters trade places,
    /// or lose letters to noise.
    /// </summary>
    public static string Distort(Random rng, string markup, float slip, float trade, float glyph)
    {
        var builder = new StringBuilder(markup.Length + 64);
        var word = new StringBuilder();
        var depth = 0;
        // Headings are plain labels, which would print a glyph tag as text, so their words never turn to glyphs.
        var lineStart = true;
        var heading = false;

        void Flush()
        {
            if (word.Length == 0)
                return;

            var text = word.ToString();
            word.Clear();
            if (!heading && text.Length > 2 && rng.NextDouble() < glyph)
            {
                builder.Append($"[color={GlyphColor}][font=\"{GlyphFont}\" size={GlyphSize}]{text}[/font][/color]");
                return;
            }

            var letters = text.ToCharArray();
            if (letters.Length > 3 && rng.NextDouble() < trade)
            {
                var i = rng.Next(letters.Length - 1);
                (letters[i], letters[i + 1]) = (letters[i + 1], letters[i]);
            }

            for (var i = 0; i < letters.Length; i++)
            {
                if (rng.NextDouble() < slip)
                    letters[i] = Slips[rng.Next(Slips.Length)];
            }

            builder.Append(letters);
        }

        foreach (var c in markup)
        {
            if (c == '\n')
            {
                // A heading's last word ends here; settle it while the line still counts as a heading.
                Flush();
                lineStart = true;
                heading = false;
            }
            // A marker comment or tag in front of a heading's '#' doesn't stop it being a heading.
            else if (lineStart && depth == 0 && !char.IsWhiteSpace(c) && c is not ('<' or '['))
            {
                lineStart = false;
                heading = c == '#';
            }

            if (c is '<' or '[')
            {
                Flush();
                depth++;
            }

            if (depth == 0 && char.IsLetterOrDigit(c))
            {
                word.Append(c);
                continue;
            }

            Flush();
            builder.Append(c);
            if (c is '>' or ']')
                depth = Math.Max(0, depth - 1);
        }

        Flush();
        return builder.ToString();
    }

    /// <summary>Swaps a share of the letters in plain text for noise.</summary>
    private string Slip(string text, float strength)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(char.IsLetterOrDigit(c) && _slip.NextDouble() < strength ? Slips[_slip.Next(Slips.Length)] : c);
        }

        return builder.ToString();
    }

    /// <summary>Writes a line or two of the book's own between paragraphs.</summary>
    private string Whisper(string markup)
    {
        var lines = new List<string>(markup.Replace("\r\n", "\n").Split('\n'));
        var count = 1 + (int) (_unease * 3f);
        for (var i = 0; i < count; i++)
        {
            var gaps = new List<int>();
            for (var l = 1; l < lines.Count - 1; l++)
            {
                if (lines[l].Trim().Length == 0)
                    gaps.Add(l);
            }

            if (gaps.Count == 0)
                break;

            var line = Loc.GetString($"wf-lore-book-whisper-{_slip.Next(1, WhisperLines + 1)}");
            lines.Insert(gaps[_slip.Next(gaps.Count)], $"  [color=#8a2a36][italic]{line}[/italic][/color]\n");
        }

        return string.Join("\n", lines);
    }

    /// <summary>Deep in, the book's own furniture slips too.</summary>
    private void UnsettleFrame()
    {
        if (_unease < FrameUnease)
            return;

        var strength = (_unease - FrameUnease) / (1f - FrameUnease) * 0.6f + 0.1f;
        BookTitle.Text = Slip(Loc.GetString("wf-lore-book-title"), strength);
        BookSubtitle.Text = Slip(Loc.GetString("wf-lore-book-subtitle"), strength);
        ChapterTrail.Text = Slip(_trailText, strength);
        Folio.Text = Slip(_folioText, strength);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        UpdateWhispers(args.DeltaSeconds);
        UpdateGlyphs(args.DeltaSeconds);
    }

    /// <summary>Finds the paragraphs on the page just drawn that hold glyph words.</summary>
    private void GatherGlyphs()
    {
        _glyphs.Clear();
        Gather(PageBody);
    }

    private void Gather(Control control)
    {
        foreach (var child in control.Children)
        {
            if (child is RichTextLabel label && label.GetFormattedMessage() is { } message)
            {
                var nodes = message.Nodes.ToArray();
                var glyphs = new List<int>();
                for (var i = 0; i < nodes.Length - 1; i++)
                {
                    if (nodes[i] is { Name: "font", Closing: false } font
                        && font.Value.StringValue == GlyphFont
                        && nodes[i + 1].IsPlainText)
                    {
                        glyphs.Add(i + 1);
                    }
                }

                if (glyphs.Count > 0)
                    _glyphs.Add((label, nodes, glyphs.ToArray()));
            }

            Gather(child);
        }
    }

    /// <summary>Shuffles the letters of every glyph word on the page now and then, faster the more unsettled the book.</summary>
    private void UpdateGlyphs(float delta)
    {
        if (_glyphs.Count == 0)
            return;

        _nextShift -= delta;
        if (_nextShift > 0f)
            return;

        var share = Math.Clamp((_unease - GlyphUnease) / (1f - GlyphUnease), 0f, 1f);
        _nextShift = ShiftSlow + (ShiftFast - ShiftSlow) * share;
        foreach (var (label, nodes, glyphs) in _glyphs)
        {
            var message = new FormattedMessage(nodes.Length);
            var next = 0;
            for (var i = 0; i < nodes.Length; i++)
            {
                if (next < glyphs.Length && glyphs[next] == i)
                {
                    next++;
                    message.PushTag(new MarkupNode(Shuffle(nodes[i].Value.StringValue ?? string.Empty)));
                    continue;
                }

                message.PushTag(nodes[i]);
            }

            label.SetMessage(message, tagsAllowed: null);
        }
    }

    /// <summary>The same letters in another order.</summary>
    private string Shuffle(string word)
    {
        var letters = word.ToCharArray();
        for (var i = letters.Length - 1; i > 0; i--)
        {
            var j = _slip.Next(i + 1);
            (letters[i], letters[j]) = (letters[j], letters[i]);
        }

        return new string(letters);
    }

    /// <summary>Plays a whisper now and then, more often and a little louder the more unsettled the book is.</summary>
    private void UpdateWhispers(float delta)
    {
        if (_unease < WhisperSoundUnease)
            return;

        if (!_entMan.EntitySysManager.TryGetEntitySystem<SharedAudioSystem>(out var audio)
            || _whisper is { } playing && _entMan.EntityExists(playing))
            return;

        _whisper = null;
        _nextWhisper -= delta;
        if (_nextWhisper > 0f)
            return;

        var calm = 1f - _unease;
        _nextWhisper = 1.5f + calm * 10f + (float) _slip.NextDouble() * (2f + calm * 6f);
        var volume = WhisperQuiet + (WhisperLoud - WhisperQuiet) * _unease;
        _whisper = audio.PlayGlobal(Whispers, Filter.Local(), false, AudioParams.Default.WithVolume(volume))?.Entity;
    }

    /// <summary>A page turning: one of the book's turn sounds, picked at random.</summary>
    private void PlayPageTurn()
    {
        if (_entMan.EntitySysManager.TryGetEntitySystem<SharedAudioSystem>(out var audio))
            audio.PlayGlobal(PageTurn, Filter.Local(), false, AudioParams.Default.WithVolume(-6f));
    }

        public override void Close()
    {
        if (_whisper is { } playing && _entMan.EntitySysManager.TryGetEntitySystem<SharedAudioSystem>(out var audio))
            audio.Stop(playing);

        _whisper = null;
        base.Close();
    }
}
