using System.Buffers.Binary;
using QNotch.Core;

namespace QNotch.Modules.Fdm;

/// <summary>Reads FDM's periodically saved HTTP file sections while its window is hidden.</summary>
internal sealed class FdmCheckpoints
{
    static readonly byte[] SectionsKey = Convert.FromHexString("0000001000730065006300740069006F006E00730000000900");
    static readonly byte[] OffsetKey = Convert.FromHexString("0000000C006F006600660073006500740000000400");
    static readonly byte[] DoneKey = Convert.FromHexString("000000080064006F006E00650000000400");
    static readonly byte[] SizeKey = Convert.FromHexString("0000000800730069007A00650000000400");
    static readonly TimeSpan Freshness = TimeSpan.FromMinutes(2.5);
    readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Softdeluxe", "Free Download Manager", "db.sqlite");

    readonly Dictionary<string, Sample> _samples = new();
    DateTime _readAt;
    sealed record Sample(long Bytes, long Total, DateTime Checkpoint, DateTime LastMovement, double Speed);

    public DownloadTransfer? Read()
    {
        var now = DateTime.UtcNow;
        var savedAt = File.GetLastWriteTimeUtc(_path);
        if (now - savedAt > Freshness) return null;
        if (savedAt != _readAt)
        {
            try
            {
                var rows = LocalSqlite.ReadRows(_path, """
                    SELECT id, hex(files) FROM downloads
                    WHERE coalesce(finishedTime, 0) = 0 AND length(files) BETWEEN 1 AND 262144
                    ORDER BY id DESC LIMIT 32
                    """);
                var present = new HashSet<string>();
                foreach (var row in rows)
                {
                    if (!ReadSections(Convert.FromHexString(row[1]), out var downloaded, out var total) || downloaded >= total) continue;
                    present.Add(row[0]);
                    if (_samples.TryGetValue(row[0], out var previous) && previous.Total == total)
                    {
                        if (downloaded == previous.Bytes) continue;
                        var elapsed = (savedAt - previous.Checkpoint).TotalSeconds;
                        var speed = downloaded > previous.Bytes && elapsed > 0 ? (downloaded - previous.Bytes) / elapsed : 0;
                        _samples[row[0]] = new(downloaded, total, savedAt, savedAt, speed);
                    }
                    else _samples[row[0]] = new(downloaded, total, savedAt, savedAt, 0);
                }
                foreach (var id in _samples.Keys.Where(id => !present.Contains(id)).ToArray()) _samples.Remove(id);
                _readAt = savedAt;
            }
            catch (IOException) { return null; }
        }

        var sample = _samples.Values.Where(sample => now - sample.LastMovement <= Freshness)
            .MaxBy(sample => sample.LastMovement);
        if (sample is null) return null;
        var remainingSeconds = sample.Speed > 0 ? (sample.Total - sample.Bytes) / sample.Speed : 0;
        var remaining = remainingSeconds is > 0 and < 8_640_000
            ? TimeSpan.FromSeconds(remainingSeconds).ToString(remainingSeconds >= 3600 ? @"h\h\ mm\m" : @"m\m\ ss\s") : null;
        var speedText = sample.Speed >= 1_000_000 ? $"≈{sample.Speed / 1_000_000:0.##} MB/s"
            : sample.Speed > 0 ? $"≈{sample.Speed / 1000:0.#} KB/s" : null;
        return new DownloadTransfer("FDM", sample.Bytes * 100d / sample.Total, speedText, remaining is null ? null : "≈" + remaining, "Saved progress");
    }

    internal static bool ReadSections(ReadOnlySpan<byte> data, out long downloaded, out long total)
    {
        downloaded = total = 0;
        if (data.Length < 4 || BinaryPrimitives.ReadInt32BigEndian(data) != 1) return false;
        var start = data.IndexOf(SectionsKey);
        if (start < 0) return false;
        var sections = data[(start + SectionsKey.Length)..];
        if (sections.Length < 4) return false;
        var count = BinaryPrimitives.ReadInt32BigEndian(sections);
        if (count is < 1 or > 2048) return false;
        sections = sections[4..];
        ReadOnlySpan<byte> sectionMap = [0, 0, 0, 28, 0, 0, 0, 0, 3];
        for (var index = 0; index < count; index++)
        {
            if (!sections.StartsWith(sectionMap)) return false;
            sections = sections[sectionMap.Length..];
            if (!ReadNumber(ref sections, OffsetKey, out var offset) || offset != total
                || !ReadNumber(ref sections, DoneKey, out var done) || !ReadNumber(ref sections, SizeKey, out var size)
                || size <= 0 || done < 0 || done > size || size > long.MaxValue - total) return false;
            downloaded += done;
            total += size;
        }
        return true;
    }

    static bool ReadNumber(ref ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, out long value)
    {
        value = 0;
        if (data.Length < key.Length + sizeof(long) || !data.StartsWith(key)) return false;
        value = BinaryPrimitives.ReadInt64BigEndian(data[key.Length..]);
        data = data[(key.Length + sizeof(long))..];
        return true;
    }
}
