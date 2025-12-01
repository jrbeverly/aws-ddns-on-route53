using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.Route53;
using Amazon.Route53.Model;
using Newtonsoft.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace DynamicDnsService
{
    public class Function
    {
        private static readonly string TableName = Environment.GetEnvironmentVariable("DDB_TABLE");
        private static readonly string DefaultHostedZoneId = Environment.GetEnvironmentVariable("HOSTED_ZONE_ID");
        private static readonly AmazonDynamoDBClient DdbClient = new AmazonDynamoDBClient();
        private static readonly ITable DefaultDdbTable =
            Amazon.DynamoDBv2.DocumentModel.Table.LoadTable(DdbClient, TableName);
        private static readonly IAmazonRoute53 DefaultRoute53Client = new AmazonRoute53Client();

        private readonly ITable _ddbTable;
        private readonly IAmazonRoute53 _route53;
        private readonly string _hostedZoneId;

        public Function() : this(DefaultDdbTable, DefaultRoute53Client, DefaultHostedZoneId) { }

        public Function(ITable ddbTable, IAmazonRoute53 route53, string hostedZoneId)
        {
            _ddbTable = ddbTable;
            _route53 = route53;
            _hostedZoneId = hostedZoneId;
        }

        public APIGatewayProxyResponse FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            var proxy = request.PathParameters?["proxy"] ?? "";
            var parts = proxy.Split('/', StringSplitOptions.RemoveEmptyEntries);

            context.Logger.LogLine($"Received {request.HttpMethod} for {request.Path}");
            if (request.HttpMethod == "GET" && parts.Length == 2 && parts[0] == "hostname")
            {
                return GetRecord(parts[1]);
            }

            if (request.HttpMethod == "POST" && parts.Length == 1 && parts[0] == "hostname")
            {
                return UpdateRecord(request.Body);
            }

            return new APIGatewayProxyResponse
            {
                StatusCode = 404,
                Body = "Not Found"
            };
        }

        private APIGatewayProxyResponse GetRecord(string hostname)
        {
            var doc = _ddbTable.GetItemAsync(hostname).Result;
            if (doc == null)
                return new APIGatewayProxyResponse { StatusCode = (int)HttpStatusCode.NotFound };

            var body = JsonConvert.SerializeObject(new { hostname, ip = doc["IP"].AsString() });
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = body,
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }

        private APIGatewayProxyResponse UpdateRecord(string body)
        {
            dynamic data = JsonConvert.DeserializeObject(body);
            string hostname = data.hostname;
            string ip = data.ip;

            var doc = new Document
            {
                ["Hostname"] = hostname,
                ["IP"] = ip,
                ["LastUpdated"] = DateTime.UtcNow.ToString("o")
            };
            _ddbTable.PutItemAsync(doc).Wait();

            var currentIp = GetCurrentARecord(hostname);
            if (currentIp == ip)
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.OK,
                    Body = JsonConvert.SerializeObject(new { status = "current" }),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                };
            }

            _route53.ChangeResourceRecordSetsAsync(new ChangeResourceRecordSetsRequest
            {
                HostedZoneId = _hostedZoneId,
                ChangeBatch = new ChangeBatch
                {
                    Changes = new List<Change>
                    {
                        new Change
                        {
                            Action = ChangeAction.UPSERT,
                            ResourceRecordSet = new ResourceRecordSet
                            {
                                Name = hostname,
                                Type = RRType.A,
                                TTL = 60,
                                ResourceRecords = new List<ResourceRecord>
                                {
                                    new ResourceRecord { Value = ip }
                                }
                            }
                        }
                    }
                }
            }).Wait();

            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = JsonConvert.SerializeObject(new { status = "updated" }),
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }

        private string? GetCurrentARecord(string hostname)
        {
            var response = _route53.ListResourceRecordSetsAsync(new ListResourceRecordSetsRequest
            {
                HostedZoneId = _hostedZoneId,
                StartRecordName = hostname,
                StartRecordType = RRType.A,
                MaxItems = "1"
            }).Result;

            var record = response.ResourceRecordSets
                .FirstOrDefault(r =>
                    r.Type == RRType.A &&
                    r.Name.TrimEnd('.').Equals(hostname.TrimEnd('.'), StringComparison.OrdinalIgnoreCase));

            return record?.ResourceRecords.FirstOrDefault()?.Value;
        }
    }
}
