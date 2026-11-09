using System.Text;

namespace Mentor.Core.Agreements;

/// <summary>
/// How a value written into an agreement draft or its administrative annex is normalised before it is stored, so that
/// the same words always make the same content and hash: line breaks become a line feed, characters take their
/// composed Unicode form (NFC) and the value is trimmed. Text that is not valid Unicode, or that holds a control
/// character other than a line feed or a tab, is refused rather than repaired.
/// </summary>
internal static class AgreementText
{
    public enum Problem
    {
        None,

        /// <summary>The value holds an unpaired surrogate or a control character.</summary>
        NotText,

        /// <summary>The value spans several lines where one line is expected.</summary>
        NotOneLine,

        TooLong,
    }

    /// <summary>The normalised value, null when blank, and the problem that refuses it.</summary>
    public static Problem Normalize(string? submitted, int maxLength, bool lineBreaks, out string? value)
    {
        value = null;
        if (submitted is null)
        {
            return Problem.None;
        }

        string composed;
        try
        {
            composed = submitted.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return Problem.NotText;
        }

        var trimmed = composed.Trim();
        foreach (var character in trimmed)
        {
            if (character is '\n' or '\t')
            {
                if (!lineBreaks)
                {
                    return Problem.NotOneLine;
                }
            }
            else if (char.IsControl(character))
            {
                return Problem.NotText;
            }
        }

        if (trimmed.Length > maxLength)
        {
            return Problem.TooLong;
        }

        value = trimmed.Length == 0 ? null : trimmed;
        return Problem.None;
    }
}
