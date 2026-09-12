using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Reflection;
using TwoG.Connector.Configuration;
using TwoG.Connector.Core;

namespace TwoG.Connector.Services;

/// <summary>
/// Orquestra o auto-update: verifica 60 s depois de abrir e a cada 6 h, baixa em
/// segundo plano e instala só nos momentos que <see cref="UpdatePolicy"/> permite.
/// Nenhuma falha daqui pode derrubar o app.
/// </summary>
// Público como os demais serviços: o MainViewModel, público, o recebe no construtor
// (internal daria CS0051).
public sealed class UpdateService : IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan DownloadBudget = TimeSpan.FromMinutes(30);

    private readonly AppSettings _settings;
    private readonly string _exePath;
    private readonly string _updatesDir;
    private readonly HttpClient _http;
    private readonly UpdateChecker _checker;
    private Timer? _timer;
    private int _checking;

    private UpdateService(AppSettings settings, AppVersion current, bool isReleaseBuild,
                          string exePath, string updatesDir, InstallKind kind, UpdateFeed feed)
    {
        _settings = settings;
        CurrentVersion = current;
        IsReleaseBuild = isReleaseBuild;
        _exePath = exePath;
        _updatesDir = updatesDir;
        Kind = kind;

        // Buffer pequeno: vale para manifesto e assinatura (bufferizados); o binário
        // vem por stream, com limite de tamanho no próprio UpdateChecker.
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(2), MaxResponseContentBufferSize = 64 * 1024 };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"2G-Connector/{current}");
        _checker = new UpdateChecker(_http, feed, updatesDir, UpdateKeys.Accepted);
        Pending = PendingUpdateStore.Load(updatesDir);
        // Sobra da atualização que acabou de dar certo: some já na partida, antes de
        // qualquer gatilho — com o auto-update desligado o TryApply nem chegaria a olhar.
        if (Pending is not null && Pending.IsStaleFor(current))
        {
            PendingUpdateStore.Clear(updatesDir);
            Pending = null;
        }
    }

    public static UpdateService Create(AppSettings settings)
    {
        var exePath = Environment.ProcessPath ?? "";
        var exeDir = Path.GetDirectoryName(exePath) ?? "";
        var kind = InstallKindDetector.Detect(ListFiles(exeDir), IsWritable(exeDir));
        var updatesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductIdentity.DataFolderName, "updates");
        return new UpdateService(settings, ReadCurrentVersion(), ReadIsReleaseBuild(), exePath, updatesDir, kind, ResolveFeed());
    }

    public AppVersion CurrentVersion { get; }

    /// <summary>Só builds de tag têm canal; os demais nunca se atualizam.</summary>
    public bool IsReleaseBuild { get; }

    public bool IsEnabled => IsReleaseBuild && _settings.AutoUpdate;

    public InstallKind Kind { get; }

    public PendingUpdate? Pending { get; private set; }

    public AppVersion? LatestSeen { get; private set; }

    public DateTime? LastCheckUtc { get; private set; }

    public string? LastError { get; private set; }

    public void Start()
    {
        if (IsReleaseBuild)
            _timer = new Timer(_ => _ = CheckNowAsync(), null, FirstCheckDelay, CheckInterval);
    }

    public async Task CheckNowAsync()
    {
        if (!IsEnabled || Interlocked.Exchange(ref _checking, 1) == 1)
            return;
        try
        {
            using var budget = new CancellationTokenSource(DownloadBudget);
            var result = await _checker.CheckAsync(CurrentVersion, InstallKindDetector.AssetFor(Kind), budget.Token);
            LastCheckUtc = DateTime.UtcNow;
            LatestSeen = result.Latest ?? LatestSeen;
            LastError = result.Status == UpdateCheckStatus.Failed ? result.Error : null;
            Pending = PendingUpdateStore.Load(_updatesDir);
        }
        catch (Exception ex)
        {
            LastError = $"falha na verificação: {ex.Message}";
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    /// <summary>
    /// Instala a atualização pendente, se a política permitir. Devolve true quando a
    /// instalação foi iniciada — quem chama deve encerrar o app em seguida.
    /// </summary>
    public bool TryApply(UpdateTrigger trigger, bool inFlight, IReadOnlyList<string> relaunchArgs,
                         bool relaunch, Action releaseMutex, Func<bool>? confirmInFlight = null)
    {
        if (!IsReleaseBuild)
            return false;
        // Desligado nas Configurações: nada automático; o botão continua valendo.
        if (trigger != UpdateTrigger.Manual && !_settings.AutoUpdate)
            return false;

        var pending = PendingUpdateStore.Load(_updatesDir);
        Pending = pending;
        // Mesma regra que a faixa da interface usa: o que não passa aqui não é oferecido lá.
        if (pending is null || !pending.CanBeInstalledBy(Kind, CurrentVersion))
            return false;

        switch (UpdatePolicy.Decide(trigger, inFlight, pending.Attempts))
        {
            case UpdateDecision.Skip:
                return false;
            case UpdateDecision.ConfirmFirst when confirmInFlight?.Invoke() != true:
                return false;
        }

        if (!PendingUpdateStore.FileIsIntact(pending))
        {
            PendingUpdateStore.Clear(_updatesDir);
            Pending = null;
            LastError = "arquivo da atualização alterado ou incompleto — será baixado de novo";
            return false;
        }

        try
        {
            // Dentro do try: se a tentativa não puder ser gravada, não instala (sem o
            // contador, uma instalação que falha se repetiria a cada partida) e não
            // lança — no gatilho Startup isto roda antes de a janela existir.
            Pending = PendingUpdateStore.RecordAttempt(_updatesDir, pending);
            UpdateInstaller.Launch(pending, Kind, _exePath, relaunchArgs, relaunch, releaseMutex);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            LastError = $"falha ao instalar a v{pending.Version}: {ex.Message}";
            return false;
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _http.Dispose();
    }

    private static UpdateFeed ResolveFeed()
    {
        // Só HTTPS: a assinatura já protege o conteúdo, mas não há motivo para aceitar menos.
        var overrideUrl = Environment.GetEnvironmentVariable("TWOG_UPDATE_FEED");
        return Uri.TryCreate(overrideUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps
            ? UpdateFeed.Beside(url)
            : UpdateFeed.GitHubLatest(ProductIdentity.GitHubRepository);
    }

    private static AppVersion ReadCurrentVersion()
    {
        var info = typeof(UpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return AppVersion.TryParse(info, out var version) ? version : AppVersion.Parse("0.0.0");
    }

    private static bool ReadIsReleaseBuild() =>
        typeof(UpdateService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(a => a.Key == "UpdateChannel" && a.Value == "stable");

    private static string[] ListFiles(string dir)
    {
        try
        {
            return Directory.GetFiles(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".2g-write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
