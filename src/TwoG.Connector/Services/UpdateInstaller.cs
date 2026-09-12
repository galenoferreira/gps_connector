using System.Diagnostics;
using System.Globalization;
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
    /// segunda instância — quem fez a troca está terminando de sair.
    /// </summary>
    public const string UpdatedArg = "--updated";

    /// <summary>
    /// Pedido de troca do exe avulso. A versão anterior roda o exe NOVO direto da pasta
    /// de updates com <c>--replace &lt;exe&gt; &lt;pid da anterior&gt; &lt;1|0&gt; [argumentos para reabrir...]</c>
    /// (1 = reabrir depois). É contrato entre versões: nunca mudar o formato.
    /// </summary>
    public const string ReplaceArg = "--replace";

    private const string OldSuffix = ".old";
    private static readonly TimeSpan PreviousExitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MutexTimeout = TimeSpan.FromSeconds(10);

    public static void Launch(PendingUpdate pending, InstallKind kind, string exePath,
                              IReadOnlyList<string> relaunchArgs, bool relaunch, Action releaseMutex)
    {
        switch (kind)
        {
            case InstallKind.Installer:
                LaunchInstaller(pending.FilePath, relaunchArgs, relaunch, releaseMutex);
                break;
            case InstallKind.Portable:
                LaunchReplacer(pending.FilePath, exePath, relaunchArgs, relaunch, releaseMutex);
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
    /// Exe avulso: quem troca o arquivo é o exe NOVO, rodando da pasta de updates, depois
    /// que este processo tiver saído de vez. Trocar daqui não é seguro: no single-file o
    /// runtime abre o bundle PELO CAMINHO a cada assembly carregado pela primeira vez, e
    /// depois da troca leria o exe novo com os offsets do antigo — BadImageFormatException
    /// em qualquer ponto do encerramento, com o mutex ainda preso.
    /// </summary>
    private static void LaunchReplacer(string newExePath, string exePath, IReadOnlyList<string> relaunchArgs,
                                       bool relaunch, Action releaseMutex)
    {
        var start = new ProcessStartInfo(newExePath) { UseShellExecute = false };
        start.ArgumentList.Add(ReplaceArg);
        start.ArgumentList.Add(exePath);
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(relaunch ? "1" : "0");
        foreach (var arg in relaunchArgs)
            start.ArgumentList.Add(arg);

        Process.Start(start);
        releaseMutex();
    }

    /// <summary>Se este processo foi lançado para trocar o exe avulso (ver <see cref="ReplaceArg"/>).</summary>
    public static bool IsReplaceRequest(IReadOnlyList<string> args) =>
        args.Count >= 4 && string.Equals(args[0], ReplaceArg, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lado do exe novo no pedido de troca: espera a versão anterior terminar, troca o
    /// arquivo e reabre pelo caminho de sempre (o EXE.xml continua certo). Processo sem
    /// janela: nunca lança nem mostra nada. Se algo falhar, o exe anterior fica (ou
    /// volta) no lugar e a próxima abertura tenta de novo, dentro do limite de tentativas.
    /// </summary>
    public static void RunReplace(IReadOnlyList<string> args, Func<TimeSpan, bool> acquireMutex, Action releaseMutex)
    {
        try
        {
            var exePath = args[1];
            var relaunch = args[3] == "1";
            var newExePath = Environment.ProcessPath;
            if (newExePath is null
                || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
                return;

            // O mutex a anterior solta antes de terminar; o arquivo dela só fica fora de
            // uso quando o processo acaba.
            if (!WaitForExit(pid, PreviousExitTimeout))
                return;
            // Com o mutex ninguém abre o conector no meio da troca. Se outra instância
            // subiu nesse intervalo, ela fica como está.
            if (!acquireMutex(MutexTimeout))
                return;

            try
            {
                Swap(newExePath, exePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // O exe anterior continua no lugar: reabre ele mesmo.
            }

            if (relaunch)
            {
                var start = new ProcessStartInfo(exePath)
                {
                    UseShellExecute = false,
                    // Não herdar a pasta de updates como diretório atual: presa, ela não
                    // seria apagada quando a pendência for limpa.
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
                };
                foreach (var arg in args.Skip(4))
                    start.ArgumentList.Add(arg);
                start.ArgumentList.Add(UpdatedArg);
                Process.Start(start);
            }
        }
        catch (Exception)
        {
            // Melhor esforço, sem janela para avisar.
        }
        finally
        {
            releaseMutex();
        }
    }

    /// <summary>O Windows deixa renomear um exe, só não sobrescrever enquanto roda.</summary>
    private static void Swap(string newExePath, string exePath)
    {
        var oldPath = exePath + OldSuffix;
        TryDelete(oldPath);                     // resto de uma troca anterior
        File.Move(exePath, oldPath);
        try
        {
            // Cópia, não move: o exe novo é este processo, rodando da pasta de updates.
            File.Copy(newExePath, exePath, overwrite: false);
        }
        catch
        {
            File.Move(oldPath, exePath);        // desfaz: fica a versão anterior
            throw;
        }
    }

    private static bool WaitForExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.WaitForExit(timeout);
        }
        catch (ArgumentException)
        {
            return true;                        // já tinha saído
        }
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
