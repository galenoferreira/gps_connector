namespace TwoG.Connector.Core.Tests;

/// <summary>Pasta temporária exclusiva por teste, apagada no Dispose.</summary>
internal sealed class TestTempDir : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "2gconn-" + Guid.NewGuid().ToString("N"));

    public TestTempDir() => Directory.CreateDirectory(Path);

    public string Sub(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Arquivo preso no Windows: sobra na pasta temporária, não quebra o teste.
        }
    }
}
