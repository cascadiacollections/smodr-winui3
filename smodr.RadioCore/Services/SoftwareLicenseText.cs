namespace smodr.Services;

/// <summary>Lossless, bounded text runs so XAML never lays out the entire notice inventory at once.</summary>
public static class SoftwareLicenseText
{
    public const int MaxSectionLength = 2048;

    public static IReadOnlyList<string> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sections = new List<string>();
        for (var offset = 0; offset < text.Length;)
        {
            var length = Math.Min(MaxSectionLength, text.Length - offset);
            if (offset + length < text.Length)
            {
                var newline = text.AsSpan(offset, length).LastIndexOf('\n');
                if (newline >= length / 2)
                {
                    length = newline + 1;
                }

                // Do not break a surrogate pair or CRLF across separately rendered text runs.
                var last = text[offset + length - 1];
                var next = text[offset + length];
                if ((char.IsHighSurrogate(last) && char.IsLowSurrogate(next)) || (last == '\r' && next == '\n'))
                {
                    length--;
                }
            }

            sections.Add(text.Substring(offset, length));
            offset += length;
        }

        return sections.AsReadOnly();
    }
}
