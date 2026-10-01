using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Arca.Core.Services;

public sealed class AppSettings
{
    // Auto-Backup Settings
    public bool AutoBackupEnabled { get; set; } = true;
    public int AutoBackupIntervalHours { get; set; } = 6; // Default: 6 hours
    public int MaxAutoBackupSnapshots { get; set; } = 15;
    public string? CustomBackupDirectory { get; set; }

    // Export Settings
    public string? DefaultExportDirectory { get; set; }

    // Language / Localization
    public string Language { get; set; } = "es"; // "es" or "en"

    // General App Behavior
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool AuditLoggingEnabled { get; set; } = true;
}

public sealed class SettingsService
{
    private readonly string _settingsFilePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Current { get; private set; }

    public SettingsService(string? baseDirectory = null)
    {
        var appData = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Arca");

        Directory.CreateDirectory(appData);
        _settingsFilePath = Path.Combine(appData, "settings.json");
        Current = Load();
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    Current = settings;
                    return Current;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Error reading settings: {ex.Message}");
        }

        Current = new AppSettings();
        return Current;
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Current = settings;
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Error saving settings: {ex.Message}");
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        try
        {
            Current = settings;
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Error saving settings: {ex.Message}");
        }
    }
}
