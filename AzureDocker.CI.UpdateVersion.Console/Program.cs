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

namespace DroneFly.CI.UpdateVersion.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                MainAsync().Wait();
                
            }
            catch (Exception exception)
            {
                System.Console.WriteLine(exception);
            }
        }

        static async Task MainAsync()
        {
            var token = GetAuthorizationHeader();
            var cloudCredential = new Microsoft.Azure.TokenCloudCredentials(
              ConfigurationManager.AppSettings["subscriptionId"], token.Result);
            var tokenCredential = new TokenCredentials(cloudCredential.Token);

            await UpdateCloudService(tokenCredential, cloudCredential);
        }

        private static async Task<string> GetAuthorizationHeader()
        {
            AuthenticationResult result = null;

            var context = new AuthenticationContext(string.Format(
              ConfigurationManager.AppSettings["login"],
              ConfigurationManager.AppSettings["tenantId"]));

            ClientCredential credentials = new ClientCredential(
                ConfigurationManager.AppSettings["clientId"],
                ConfigurationManager.AppSettings["clientSecret"]);

            await Task.Factory.StartNew(() =>
            {
                result = context.AcquireTokenAsync(
                 ConfigurationManager.AppSettings["apiEndpoint"], credentials).Result;
            });

            if (result == null)
            {
                throw new InvalidOperationException("Failed to obtain the JWT token");
            }

            string token = result.AccessToken;
            return token;
        }

        public async static Task<string> UpdateCloudService(
            ServiceClientCredentials clientCredentials,
            Microsoft.Azure.TokenCloudCredentials cloudCredentials)
        {
            var environment = AzureEnvironment.PublicEnvironments[EnvironmentName.AzureCloud];
            var loggingHandler = new LoggingHandler(new HttpClientHandler());

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
                    new System.Collections.Generic.KeyValuePair<string, string>(imageNameKey, "leedale/dronefly:0.7.2"));

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
    }


    public class LoggingHandler : DelegatingHandler
    {
        public LoggingHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            System.Console.WriteLine("*********************");
            System.Console.Write("Request: ");
            System.Console.WriteLine("{0} {1}", request.Method, request.RequestUri);
            if (request.Content != null)
            {
                System.Console.WriteLine(await request.Content.ReadAsStringAsync());
            }
            System.Console.WriteLine();

            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

            System.Console.Write("Response: ");
            System.Console.WriteLine("{0} {1}", (int)response.StatusCode, response.ReasonPhrase);
            if (response.Content != null)
            {
                System.Console.WriteLine(await response.Content.ReadAsStringAsync());
            }
            System.Console.WriteLine();

            return response;
        }
    }
}
