using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace TwoG.GpsClient.Services;

/// <summary>
/// Disponibiliza as DLLs do SimConnect a partir de recursos embutidos no próprio
/// executável, para que o app seja distribuído como um único arquivo.
///
/// Por que não deixar o bundle single-file cuidar disso: o wrapper gerenciado
/// (Microsoft.FlightSimulator.SimConnect.dll) é mixed-mode C++/CLI, e a doc da
/// Microsoft é explícita — "Managed C++ components aren't well suited for single
/// file deployment" — porque assemblies do bundle são carregados da MEMÓRIA, o que
/// não funciona para mixed-mode. Aqui elas são extraídas para
/// %LOCALAPPDATA%\2G GPS Cliente\runtime\&lt;versão&gt;\ e carregadas de arquivos reais
/// em disco, que é o cenário suportado.
///
/// <see cref="Ensure"/> precisa rodar ANTES de qualquer código que toque tipos do
/// SimConnect (ver o factory com NoInlining em App.xaml.cs).
/// </summary>
internal static class SimConnectRuntime
{
    private const string ManagedResource = "TwoG.GpsClient.Native.Microsoft.FlightSimulator.SimConnect.dll";
    private const string NativeResource = "TwoG.GpsClient.Native.SimConnect.dll";
    private const string ManagedFileName = "Microsoft.FlightSimulator.SimConnect.dll";
    private const string NativeFileName = "SimConnect.dll";
    private const string ManagedAssemblyName = "Microsoft.FlightSimulator.SimConnect";

    private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr reserved, uint flags);

    private static bool _ready;

    /// <summary>Mensagem do último erro de preparação, ou null se tudo correu bem.</summary>
    public static string? Error { get; private set; }

    public static void Ensure()
    {
        if (_ready)
            return;

        try
        {
            var version = typeof(SimConnectRuntime).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "2G GPS Cliente", "runtime", version);
            Directory.CreateDirectory(dir);

            var nativePath = Extract(NativeResource, dir, NativeFileName);
            var managedPath = Extract(ManagedResource, dir, ManagedFileName);

            // A nativa precisa estar carregada ANTES do wrapper: quando o CLR carrega
            // o assembly mixed-mode, o loader do Windows resolve o import "SimConnect.dll"
            // — se o módulo já estiver no processo com esse nome, resolve direto.
            if (LoadLibraryEx(nativePath, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH) == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"Falha ao carregar SimConnect.dll (erro {Marshal.GetLastWin32Error()}).");

            AssemblyLoadContext.Default.Resolving += (_, name) =>
                string.Equals(name.Name, ManagedAssemblyName, StringComparison.OrdinalIgnoreCase)
                    ? Assembly.LoadFrom(managedPath)
                    : null;

            _ready = true;
            Error = null;
        }
        catch (Exception ex)
        {
            // Não derruba o app: a UI mostra o erro e o resto (rede, configurações)
            // continua funcionando.
            Error = ex.Message;
        }
    }

    private static string Extract(string resourceName, string dir, string fileName)
    {
        var target = Path.Combine(dir, fileName);
        using var stream = typeof(SimConnectRuntime).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Recurso embutido ausente: {resourceName}");

        // Já extraída nesta versão? (o diretório é versionado, então tamanho basta)
        if (File.Exists(target) && new FileInfo(target).Length == stream.Length)
            return target;

        var tmp = Path.Combine(dir, $"{fileName}.{Environment.ProcessId}.tmp");
        using (var file = File.Create(tmp))
            stream.CopyTo(file);

        try
        {
            File.Move(tmp, target, overwrite: true);
        }
        catch (Exception) when (File.Exists(target))
        {
            // Outra instância chegou primeiro, ou o arquivo está em uso por um
            // processo que já o carregou: o que está lá serve.
            TryDelete(tmp);
        }
        return target;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Lixo temporário não é motivo para falhar.
        }
    }
}
