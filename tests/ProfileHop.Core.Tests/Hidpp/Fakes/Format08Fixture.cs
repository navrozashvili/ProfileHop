using System.Globalization;
using System.Text;
using ProfileHop.Core.Hidpp;

namespace ProfileHop.Core.Tests.Hidpp.Fakes;

/// <summary>
/// Reads <c>tests/fixtures/profiles-superstrike-format08-2026-11-08.txt</c>: the first 0x30 bytes and the name of each
/// real PRO X2 SUPERSTRIKE profile sector (profile format 0x08). <see cref="Sector"/> rebuilds a 255-byte sector:
/// recorded bytes, name at 0xA0, 0xFF elsewhere, recomputed CRC.
/// </summary>
internal static class Format08Fixture
{
    public const string FileName = "profiles-superstrike-format08-2026-11-08.txt";
    public const int SectorSize = 255;

    public static byte[] Sector(ushort sector)
    {
        foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "fixtures", FileName)))
        {
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line))
                continue;
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (ushort.Parse(parts[0].AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) != sector)
                continue;

            var data = new byte[SectorSize];
            Array.Fill(data, (byte)0xFF);
            for (int i = 2; i < parts.Length; i++)
                data[i - 2] = byte.Parse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (parts[1] != "-")
                Encoding.Unicode.GetBytes(parts[1] + "\0").CopyTo(data, 0xA0);
            ushort crc = ProfileSector.CrcCcitt(data.AsSpan(0, SectorSize - 2));
            data[^2] = (byte)(crc >> 8);
            data[^1] = (byte)crc;
            return data;
        }
        throw new KeyNotFoundException($"sector 0x{sector:X4} not in {FileName}");
    }
}
