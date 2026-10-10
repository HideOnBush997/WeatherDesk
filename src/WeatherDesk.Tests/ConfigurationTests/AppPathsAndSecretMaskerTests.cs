using WeatherDesk.Infrastructure.Configuration;

namespace WeatherDesk.Tests.ConfigurationTests;

public sealed class AppPathsAndSecretMaskerTests
{
    [Fact]
    public void GetRootDirectory_places_application_data_under_local_app_data()
    {
        var root = AppPaths.GetRootDirectory("D:\\Users\\tester\\AppData\\Local");

        Assert.Equal("D:\\Users\\tester\\AppData\\Local\\WeatherDesk", root);
    }

    [Theory]
    [InlineData("credential-123456789", "****************6789")]
    [InlineData("abcd", "****")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Mask_never_returns_a_short_secret_in_full(string? secret, string expected)
    {
        var masked = SecretMasker.Mask(secret);

        Assert.Equal(expected, masked);
        if (!string.IsNullOrEmpty(secret))
        {
            Assert.NotEqual(secret, masked);
        }
    }
}
