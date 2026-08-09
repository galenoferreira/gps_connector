using System.IO;
using System.Text.Json;

namespace TwoG.GpsClient.Configuration;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "2G GPS Cliente");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path));
                if (settings is not null)
                    return Sanitize(settings);
            }
        }
        catch (Exception)
        {
            // Arquivo corrompido ou ilegível: volta ao padrão.
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception)
        {
            // Falha ao persistir não deve derrubar o app.
        }
    }

    private static AppSettings Sanitize(AppSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.DeviceName)) s.DeviceName = "2G GPS";
        s.DeviceName = s.DeviceName.Replace(",", " ").Trim();
        if (s.Port is < 1 or > 65535) s.Port = 49002;
        if (s.XgpsHz is < 0.5 or > 10) s.XgpsHz = 5.0;
        if (s.XattHz is < 1 or > 10) s.XattHz = 5.0;
        return s;
    }
}
