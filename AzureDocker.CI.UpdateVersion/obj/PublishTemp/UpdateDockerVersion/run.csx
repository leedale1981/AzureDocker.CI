#r "Microsoft.Azure.Management.WebSites"
#r "Microsoft.Azure.Management.WebSites.Models"
#r "Microsoft.IdentityModel.Clients.ActiveDirectory"
#r "Microsoft.Rest"
#r "Microsoft.Azure.Management.ResourceManager"
#r "Microsoft.Azure.Common.Authentication.Models"

using Microsoft.Azure.Common.Authentication.Models;
using Microsoft.Azure.Management.ResourceManager;
using Microsoft.Azure.Management.WebSites;
using Microsoft.Azure.Management.WebSites.Models;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    log.Info($"C# HTTP trigger function processed a request. RequestUri={req.RequestUri}");

    // parse query parameter
    // Get request body
    log.WriteLine(data.ToString());
    dynamic data = await req.Content.ReadAsAsync<object>();
    string version = data;

    string resource = req.GetQueryNameValuePairs()
        .FirstOrDefault(q => string.Compare(q.Key, "resource", true) == 0)
        .Value;

    string appName = req.GetQueryNameValuePairs()
        .FirstOrDefault(q => string.Compare(q.Key, "app", true) == 0)
        .Value;

    await UpdateAzureAppDockerVersion(resource, appName, version);

    return name == null
        ? req.CreateResponse(HttpStatusCode.BadRequest, "Please pass a name on the query string or in the request body")
        : req.CreateResponse(HttpStatusCode.OK, "");
}

private static async Task<string> GetAuthorizationHeader()
{
    AuthenticationResult result = null;

    var context = new AuthenticationContext(string.Format(
      ConfigurationManager.AppSettings["Login"],
      ConfigurationManager.AppSettings["TenantId"]));

    ClientCredential credentials = new ClientCredential(
        ConfigurationManager.AppSettings["ClientId"],
        ConfigurationManager.AppSettings["ClientSecret"]);

    await Task.Factory.StartNew(() =>
    {
        result = context.AcquireTokenAsync(
         ConfigurationManager.AppSettings["ApiEndpoint"], credentials).Result;
    });

    if (result == null)
    {
        throw new InvalidOperationException("Failed to obtain the JWT token");
    }

    string token = result.AccessToken;
    return token;
}

public static async void UpdateAzureAppDockerVersion(string resource, string appName, string version, TraceWriter logger)
{
    var token = GetAuthorizationHeader();
    var cloudCredential = new Microsoft.Azure.TokenCloudCredentials(
      ConfigurationManager.AppSettings["SubscriptionId"], token.Result);
    var tokenCredential = new TokenCredentials(cloudCredential.Token);

    await UpdateCloudService(tokenCredential, cloudCredential);
}

public async static Task<string> UpdateCloudService(
            ServiceClientCredentials clientCredentials,
            Microsoft.Azure.TokenCloudCredentials cloudCredentials,
            TraceWriter logger,
            string resource,
            string appName,
            string version)
{
    var environment = AzureEnvironment.PublicEnvironments[EnvironmentName.AzureCloud];
    var loggingHandler = new LoggingHandler(new HttpClientHandler(), logger);

    using (var httpClient = new HttpClient(loggingHandler))
    {
        var resourceGroupClient = new ResourceManagementClient(environment.GetEndpointAsUri(AzureEnvironment.Endpoint.ResourceManager), clientCredentials, loggingHandler);
        resourceGroupClient.SubscriptionId = cloudCredentials.SubscriptionId;
        var websiteClient = new WebSiteManagementClient(environment.GetEndpointAsUri(AzureEnvironment.Endpoint.ResourceManager), clientCredentials, loggingHandler);
        websiteClient.SubscriptionId = cloudCredentials.SubscriptionId;

        var currentAppSettings = await websiteClient.WebApps.ListApplicationSettingsAsync(
                    ConfigurationManager.AppSettings["resourceGroup"],
                    ConfigurationManager.AppSettings["appName"]);

        var appSettings = new StringDictionary
        {
            Location = currentAppSettings.Location,
            Kind = currentAppSettings.Kind,
            Name = currentAppSettings.Name,
            Tags = currentAppSettings.Tags,
            Type = currentAppSettings.Type
        };

        string imageNameKey = "DOCKER_CUSTOM_IMAGE_NAME";

        appSettings.Properties = new Dictionary<string, string>();
        appSettings.Properties.Add(
            new System.Collections.Generic.KeyValuePair<string, string>(imageNameKey, "leedale/dronefly:" + version));

        foreach (var setting in currentAppSettings.Properties)
        {
            if (setting.Key != imageNameKey)
            {
                appSettings.Properties.Add(
                    new System.Collections.Generic.KeyValuePair<string, string>(setting.Key, setting.Value));
            }
        }

        await websiteClient.WebApps.UpdateApplicationSettingsAsync(
            ConfigurationManager.AppSettings["resourceGroup"],
            ConfigurationManager.AppSettings["appName"],
            appSettings);
    }

    return "Successfully updated service.";
}
   

public class LoggingHandler : DelegatingHandler
{
    private TraceWriter traceWriter;

    public LoggingHandler(HttpMessageHandler innerHandler, TraceWriter traceWriter)
        : base(innerHandler)
    {
        this.traceWriter = traceWriter;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        traceWriter.WriteLine("*********************");
        traceWriter.Write("Request: ");
        traceWriter.WriteLine("{0} {1}", request.Method, request.RequestUri);
        if (request.Content != null)
        {
            traceWriter.WriteLine(await request.Content.ReadAsStringAsync());
        }
        traceWriter.WriteLine();

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

        traceWriter.Write("Response: ");
        traceWriter.WriteLine("{0} {1}", (int)response.StatusCode, response.ReasonPhrase);
        if (response.Content != null)
        {
            traceWriter.WriteLine(await response.Content.ReadAsStringAsync());
        }
        traceWriter.WriteLine();

        return response;
    }
}