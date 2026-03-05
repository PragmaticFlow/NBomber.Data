using System.Text.Json;

namespace NBomber.Data.Tests;

public class TestSettings
{
    public long TestFileSizeMB { get; set; }
    public long MaxExecutionTimeMs { get; set; }
    public long MaxAllowedMemoryMB { get; set; }
    public long MaxAllowedMemoryMBConcurrency { get; set; }

    public long TestFileSizeBytes => TestFileSizeMB * 1024 * 1024;
    public long MaxAllowedMemoryBytes => MaxAllowedMemoryMB * 1024 * 1024;
    public long MaxAllowedMemoryBytesConcurrency => MaxAllowedMemoryMBConcurrency * 1024 * 1024;

    private static TestSettings? _instance;

    public static TestSettings Instance => _instance ??= Load();

    private static TestSettings Load()
    {
        var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "testsettings.json");

        if (!File.Exists(settingsPath))
            throw new FileNotFoundException($"Test settings file not found: {settingsPath}");

        var json = File.ReadAllText(settingsPath);
        var settings = JsonSerializer.Deserialize<Dictionary<string, TestSettings>>(json)
            ?? throw new InvalidOperationException("Failed to deserialize test settings");

        var isCI = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true"
            || Environment.GetEnvironmentVariable("CI") == "true";

        var environmentKey = isCI ? "CI" : "Local";

        if (!settings.TryGetValue(environmentKey, out var result))
            throw new InvalidOperationException($"Settings for environment '{environmentKey}' not found");

        return result;
    }
}
