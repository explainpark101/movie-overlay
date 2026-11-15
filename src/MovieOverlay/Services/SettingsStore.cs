using System.IO;
using System.Text.Json;
using MovieOverlay.Models;

namespace MovieOverlay.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;

    public bool HadExistingFile { get; private set; }

    public SettingsStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MovieOverlay");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            HadExistingFile = File.Exists(_path);
            if (!HadExistingFile)
            {
                return CreateDefault();
            }

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? CreateDefault();
        }
        catch
        {
            HadExistingFile = false;
            return CreateDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_path, json);
    }

    private static AppSettings CreateDefault() => new();
}
