using Robust.Shared.Random;

namespace Content.Server.Chat.Systems;

public sealed partial class ChatSystem
{
    /// <summary>
    /// Returns the chat message of the emote whose trigger is exactly <paramref name="action"/>, or the action unchanged.
    /// Only a message that starts with the typed word counts, so a synonym trigger ("*giggles" for Laugh) stays as typed.
    /// Punctuation typed after the trigger replaces the message's own ("*trills?" gives "trills?").
    /// </summary>
    public string ResolveTypedEmote(EntityUid source, string action)
    {
        var word = action.AsSpan().Trim();
        var start = 0;
        var end = word.Length;
        while (start < end && char.IsPunctuation(word[start]))
            start++;
        while (end > start && char.IsPunctuation(word[end - 1]))
            end--;

        var typed = word[start..end].ToString().ToLowerInvariant();
        if (!_wordEmoteDict.TryGetValue(typed, out var emotes))
            return action;

        foreach (var emote in emotes)
        {
            if (emote.ChatMessages.Count == 0 || !AllowedToUseEmote(source, emote))
                continue;

            var messages = new List<string>();
            foreach (var id in emote.ChatMessages)
            {
                var text = Loc.GetString(id, ("entity", source));
                if (text.TrimStart().StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                    messages.Add(text);
            }

            if (messages.Count == 0)
                continue;

            var message = _random.Pick(messages);
            if (end == word.Length)
                return message;

            message = message.TrimEnd();
            var cut = message.Length;
            while (cut > 0 && char.IsPunctuation(message[cut - 1]))
                cut--;

            return message[..cut] + word[end..].ToString();
        }

        return action;
    }
}
