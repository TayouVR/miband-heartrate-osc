using Tomlyn;

namespace MiBandHR.Core.Configuration;

public class ConfigurationManager {
    private readonly string _configPath;
    private AppConfig _currentConfig;

    private static ConfigurationManager? _instance;

    public static ConfigurationManager Instance =>
        _instance ?? throw new InvalidOperationException("ConfigurationManager not initialized");

    public static void Initialize(string? configPath = null) {
        _instance = new ConfigurationManager(configPath);
    }

    private ConfigurationManager(string? configPath) {
        configPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "miband-heartrate",
            "config.toml"
        );

        _configPath = configPath;
        _currentConfig = LoadConfiguration();
    }

    public AppConfig CurrentConfig => _currentConfig;

    private AppConfig LoadConfiguration() {
        if (!File.Exists(_configPath)) {
            var defaultConfig = new AppConfig();
            SaveConfiguration(defaultConfig);
            return defaultConfig;
        }

        var tomlString = File.ReadAllText(_configPath);
        return TomlSerializer.Deserialize(tomlString, AppConfigTomlContext.Default.AppConfig)!;
    }

    public void SaveConfiguration(AppConfig? config = null) {
        if (config != null) {
            _currentConfig = config;
        }

        var tomlString = TomlSerializer.Serialize(_currentConfig, AppConfigTomlContext.Default.AppConfig);
        var directory = Path.GetDirectoryName(_configPath);
        if (directory != null && !Directory.Exists(directory)) {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_configPath, tomlString);
    }
}
