using Content.Client._WF.DoomArcade.Engine;
using Robust.Client.Input;
using static Robust.Client.Input.Keyboard;

namespace Content.Client._WF.DoomArcade;

/// <summary>Maps engine keys to Doom keys. Unmapped keys, such as the console key, stay with the game.</summary>
public static class DoomArcadeKeys
{
    public static DoomKey ToDoom(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
            return DoomKey.A + (key - Key.A);
        if (key is >= Key.Num0 and <= Key.Num9)
            return DoomKey.Num0 + (key - Key.Num0);
        if (key is >= Key.F1 and <= Key.F10)
            return DoomKey.F1 + (key - Key.F1);

        return key switch
        {
            Key.Escape => DoomKey.Escape,
            Key.Control => DoomKey.LControl,
            Key.Shift => DoomKey.LShift,
            Key.Alt => DoomKey.LAlt,
            Key.Space => DoomKey.Space,
            Key.Return or Key.NumpadEnter => DoomKey.Enter,
            Key.BackSpace => DoomKey.Backspace,
            Key.Tab => DoomKey.Tab,
            Key.Left => DoomKey.Left,
            Key.Right => DoomKey.Right,
            Key.Up => DoomKey.Up,
            Key.Down => DoomKey.Down,
            Key.Minus => DoomKey.Hyphen,
            Key.Equal => DoomKey.Equal,
            Key.Comma => DoomKey.Comma,
            Key.Period => DoomKey.Period,
            Key.Slash => DoomKey.Slash,
            Key.Pause => DoomKey.Pause,
            _ => DoomKey.Unknown,
        };
    }
}
