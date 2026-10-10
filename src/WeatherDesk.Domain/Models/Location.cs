namespace WeatherDesk.Domain.Models;

public enum AdministrativeLevel
{
    Unknown,
    PrefectureCity,
    PrefectureRegion,
    CountyCity,
    District,
    County
}

public sealed record Location(
    string Id,
    string Name,
    string? Admin1,
    string? Country,
    string? CountryCode,
    double Latitude,
    double Longitude,
    string? Timezone,
    int? UtcOffsetSeconds)
{
    public string? Admin2 { get; init; }
    public AdministrativeLevel AdministrativeLevel { get; init; }
    public string? GeocodingName { get; init; }
    public string? GeocodingNameHyphenated { get; init; }
    public string? AdministrativeSuffix { get; init; }

    public string DisplayName =>
        string.Join(", ", DistinctParts(Name, Admin2, Admin1, Country));

    public string WeatherDisplayName =>
        string.Join(", ", DistinctParts(Name, Admin1, Country));

    public string SearchDisplayName
    {
        get
        {
            var suffix = AdministrativeLevel switch
            {
                AdministrativeLevel.PrefectureCity or AdministrativeLevel.CountyCity => AdministrativeSuffix ?? "市",
                AdministrativeLevel.PrefectureRegion => AdministrativeSuffix ?? string.Empty,
                AdministrativeLevel.District => AdministrativeSuffix ?? "区",
                AdministrativeLevel.County => AdministrativeSuffix ?? "县",
                _ => string.Empty
            };
            var place = Name.EndsWith(suffix, StringComparison.Ordinal) || suffix.Length == 0 ? Name : Name + suffix;
            var province = (Admin1 ?? string.Empty).Trim();
            if (province.Length > 0 && !province.EndsWith("省", StringComparison.Ordinal) && !province.EndsWith("自治区", StringComparison.Ordinal) && !province.EndsWith("市", StringComparison.Ordinal))
                province += "省";
            return string.IsNullOrWhiteSpace(province) ? place : $"{place} {province}";
        }
    }

    private static IEnumerable<string> DistinctParts(params string?[] values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
