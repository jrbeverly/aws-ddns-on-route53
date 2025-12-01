using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.TestUtilities;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.Route53;
using Amazon.Route53.Model;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace DynamicDnsService.Tests;

public class FunctionTest
{
    private const string HostedZoneId = "ZONE123";
    private const string Hostname = "host.example.com";

    private static APIGatewayProxyRequest PostRequest(string hostname, string ip) =>
        new APIGatewayProxyRequest
        {
            HttpMethod = "POST",
            Path = "/hostname",
            PathParameters = new Dictionary<string, string> { { "proxy", "hostname" } },
            Body = JsonConvert.SerializeObject(new { hostname, ip })
        };

    private static ListResourceRecordSetsResponse EmptyRecordSet() =>
        new ListResourceRecordSetsResponse { ResourceRecordSets = new List<ResourceRecordSet>() };

    private static ListResourceRecordSetsResponse RecordSetWithIp(string hostname, string ip) =>
        new ListResourceRecordSetsResponse
        {
            ResourceRecordSets = new List<ResourceRecordSet>
            {
                new ResourceRecordSet
                {
                    Name = hostname + ".",
                    Type = RRType.A,
                    TTL = 60,
                    ResourceRecords = new List<ResourceRecord> { new ResourceRecord { Value = ip } }
                }
            }
        };

    private (Function function, Mock<IAmazonRoute53> route53Mock) BuildFunction(
        ListResourceRecordSetsResponse listResponse)
    {
        var tableMock = new Mock<ITable>();
        tableMock
            .Setup(t => t.PutItemAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Document());

        var route53Mock = new Mock<IAmazonRoute53>();
        route53Mock
            .Setup(r => r.ListResourceRecordSetsAsync(It.IsAny<ListResourceRecordSetsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(listResponse);
        route53Mock
            .Setup(r => r.ChangeResourceRecordSetsAsync(It.IsAny<ChangeResourceRecordSetsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChangeResourceRecordSetsResponse
            {
                ChangeInfo = new ChangeInfo { Id = "test", Status = ChangeStatus.PENDING }
            });

        var function = new Function(tableMock.Object, route53Mock.Object, HostedZoneId);
        return (function, route53Mock);
    }

    [Fact]
    public void Post_WhenIpMatchesCurrentRecord_ReturnsCurrentStatus()
    {
        var (function, route53Mock) = BuildFunction(RecordSetWithIp(Hostname, "1.2.3.4"));
        var context = new TestLambdaContext();

        var response = function.FunctionHandler(PostRequest(Hostname, "1.2.3.4"), context);

        Assert.Equal(200, response.StatusCode);
        var body = JsonConvert.DeserializeAnonymousType(response.Body, new { status = "" });
        Assert.Equal("current", body!.status);
        route53Mock.Verify(r => r.ChangeResourceRecordSetsAsync(
            It.IsAny<ChangeResourceRecordSetsRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Post_WhenIpDiffersFromCurrentRecord_CallsChangeAndReturnsUpdatedStatus()
    {
        var (function, route53Mock) = BuildFunction(RecordSetWithIp(Hostname, "1.2.3.4"));
        var context = new TestLambdaContext();

        var response = function.FunctionHandler(PostRequest(Hostname, "5.6.7.8"), context);

        Assert.Equal(200, response.StatusCode);
        var body = JsonConvert.DeserializeAnonymousType(response.Body, new { status = "" });
        Assert.Equal("updated", body!.status);
        route53Mock.Verify(r => r.ChangeResourceRecordSetsAsync(
            It.Is<ChangeResourceRecordSetsRequest>(req =>
                req.ChangeBatch.Changes[0].ResourceRecordSet.ResourceRecords[0].Value == "5.6.7.8" &&
                req.ChangeBatch.Changes[0].ResourceRecordSet.TTL == 60),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Post_WhenNoExistingRecord_CreatesRecordWithTtl60()
    {
        var (function, route53Mock) = BuildFunction(EmptyRecordSet());
        var context = new TestLambdaContext();

        var response = function.FunctionHandler(PostRequest(Hostname, "1.2.3.4"), context);

        Assert.Equal(200, response.StatusCode);
        var body = JsonConvert.DeserializeAnonymousType(response.Body, new { status = "" });
        Assert.Equal("updated", body!.status);
        route53Mock.Verify(r => r.ChangeResourceRecordSetsAsync(
            It.Is<ChangeResourceRecordSetsRequest>(req =>
                req.HostedZoneId == HostedZoneId &&
                req.ChangeBatch.Changes[0].ResourceRecordSet.TTL == 60 &&
                req.ChangeBatch.Changes[0].ResourceRecordSet.ResourceRecords[0].Value == "1.2.3.4"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Post_WhenRoute53ChangeThrows_DynamoDbWriteOccursBeforeFailure()
    {
        Document? captured = null;
        var tableMock = new Mock<ITable>();
        tableMock
            .Setup(t => t.PutItemAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Callback<Document, CancellationToken>((doc, _) => captured = doc)
            .ReturnsAsync(new Document());

        var route53Mock = new Mock<IAmazonRoute53>();
        route53Mock
            .Setup(r => r.ListResourceRecordSetsAsync(It.IsAny<ListResourceRecordSetsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyRecordSet());
        route53Mock
            .Setup(r => r.ChangeResourceRecordSetsAsync(It.IsAny<ChangeResourceRecordSetsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonRoute53Exception("Transient failure"));

        var function = new Function(tableMock.Object, route53Mock.Object, HostedZoneId);
        var context = new TestLambdaContext();

        Assert.ThrowsAny<Exception>(() => function.FunctionHandler(PostRequest(Hostname, "9.9.9.9"), context));
        Assert.NotNull(captured);
        Assert.Equal("9.9.9.9", captured!["IP"].AsString());
        Assert.Equal(Hostname, captured!["Hostname"].AsString());
    }

    [Fact]
    public void TwoConsecutivePosts_WithDifferentIps_CallsRoute53Twice()
    {
        var tableMock = new Mock<ITable>();
        tableMock
            .Setup(t => t.PutItemAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Document());

        var route53Mock = new Mock<IAmazonRoute53>();
        route53Mock
            .SetupSequence(r => r.ListResourceRecordSetsAsync(It.IsAny<ListResourceRecordSetsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyRecordSet())
            .ReturnsAsync(RecordSetWithIp(Hostname, "1.2.3.4"));
        route53Mock
            .Setup(r => r.ChangeResourceRecordSetsAsync(It.IsAny<ChangeResourceRecordSetsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChangeResourceRecordSetsResponse
            {
                ChangeInfo = new ChangeInfo { Id = "test", Status = ChangeStatus.PENDING }
            });

        var function = new Function(tableMock.Object, route53Mock.Object, HostedZoneId);
        var context = new TestLambdaContext();

        function.FunctionHandler(PostRequest(Hostname, "1.2.3.4"), context);
        function.FunctionHandler(PostRequest(Hostname, "5.6.7.8"), context);

        route53Mock.Verify(r => r.ChangeResourceRecordSetsAsync(
            It.IsAny<ChangeResourceRecordSetsRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void TwoConsecutivePosts_WithSameIp_CallsRoute53OnlyOnce()
    {
        var tableMock = new Mock<ITable>();
        tableMock
            .Setup(t => t.PutItemAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Document());

        var route53Mock = new Mock<IAmazonRoute53>();
        route53Mock
            .SetupSequence(r => r.ListResourceRecordSetsAsync(It.IsAny<ListResourceRecordSetsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyRecordSet())
            .ReturnsAsync(RecordSetWithIp(Hostname, "1.2.3.4"));
        route53Mock
            .Setup(r => r.ChangeResourceRecordSetsAsync(It.IsAny<ChangeResourceRecordSetsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChangeResourceRecordSetsResponse
            {
                ChangeInfo = new ChangeInfo { Id = "test", Status = ChangeStatus.PENDING }
            });

        var function = new Function(tableMock.Object, route53Mock.Object, HostedZoneId);
        var context = new TestLambdaContext();

        function.FunctionHandler(PostRequest(Hostname, "1.2.3.4"), context);
        function.FunctionHandler(PostRequest(Hostname, "1.2.3.4"), context);

        route53Mock.Verify(r => r.ChangeResourceRecordSetsAsync(
            It.IsAny<ChangeResourceRecordSetsRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
