using System.Globalization;
using WeatherDesk.Domain.Models;

namespace WeatherDesk.App.Search;

public static class OfflinePlaceCatalog
{
    private static readonly Lazy<IReadOnlyList<Location>> Cached = new(Load);
    public static IReadOnlyList<Location> All => Cached.Value;

    private static IReadOnlyList<Location> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "china-places.csv");
        if (!File.Exists(path)) return Array.Empty<Location>();
        var rows = new Dictionary<string, (string Parent, int Deep, string Name, string Pinyin, string FullName)>(StringComparer.Ordinal);
        foreach (var line in ReadRecords(path).Skip(1))
        {
            var fields = ParseLine(line);
            if (fields.Count < 8 || !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var deep)) continue;
            rows[fields[0]] = (fields[1], deep, fields[3].Trim(), fields[5].Trim(), fields[7].Trim());
        }
        var places = new List<Location>();
        foreach (var (id, row) in rows)
        {
            if (row.Deep is not (1 or 2)) continue;
            var provinceId = id;
            while (rows.TryGetValue(provinceId, out var current) && current.Deep > 0) provinceId = current.Parent;
            var province = rows.TryGetValue(provinceId, out var provinceRow) ? provinceRow.Name.TrimEnd('省', '市') : row.Parent;
            var level = row.Deep == 1 ? InferPrefectureLevel(row.Name, row.FullName) : InferLevel(row.FullName);
            var suffix = GetAdministrativeSuffix(row.Name, row.FullName);
            var pinyinWords = row.Pinyin.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var pinyinQueries = new[] { string.Concat(pinyinWords), string.Join('-', pinyinWords) }
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var parentName = row.Deep == 2 && rows.TryGetValue(row.Parent, out var parentRow) ? parentRow.Name : null;
            places.Add(new Location($"offline-{id}", row.Name, province, "中国", "CN", 0, 0, "Asia/Shanghai", 8 * 3600)
            {
                Admin2 = parentName,
                AdministrativeLevel = level,
                AdministrativeSuffix = suffix,
                GeocodingName = pinyinQueries.ElementAtOrDefault(0),
                GeocodingNameHyphenated = pinyinQueries.ElementAtOrDefault(1)
            });
        }
        return places;
    }

    private static AdministrativeLevel InferLevel(string name) =>
        name.EndsWith("市", StringComparison.Ordinal) ? AdministrativeLevel.CountyCity :
        name.EndsWith("区", StringComparison.Ordinal) ? AdministrativeLevel.District : AdministrativeLevel.County;

    private static AdministrativeLevel InferPrefectureLevel(string name, string fullName) =>
        fullName.TrimEnd().EndsWith("市", StringComparison.Ordinal)
            ? AdministrativeLevel.PrefectureCity
            : AdministrativeLevel.PrefectureRegion;

    private static string? GetAdministrativeSuffix(string name, string fullName)
    {
        var trimmed = fullName.Trim();
        return trimmed.StartsWith(name, StringComparison.Ordinal) && trimmed.Length > name.Length
            ? trimmed[name.Length..]
            : null;
    }

    private static List<string> ParseLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var ch in line)
        {
            if (ch == '"') { quoted = !quoted; continue; }
            if (ch == ',' && !quoted) { result.Add(current.ToString()); current.Clear(); continue; }
            current.Append(ch);
        }
        result.Add(current.ToString());
        return result;
    }

    private static IEnumerable<string> ReadRecords(string path)
    {
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var ch in File.ReadAllText(path))
        {
            if (ch == '"') quoted = !quoted;
            if ((ch == '\r' || ch == '\n') && !quoted)
            {
                if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                continue;
            }
            if (ch != '\r' && ch != '\n') current.Append(ch);
        }
        if (current.Length > 0) yield return current.ToString();
    }
}
