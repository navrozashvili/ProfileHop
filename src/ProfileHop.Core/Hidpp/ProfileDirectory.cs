namespace ProfileHop.Core.Hidpp;

/// <param name="Position">1-based position in the directory.</param>
/// <param name="Sector">Profile sector; for user profiles this is the profile number (Solaar convention).</param>
public readonly record struct ProfileDirectoryEntry(int Position, ushort Sector, bool Enabled);

/// <summary>
/// Profile directory in onboard sector 0x0000: 4-byte entries [sector hi, sector lo, enabled, reserved],
/// ending at an entry starting FF FF. See docs/protocol-verification.md.
/// </summary>
public static class ProfileDirectory
{
    public const ushort Sector = 0x0000;
    public const int ChunkSize = 16;
    public const int MaxChunks = 16;

    /// <summary>Reads the directory through memoryRead.</summary>
    public static IReadOnlyList<ProfileDirectoryEntry> Read(OnboardProfilesFeature onboard, ProfilesDescription description) =>
        Parse(offset => onboard.MemoryRead(Sector, (ushort)offset), description.SectorSize,
            description.ProfileCount == 0 ? int.MaxValue : description.ProfileCount);

    /// <summary>
    /// Parses the directory, reading 16-byte chunks on demand. Stops at FF FF, after <paramref name="maxEntries"/>
    /// entries, at the end of the sector, or after <see cref="MaxChunks"/> chunks. Like libratbag, a chunk that
    /// would run past the sector end is read from <c>sectorSize - 16</c> instead.
    /// </summary>
    public static IReadOnlyList<ProfileDirectoryEntry> Parse(Func<int, byte[]> readChunk, int sectorSize, int maxEntries)
    {
        int limit = Math.Clamp(sectorSize, ChunkSize, ChunkSize * MaxChunks);
        var buffer = new byte[limit];
        int loaded = 0;
        int chunks = 0;
        var entries = new List<ProfileDirectoryEntry>();

        for (int i = 0; i < maxEntries; i++)
        {
            int end = 4 * i + 4;
            if (end > limit)
                break;
            while (loaded < end)
            {
                if (chunks == MaxChunks)
                    return entries;
                int offset = Math.Min(loaded, limit - ChunkSize);
                byte[] chunk = readChunk(offset);
                chunk.AsSpan(0, ChunkSize).CopyTo(buffer.AsSpan(offset));
                loaded = offset + ChunkSize;
                chunks++;
            }

            int at = 4 * i;
            if (buffer[at] == 0xFF && buffer[at + 1] == 0xFF)
                break;
            entries.Add(new ProfileDirectoryEntry(i + 1, (ushort)((buffer[at] << 8) | buffer[at + 1]), buffer[at + 2] != 0));
        }
        return entries;
    }

    /// <summary>Enabled user profiles (sector 0x0001..0x00FF), ascending, distinct.</summary>
    public static IReadOnlyList<int> EnabledProfiles(IEnumerable<ProfileDirectoryEntry> entries) =>
        entries.Where(e => e.Enabled && e.Sector is >= 1 and <= 0xFF)
            .Select(e => (int)e.Sector)
            .Distinct()
            .Order()
            .ToList();
}
