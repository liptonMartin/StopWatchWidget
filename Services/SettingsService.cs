using System.Text.Json;
using System.Text.Json.Serialization;
using StopwatchWidget.Models;
using Windows.Storage;

namespace StopwatchWidget.Services;

/// <summary>
/// Persists <see cref="WindowSettings"/> as JSON in the application's local folder
/// (<see cref="ApplicationData.Current"/>), so the widget reopens the way it was left.
/// </summary>
public sealed class SettingsService
{
    private const string FileName = "window-settings.json";

    private readonly string _filePath;

    /// <summary>
    /// Creates the service and resolves the on-disk location.
    /// </summary>
    public SettingsService()
    {
        _filePath = Path.Combine(ResolveFolderPath(), FileName);
    }

    /// <summary>The full path of the settings file (handy for diagnostics).</summary>
    public string FilePath => _filePath;

    /// <summary>
    /// Reads the saved settings, or <see langword="null"/> when there's nothing usable
    /// (first run, missing file or corrupted content).
    /// </summary>
    public WindowSettings? Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.WindowSettings);
        }
        catch (Exception)
        {
            // A broken file must never stop the app from starting; fall back to defaults.
            return null;
        }
    }

    /// <summary>
    /// Writes the settings to disk, overwriting any previous file.
    /// </summary>
    public void Save(WindowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.WindowSettings);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception)
        {
            // Persistence is best-effort and happens during shutdown; swallow failures.
        }
    }

    private static string ResolveFolderPath()
    {
        try
        {
            // Packaged apps store this under
            // %LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState.
            return ApplicationData.Current.LocalFolder.Path;
        }
        catch (Exception)
        {
            // Unpackaged runs have no package identity, so ApplicationData.Current
            // throws. Fall back to a plain folder under %LOCALAPPDATA%.
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StopwatchWidget");
        }
    }
}

/// <summary>
/// Source-generated JSON context so serialization keeps working under trimming
/// (the Release configuration enables <c>PublishTrimmed</c>).
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(WindowSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}
