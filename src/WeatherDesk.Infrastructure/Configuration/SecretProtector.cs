using System.Security.Cryptography;
using System.Text;

namespace WeatherDesk.Infrastructure.Configuration;

public sealed class SecretProtector : ISecretProtector
{
    private const string EntropyPurpose = "WeatherDesk.ApiKey.v1";

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("WeatherDesk credentials require Windows DPAPI.");
        }

        var input = plaintext.ToArray();
        try
        {
            return ProtectedData.Protect(
                input,
                Encoding.UTF8.GetBytes(EntropyPurpose),
                DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("WeatherDesk credentials require Windows DPAPI.");
        }

        return ProtectedData.Unprotect(
            protectedData.ToArray(),
            Encoding.UTF8.GetBytes(EntropyPurpose),
            DataProtectionScope.CurrentUser);
    }
}
