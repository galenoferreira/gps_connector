using System.IO;
using System.Text.Json;

namespace TwoG.Connector.Configuration;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly string _defaultDeviceName;

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, Core.ProductIdentity.DataFolderName);
        var legacyDir = Path.Combine(appData, Core.ProductIdentity.LegacyDataFolderName);
        // Primeira execução depois da v1.3.0: traz a configuração antiga.
        Core.SettingsMigration.CopyLegacyIfMissing(dir, legacyDir);
        // Sem arquivo gravado, quem veio da v1.3.0 continua com "2G GPS".
        _defaultDeviceName = Core.SettingsMigration.DefaultDeviceName(legacyDir);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, Core.SettingsMigration.FileName);
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
        return new AppSettings { DeviceName = _defaultDeviceName };
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

    private AppSettings Sanitize(AppSettings s)
    {
        s.DeviceName = Core.XgpsSentences.SanitizeDeviceName(s.DeviceName);
        if (s.DeviceName.Length == 0) s.DeviceName = _defaultDeviceName;
        if (s.Port is < 1 or > 65535) s.Port = 49002;
        if (s.XgpsHz is < 0.5 or > 10) s.XgpsHz = 2.0;
        if (s.XattHz is < 1 or > 10) s.XattHz = 2.0;
        return s;
    }
}
