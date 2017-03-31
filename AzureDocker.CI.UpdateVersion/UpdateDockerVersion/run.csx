#r "bin/Microsoft.Azure.Management.WebSites.dll"
#r "bin/Microsoft.IdentityModel.Clients.ActiveDirectory.dll"
#r "bin/Microsoft.Rest.ClientRuntime.dll"
#r "bin/Microsoft.Rest.ClientRuntime.Azure.dll"
#r "bin/Microsoft.Rest.ClientRuntime.Azure.Authentication.dll"
#r "bin/Microsoft.Azure.Management.ResourceManager.dll"
#r "bin/Microsoft.Azure.Common.dll"
#r "bin/Microsoft.Azure.Common.NetFramework.dll"
#r "bin/Microsoft.Azure.Common.Authentication.dll"
#r "bin/System.Net.Http.Extensions.dll"
#r "bin/System.Net.Http.Primitives.dll"

using Microsoft.Azure.Common.Authentication.Models;
using Microsoft.Azure.Management.ResourceManager;
using Microsoft.Azure.Management.WebSites;
using Microsoft.Azure.Management.WebSites.Models;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;
using System;
using System.Net;
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
    dynamic data = await req.Content.ReadAsAsync<object>();
    string[] repoNameArray = data.repository.repo_name.ToString().Split(':');
    string version = repoNameArray[repoNameArray.Length - 1];
    log.Info("Version: " + version);

    string resource = req.GetQueryNameValuePairs()
        .FirstOrDefault(q => string.Compare(q.Key, "resource", true) == 0)
        .Value;

    string appName = req.GetQueryNameValuePairs()
        .FirstOrDefault(q => string.Compare(q.Key, "app", true) == 0)
        .Value;

    await UpdateAzureAppDockerVersion(resource, appName, version, log);

    return req.CreateResponse(HttpStatusCode.OK, "");
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

public static async Task<string> UpdateAzureAppDockerVersion(string resource, string appName, string version, TraceWriter logger)
{
    var token = GetAuthorizationHeader();
    var cloudCredential = new Microsoft.Azure.TokenCloudCredentials(
      ConfigurationManager.AppSettings["SubscriptionId"], token.Result);
    var tokenCredential = new TokenCredentials(cloudCredential.Token);

    await UpdateCloudService(tokenCredential, cloudCredential, logger, resource, appName, version);

    return "Done";
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
                    resource, appName);

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
            resource, appName,
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
        traceWriter.Info("*********************");
        traceWriter.Info("Request: ");
        traceWriter.Info(string.Format("{0} {1}", request.Method, request.RequestUri));
        if (request.Content != null)
        {
            traceWriter.Info(await request.Content.ReadAsStringAsync());
        }

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

        traceWriter.Info("Response: ");
        traceWriter.Info(string.Format("{0} {1}", (int)response.StatusCode, response.ReasonPhrase));
        if (response.Content != null)
        {
            traceWriter.Info(await response.Content.ReadAsStringAsync());
        }

        return response;
    }
}