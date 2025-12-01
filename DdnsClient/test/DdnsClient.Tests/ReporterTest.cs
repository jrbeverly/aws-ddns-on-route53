using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DdnsClient;
using Xunit;

namespace DdnsClient.Tests;

public class ReporterTest
{
    private static HttpClient FakeHttp(HttpStatusCode status, string body)
    {
        return new HttpClient(new FakeHandler(status, body));
    }

    [Fact]
    public async Task GetPublicIpAsync_TrimsWhitespace()
    {
        var http = FakeHttp(HttpStatusCode.OK, "  1.2.3.4\n");
        var ip = await Reporter.GetPublicIpAsync(http);
        Assert.Equal("1.2.3.4", ip);
    }

    [Fact]
    public async Task GetPublicIpAsync_ThrowsOnHttpError()
    {
        var http = FakeHttp(HttpStatusCode.InternalServerError, "error");
        await Assert.ThrowsAsync<HttpRequestException>(() => Reporter.GetPublicIpAsync(http));
    }

    [Fact]
    public async Task ReportAsync_ReturnsCurrent_WhenServiceSaysCurrent()
    {
        var http = FakeHttp(HttpStatusCode.OK, """{"status":"current"}""");
        var status = await Reporter.ReportAsync(http, "host.example.com", "1.2.3.4", "http://example.com/hostname");
        Assert.Equal("current", status);
    }

    [Fact]
    public async Task ReportAsync_ReturnsUpdated_WhenServiceSaysUpdated()
    {
        var http = FakeHttp(HttpStatusCode.OK, """{"status":"updated"}""");
        var status = await Reporter.ReportAsync(http, "host.example.com", "1.2.3.4", "http://example.com/hostname");
        Assert.Equal("updated", status);
    }

    [Fact]
    public async Task ReportAsync_ThrowsHttpRequestException_OnNonSuccessStatus()
    {
        var http = FakeHttp(HttpStatusCode.ServiceUnavailable, "down");
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Reporter.ReportAsync(http, "host.example.com", "1.2.3.4", "http://example.com/hostname"));
    }
}

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;

    public FakeHandler(HttpStatusCode status, string body)
    {
        _status = status;
        _body = body;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(_status)
        {
            Content = new StringContent(_body)
        });
    }
}
