using DdnsClient;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: ddns-client <hostname> <endpoint-url>");
    return 1;
}

var hostname = args[0];
var endpointUrl = args[1];

using var http = new HttpClient();

string ip;
try
{
    ip = await Reporter.GetPublicIpAsync(http);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Unable to determine public IP: {ex.Message}");
    return 2;
}

string status;
try
{
    status = await Reporter.ReportAsync(http, hostname, ip, endpointUrl);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to reach endpoint: {ex.Message}");
    return 3;
}

Console.WriteLine($"{hostname} {ip} {status}");
return 0;
