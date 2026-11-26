using System.Diagnostics.CodeAnalysis;

namespace Content.Client._WF.DoomArcade.MeltySynth;

// Stand-ins for BCL types the client sandbox forbids, so the vendored code stays close to upstream.

internal sealed class ArgumentNullException : Exception
{
    public ArgumentNullException(string message) : base(message)
    {
    }
}

internal sealed class OverflowException : Exception
{
    public OverflowException(string message) : base(message)
    {
    }
}

internal static class Debug
{
    public static void Assert([DoesNotReturnIf(false)] bool condition)
    {
    }
}
