using System.Globalization;

namespace ProfileHop.Core.Tests.Hidpp.Fakes;

/// <summary>
/// Reads <c>tests/fixtures/probe-superstrike-2026-11-08.txt</c> (real <c>profilehop-cli probe</c> output, copied next
/// to the test assembly): the PRO X2 SUPERSTRIKE feature table.
/// </summary>
internal static class ProbeFixture
{
    public const string FileName = "probe-superstrike-2026-11-08.txt";

    /// <summary>(Index, Id, Name) rows of the "Features" table; Name is null where the CLI printed "?".</summary>
    public static IReadOnlyList<(byte Index, ushort Id, string? Name)> Features()
    {
        var rows = new List<(byte, ushort, string?)>();
        bool inTable = false;
        foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "fixtures", FileName)))
        {
            inTable |= line.StartsWith("Features (", StringComparison.Ordinal);
            if (!inTable)
                continue;
            // "  0x0E   0x8100  0x00    0  ONBOARD_PROFILES   <== onboard profiles"
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || !parts[0].StartsWith("0x", StringComparison.Ordinal) || !parts[1].StartsWith("0x", StringComparison.Ordinal))
                continue;
            byte index = byte.Parse(parts[0].AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            ushort id = ushort.Parse(parts[1].AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            rows.Add((index, id, parts[4] == "?" ? null : parts[4]));
        }
        return rows;
    }

    /// <summary>Feature IDs in index order (position = feature index).</summary>
    public static IEnumerable<ushort> FeatureIds() => Features().OrderBy(f => f.Index).Select(f => f.Id);
}
