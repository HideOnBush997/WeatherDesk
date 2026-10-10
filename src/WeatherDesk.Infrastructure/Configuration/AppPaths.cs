namespace WeatherDesk.Infrastructure.Configuration;

public static class AppPaths
{
    private const string ApplicationDirectoryName = "WeatherDesk";

    public static string RootDirectory => GetRootDirectory(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static string DatabasePath => Path.Combine(RootDirectory, "weatherdesk.db");

    public static string SettingsPath => Path.Combine(RootDirectory, "settings.json");

    public static string CredentialsPath => Path.Combine(RootDirectory, "credentials.dat");

    public static string GetRootDirectory(string localApplicationDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(localApplicationDataDirectory))
        {
            throw new ArgumentException("Local application data directory is required.", nameof(localApplicationDataDirectory));
        }

        return Path.GetFullPath(Path.Combine(localApplicationDataDirectory, ApplicationDirectoryName));
    }
}
