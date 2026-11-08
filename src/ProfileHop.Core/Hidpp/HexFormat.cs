using System.Globalization;

namespace ProfileHop.Core.Hidpp;

/// <summary>Hex text for HID++ frames (debug log) and memory dumps (CLI <c>status --raw</c>).</summary>
public static class HexFormat
{
    /// <summary>"11 01 0E 4B 00 ...": upper-case, space-separated.</summary>
    public static string Frame(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return "";
        return string.Create(bytes.Length * 3 - 1, bytes.ToArray(), static (span, data) =>
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0)
                    span[i * 3 - 1] = ' ';
                data[i].TryFormat(span[(i * 3)..], out _, "X2", CultureInfo.InvariantCulture);
            }
        });
    }

    /// <summary>One line per <paramref name="perLine"/> bytes: "0010  10 11 12 ..." (offset in hex).</summary>
    public static IReadOnlyList<string> Dump(ReadOnlySpan<byte> bytes, int perLine = 16)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(perLine, 1);
        var lines = new List<string>();
        for (int offset = 0; offset < bytes.Length; offset += perLine)
        {
            ReadOnlySpan<byte> row = bytes.Slice(offset, Math.Min(perLine, bytes.Length - offset));
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{offset:X4}  {Frame(row)}"));
        }
        return lines;
    }
}
