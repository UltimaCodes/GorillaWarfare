using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// One cleaning rule for every name somebody types and everybody else sees - player names and
/// room names - applied when a name is typed and again whenever one is displayed. The display side
/// matters because the other people in a room are on their own clients, and their names arrive as
/// whatever they typed.
///
/// Names used to go straight into rich-text labels, so a name like "&lt;size=200&gt;" restyled
/// everybody's scoreboard, kill feed and standings (or the whole room browser, for a room name);
/// and nothing limited length, so a long name ran into the next scoreboard column.
/// </summary>
public static class PlayerNames
{
    public const int MaxLength = 16;
    public const int MaxRoomNameLength = 24;

    static readonly Regex Tags = new Regex("<[^>]*>");

    public static string Clean(string raw, int maxLength = MaxLength)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        string text = Tags.Replace(raw, string.Empty).Replace("<", string.Empty).Replace(">", string.Empty);

        StringBuilder kept = new StringBuilder(text.Length);

        foreach (char c in text)
        {
            if (!char.IsControl(c))
                kept.Append(c);
        }

        text = kept.ToString().Trim();
        return text.Length > maxLength ? text.Substring(0, maxLength).TrimEnd() : text;
    }

    /// For TMP_InputField.onValidateInput: angle brackets never make it into the field at all,
    /// rather than being typed and then quietly missing from what everybody else sees.
    public static char RejectAngleBrackets(string text, int index, char added) =>
        added == '<' || added == '>' ? '\0' : added;
}
