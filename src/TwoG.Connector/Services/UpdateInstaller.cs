using System.Diagnostics;
using System.IO;
using TwoG.Connector.Core;

namespace TwoG.Connector.Services;

/// <summary>
/// Executa a instalação de uma atualização já verificada. Quem chama encerra o app
/// logo em seguida.
/// </summary>
internal static class UpdateInstaller
{
    /// <summary>
    /// Passado ao exe novo no modo avulso: espere o mutex em vez de sair como
    /// segunda instância — a versão anterior está terminando de sair.
    /// </summary>
    public const string UpdatedArg = "--updated";

    private const string OldSuffix = ".old";

    public static void Launch(PendingUpdate pending, InstallKind kind, string exePath,
                              IReadOnlyList<string> relaunchArgs, bool relaunch, Action releaseMutex)
    {
        switch (kind)
        {
            case InstallKind.Installer:
                LaunchInstaller(pending.FilePath, relaunchArgs, relaunch, releaseMutex);
                break;
            case InstallKind.Portable:
                SwapExecutable(pending.FilePath, exePath, relaunchArgs, relaunch, releaseMutex);
                break;
            default:
                throw new IOException("pasta do executável sem permissão de escrita");
        }
    }

    /// <summary>
    /// Setup silencioso. O mutex é liberado logo depois de o processo nascer: o
    /// AppMutex do Inno, em modo silencioso, CANCELA a instalação se nos achar vivos,
    /// e o setup leva centenas de ms para chegar a essa verificação.
    /// </summary>
    private static void LaunchInstaller(string setupPath, IReadOnlyList<string> relaunchArgs,
                                        bool relaunch, Action releaseMutex)
    {
        var start = new ProcessStartInfo(setupPath) { UseShellExecute = false };
        foreach (var arg in new[]
                 {
                     "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART",
                     // Se ainda estivermos saindo quando ele for copiar o exe, o
                     // Restart Manager nos fecha. Sem reabrir por conta própria: quem
                     // reabre é a entrada [Run] do setup.iss, com os nossos argumentos.
                     "/CLOSEAPPLICATIONS", "/FORCECLOSEAPPLICATIONS", "/NORESTARTAPPLICATIONS",
                 })
            start.ArgumentList.Add(arg);

        start.ArgumentList.Add(relaunch
            ? $"/relaunchargs={string.Join(' ', relaunchArgs)}"
            : "/norelaunch=1");

        Process.Start(start);
        releaseMutex();
    }

    /// <summary>
    /// Exe avulso: o Windows deixa renomear um exe em execução, só não sobrescrever.
    /// O caminho não muda, então o EXE.xml continua certo.
    /// </summary>
    private static void SwapExecutable(string newExePath, string exePath, IReadOnlyList<string> relaunchArgs,
                                       bool relaunch, Action releaseMutex)
    {
        var oldPath = exePath + OldSuffix;
        TryDelete(oldPath);                     // resto de uma troca anterior
        File.Move(exePath, oldPath);
        try
        {
            // Cópia, não move: a pasta de updates pode estar em outro volume.
            File.Copy(newExePath, exePath, overwrite: false);
        }
        catch
        {
            File.Move(oldPath, exePath);        // desfaz: o app continua como estava
            throw;
        }

        if (relaunch)
        {
            var start = new ProcessStartInfo(exePath) { UseShellExecute = false };
            foreach (var arg in relaunchArgs)
                start.ArgumentList.Add(arg);
            start.ArgumentList.Add(UpdatedArg);
            Process.Start(start);
        }
        releaseMutex();
    }

    /// <summary>Apaga o .old deixado pela troca do exe avulso. Melhor esforço.</summary>
    public static void CleanupAfterUpdate(string? exePath)
    {
        if (exePath is null)
            return;
        for (var i = 0; i < 5; i++)
        {
            if (TryDelete(exePath + OldSuffix))
                return;
            Thread.Sleep(200);                  // a versão anterior pode estar terminando de sair
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
