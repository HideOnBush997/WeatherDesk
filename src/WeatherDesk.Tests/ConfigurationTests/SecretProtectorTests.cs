using System.Text;
using WeatherDesk.Infrastructure.Configuration;

namespace WeatherDesk.Tests.ConfigurationTests;

public sealed class SecretProtectorTests
{
    [Fact]
    public void SecretProtector_encrypts_and_decrypts_for_the_current_user()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var protector = new SecretProtector();
        var plaintext = Encoding.UTF8.GetBytes("protector-roundtrip-check");

        var encrypted = protector.Protect(plaintext);
        var decrypted = protector.Unprotect(encrypted);

        Assert.False(plaintext.AsSpan().SequenceEqual(encrypted));
        Assert.Equal(plaintext, decrypted);
    }
}
