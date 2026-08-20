using System.Net.Http;
using TwoG.GpsClient.Core;

namespace TwoG.GpsClient.Core.Tests;

/// <summary>
/// Testes de integração reais: sobem o servidor numa porta efêmera e falam HTTP com
/// ele. É o que prova que o EFB conseguirá buscar o plano.
/// </summary>
public class FlightPlanServerTests
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static FlightPlanServer StartOnFreePort()
    {
        var server = new FlightPlanServer();
        server.Start(0);   // 0 = o sistema escolhe uma porta livre
        Assert.True(server.IsRunning, server.LastError ?? "servidor não subiu");
        return server;
    }

    private static string Url(FlightPlanServer server, string path = FlightPlanServer.Path) =>
        $"http://127.0.0.1:{server.Port}{path}";

    [Fact]
    public async Task ServesPublishedPlanAsJson()
    {
        using var server = StartOnFreePort();
        var plan = new FlightPlan("SBSP", "SBRJ", 12000,
        [
            new FlightPlanWaypoint("SBSP", WaypointKind.Airport, -23.626667, -46.656111, 2630),
            new FlightPlanWaypoint("SBRJ", WaypointKind.Airport, -22.910278, -43.163056, null),
        ]);
        server.Publish(FlightPlanJson.Serialize(plan, "MSFS 2024", DateTime.UtcNow));

        var response = await Http.GetAsync(Url(server));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"departure\":\"SBSP\"", body);
        Assert.Contains("\"schemaVersion\":1", body);
    }

    [Fact]
    public async Task WithoutPublishedPlanReturns404()
    {
        using var server = StartOnFreePort();
        var response = await Http.GetAsync(Url(server));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnknownPathReturns404()
    {
        using var server = StartOnFreePort();
        server.Publish("{}");
        var response = await Http.GetAsync(Url(server, "/outra-coisa"));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NonGetMethodIsRejected()
    {
        using var server = StartOnFreePort();
        server.Publish("{}");
        var response = await Http.PostAsync(Url(server), new StringContent("x"));
        Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task RepublishReplacesTheServedPlan()
    {
        using var server = StartOnFreePort();
        server.Publish("""{"schemaVersion":1,"departure":"AAAA"}""");
        server.Publish("""{"schemaVersion":1,"departure":"BBBB"}""");

        var body = await Http.GetStringAsync(Url(server));
        Assert.Contains("BBBB", body);
        Assert.DoesNotContain("AAAA", body);
    }

    [Fact]
    public async Task ServesSeveralRequestsInSequence()
    {
        // Cada resposta fecha a conexão; o servidor precisa seguir aceitando.
        using var server = StartOnFreePort();
        server.Publish("""{"ok":true}""");

        for (var i = 0; i < 5; i++)
            Assert.Contains("ok", await Http.GetStringAsync(Url(server)));
    }

    [Fact]
    public void StopThenStartAgainWorks()
    {
        var server = new FlightPlanServer();
        server.Start(0);
        var firstPort = server.Port;
        server.Stop();
        Assert.False(server.IsRunning);

        server.Start(0);
        Assert.True(server.IsRunning);
        Assert.NotEqual(0, server.Port);
        server.Dispose();
        _ = firstPort;
    }

    [Fact]
    public void PortInUseIsReportedInsteadOfThrowing()
    {
        using var first = StartOnFreePort();
        var second = new FlightPlanServer();
        second.Start(first.Port);   // porta ocupada

        // Não pode explodir: a UI mostra a mensagem e a posição segue funcionando.
        Assert.False(second.IsRunning);
        Assert.NotNull(second.LastError);
        second.Dispose();
    }
}
