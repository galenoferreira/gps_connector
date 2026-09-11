namespace TwoG.Connector.Core;

/// <summary>
/// Nomes do produto num lugar só. Separa o que é marca (pode mudar) do que é
/// contrato com o sistema operacional ou com a v1.3.0 (não pode).
/// </summary>
public static class ProductIdentity
{
    public const string Name = "2G Connector";

    /// <summary>Nome até a v1.3.0. Usado só para migrar o que ela deixou no sistema.</summary>
    public const string LegacyName = "2G GPS Cliente";

    public const string ExeFileName = "2G-Connector.exe";
    public const string SetupFileName = "2G-Connector-Setup.exe";

    /// <summary>Pasta em %APPDATA% e %LOCALAPPDATA%.</summary>
    public const string DataFolderName = Name;
    public const string LegacyDataFolderName = LegacyName;

    /// <summary>Nome nas sentenças XGPS numa instalação nova, sem configuração gravada.</summary>
    public const string DefaultDeviceName = "2G Connector";

    /// <summary>
    /// Padrão até a v1.3.0. Quem atualiza sem nunca ter gravado configuração
    /// continua com ele: o nome aparece no EFB e não deve mudar sem aviso.
    /// </summary>
    public const string LegacyDefaultDeviceName = "2G GPS";

    public const string GitHubRepository = "galenoferreira/gps_connector";

    // ── Herdados da v1.3.0: NUNCA mudar ────────────────────────────────
    // São a forma de a v1.3.0 e as versões seguintes se enxergarem. Com os
    // mesmos nomes, se as duas forem lançadas (entrada antiga no EXE.xml, atalho
    // velho), só uma sobe; com nomes diferentes, as duas transmitiriam em dobro.
    public const string SingleInstanceMutexName = @"Local\TwoG.GpsClient.SingleInstance";
    public const string ShowWindowEventName = @"Local\TwoG.GpsClient.ShowWindow";
}
