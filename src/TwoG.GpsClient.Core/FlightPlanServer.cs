using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TwoG.GpsClient.Core;

/// <summary>
/// Serve o último plano de voo sincronizado em HTTP, para o EFB buscar.
///
/// Por que TCP e não UDP: uma rota real passa dos 1472 bytes de um datagrama, e UDP
/// não garante entrega. Por que TcpListener e não HttpListener: escutar em todas as
/// interfaces com HttpListener exige reserva de URL ou privilégio de administrador no
/// Windows, e o app é instalado por usuário — TcpListener não tem essa restrição.
/// O HTTP falado aqui é o mínimo: uma resposta a GET, sem keep-alive.
/// </summary>
public sealed class FlightPlanServer : IDisposable
{
    public const string Path = "/flightplan";

    private readonly object _lock = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private string _payload = "";

    public int Port { get; private set; }

    /// <summary>True enquanto o servidor está aceitando conexões.</summary>
    public bool IsRunning => _listener is not null;

    /// <summary>Erro que impediu o servidor de subir, ou null.</summary>
    public string? LastError { get; private set; }

    public void Start(int port)
    {
        Stop();
        try
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _listener = listener;
            _cancellation = new CancellationTokenSource();
            LastError = null;
            _ = AcceptLoopAsync(listener, _cancellation.Token);
        }
        catch (SocketException ex)
        {
            LastError = $"Porta {port} indisponível: {ex.Message}";
            _listener = null;
        }
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        try
        {
            _listener?.Stop();
        }
        catch (Exception)
        {
            // Já parado.
        }
        _listener = null;
        _cancellation?.Dispose();
        _cancellation = null;
    }

    public void Dispose() => Stop();

    /// <summary>Publica o JSON que passará a ser servido.</summary>
    public void Publish(string json)
    {
        lock (_lock)
            _payload = json;
    }

    private string CurrentPayload()
    {
        lock (_lock)
            return _payload;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token);
            }
            catch (Exception)
            {
                return;   // servidor parado ou socket morto
            }

            _ = HandleAsync(client, token);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;

                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);

                var requestLine = await reader.ReadLineAsync(token) ?? "";
                var parts = requestLine.Split(' ');
                var method = parts.Length > 0 ? parts[0] : "";
                var target = parts.Length > 1 ? parts[1] : "";

                var body = CurrentPayload();
                var (status, contentType, content) =
                    !method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                        ? ("405 Method Not Allowed", "text/plain; charset=utf-8", "Use GET.")
                    : !target.StartsWith(Path, StringComparison.OrdinalIgnoreCase)
                        ? ("404 Not Found", "text/plain; charset=utf-8", $"Use {Path}.")
                    : body.Length == 0
                        ? ("404 Not Found", "text/plain; charset=utf-8", "Nenhum plano sincronizado ainda.")
                        : ("200 OK", "application/json; charset=utf-8", body);

                var payload = Encoding.UTF8.GetBytes(content);
                var header = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {status}\r\n"
                    + $"Content-Type: {contentType}\r\n"
                    + $"Content-Length: {payload.Length}\r\n"
                    + "Access-Control-Allow-Origin: *\r\n"
                    + "Cache-Control: no-store\r\n"
                    + "Connection: close\r\n\r\n");

                await stream.WriteAsync(header, token);
                await stream.WriteAsync(payload, token);
                await stream.FlushAsync(token);
            }
            catch (Exception)
            {
                // Cliente desistiu ou rede caiu: nada a fazer.
            }
        }
    }
}
