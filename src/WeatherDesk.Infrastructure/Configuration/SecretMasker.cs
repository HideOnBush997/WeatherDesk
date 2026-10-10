namespace WeatherDesk.Infrastructure.Configuration;

public static class SecretMasker
{
    private const int VisibleSuffixLength = 4;

    public static string Mask(string? secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return string.Empty;
        }

        var visibleLength = Math.Min(VisibleSuffixLength, secret.Length);
        var maskedLength = secret.Length - visibleLength;
        var suffix = visibleLength == secret.Length ? string.Empty : secret[^visibleLength..];

        return new string('*', Math.Max(maskedLength, VisibleSuffixLength)) + suffix;
    }
}
