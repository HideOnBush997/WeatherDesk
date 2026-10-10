# WeatherDesk

WeatherDesk is a .NET 8 and Avalonia desktop weather app with a weather-focused AI assistant. Weather data comes from Open-Meteo. The default AI mode is Demo, so the app can be exercised without a DeepSeek key.

## Current Status

M0 through M12 are implemented. The Avalonia MVVM workspace provides weather search and display, current/hourly/7-day forecasts, cached fallback, favorites and recent cities, startup recovery, an in-app settings panel, runtime theme and temperature-unit selection, and a constrained weather Agent. The default Demo mode needs no AI credential.

## Build and Test

```powershell
dotnet restore WeatherDesk.sln
dotnet build WeatherDesk.sln
dotnet test WeatherDesk.sln
```

On Windows, run `src/WeatherDesk.App/bin/Debug/net8.0/WeatherDesk.App.exe`. Settings are stored below `%LOCALAPPDATA%\\WeatherDesk`; the API key is protected in `credentials.dat` and is never written to `settings.json`. Selecting Demo mode requires no network AI credential. SQLite failures fall back to in-memory cache, city, and last-city state for the current process.

The main window can be used without configuration: search for a city, select it, and use the weather display and chat assistant. The Settings panel accepts `Auto`, `Demo`, or `DeepSeek`, stores the selected unit and theme, and masks the saved key. In `Auto`, DeepSeek is selected only when a key is present; failures fall back to Demo.

GitHub Actions runs restore, Release build, and solution tests on every push and pull request.

## Implementation Assumptions

- M1: The question analyzer remains a pure function. M6 supplies `AgentContext.Today` from `IClock.TodayIn`; without it, only ISO dates are resolved. Weekdays resolve to the nearest future date, with the current weekday meaning today. Weather advice uses the thresholds documented in `doc/prompt.md` section 14.
- M3: The weather snapshot records its hourly query date. Missing optional values represented by non-nullable domain fields default to `0`; nullable visibility, UV, sunrise, and sunset values remain null.
- M3: A refresh is keyed by `Location.Id`. A newer refresh for the same city cancels the older one, while different cities refresh independently.
- M10: Weather values remain in Celsius in the domain and are converted only for display; timestamps use the selected city's UTC offset when available.
- M12: The repository's final verification is `dotnet restore`, `dotnet build`, and `dotnet test`; the Windows CI workflow runs the same checks in Release configuration.
