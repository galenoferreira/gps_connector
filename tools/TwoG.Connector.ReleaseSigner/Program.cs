using System.Security.Cryptography;
using System.Text;
using TwoG.Connector.Core;

// Gera e assina o update.json de um release.
//
//   dotnet run --project tools/TwoG.Connector.ReleaseSigner/TwoG.Connector.ReleaseSigner.csproj -- <pasta dist> <versão> <arquivo>...
//
// A chave privada vem de UPDATE_SIGNING_KEY (conteúdo PEM, no CI) ou de
// UPDATE_SIGNING_KEY_PATH (arquivo, uso local). Ela nunca é impressa — nem em
// mensagem de erro, que mostra só o tipo da exceção.

if (args.Length < 3)
{
    Console.Error.WriteLine("uso: <pasta dist> <versão> <arquivo> [arquivo...]");
    return 2;
}

var dist = args[0];
if (!AppVersion.TryParse(args[1], out var version))
{
    Console.Error.WriteLine($"versão inválida: {args[1]}");
    return 2;
}
// O app monta a URL do binário a partir da versão do manifesto (releases/download/v{versão}),
// então ela tem de ser idêntica à da tag. TryParse aceita "v", espaços e "+build" e os
// descarta; aceitar "1.4.0+1" aqui assinaria "1.4.0" e todo app baixaria um 404.
if (!string.Equals(version.ToString(), args[1], StringComparison.Ordinal))
{
    Console.Error.WriteLine($"versão não canônica: {args[1]} (esperado {version})");
    return 2;
}

var keyPem = Environment.GetEnvironmentVariable("UPDATE_SIGNING_KEY");
if (string.IsNullOrWhiteSpace(keyPem))
{
    var keyPath = Environment.GetEnvironmentVariable("UPDATE_SIGNING_KEY_PATH");
    if (!string.IsNullOrWhiteSpace(keyPath) && File.Exists(keyPath))
        keyPem = File.ReadAllText(keyPath);
}
if (string.IsNullOrWhiteSpace(keyPem))
{
    Console.Error.WriteLine("chave de assinatura ausente: defina UPDATE_SIGNING_KEY ou UPDATE_SIGNING_KEY_PATH");
    return 1;
}

var files = new Dictionary<string, UpdateFile>(StringComparer.OrdinalIgnoreCase);
foreach (var name in args.Skip(2))
{
    var path = Path.Combine(dist, name);
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"arquivo não encontrado: {path}");
        return 1;
    }
    files[name] = UpdateFile.FromFile(path);
}

var manifest = UpdateManifest.Serialize(version, files);

string signature;
try
{
    signature = UpdateSignature.Sign(manifest, keyPem);
}
catch (Exception ex) when (ex is ArgumentException or CryptographicException)
{
    Console.Error.WriteLine($"chave de assinatura ilegível ({ex.GetType().Name})");
    return 1;
}

// Confere com as chaves embutidas no app ANTES de publicar: um secret que não
// bate com elas geraria um release que nenhum app instalado aceita.
if (!UpdateSignature.Verify(manifest, signature, UpdateKeys.Accepted))
{
    Console.Error.WriteLine("a assinatura não confere com nenhuma chave pública embutida no app");
    return 1;
}

File.WriteAllBytes(Path.Combine(dist, "update.json"), manifest);
File.WriteAllText(Path.Combine(dist, "update.json.sig"), signature);
Console.WriteLine(Encoding.UTF8.GetString(manifest));
return 0;
