using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace AFSDataGateway.Services.IvtAdmin
{
    public interface IAzureIvt
    {
        // Lists every missing setting; empty when the Azure path can be attempted.
        IReadOnlyList<string> ConfigurationProblems();
        Task<IReadOnlyList<string>> UploadFolderAsync(string localDirectory, string blobPrefix, CancellationToken cancellation);
        Task<string> StartPipelineAsync(CancellationToken cancellation);
        Task<string> PipelineRunStatusAsync(string runId, CancellationToken cancellation);
    }

    // Azure Blob + Data Factory over REST. Credentials: client secret from an environment variable, or the
    // managed identity of the host. No keys are stored in configuration files or source.
    public sealed class AzureIvtClient : IAzureIvt
    {
        private const string StorageScope = "https://storage.azure.com/.default";
        private const string ManagementScope = "https://management.azure.com/.default";
        private readonly IvtAdminSettings.AzureSettings azure;
        public AzureIvtClient(IvtAdminSettings settings) { azure = settings.Azure; }

        public IReadOnlyList<string> ConfigurationProblems()
        {
            var problems = new List<string>();
            if (!azure.Enabled) problems.Add("azure.enabled is false");
            foreach (var pair in new Dictionary<string, string> {
                { "azure.storageAccountUrl", azure.StorageAccountUrl }, { "azure.container", azure.Container },
                { "azure.tenantId", azure.TenantId }, { "azure.subscriptionId", azure.SubscriptionId },
                { "azure.resourceGroup", azure.ResourceGroup }, { "azure.factory", azure.Factory }, { "azure.pipeline", azure.Pipeline } })
                if (string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Contains("<")) problems.Add(pair.Key + " is not set");
            if (!string.IsNullOrWhiteSpace(azure.ClientSecretEnvironmentVariable))
            {
                if (string.IsNullOrWhiteSpace(azure.ClientId)) problems.Add("azure.clientId is not set");
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(azure.ClientSecretEnvironmentVariable)))
                    problems.Add("environment variable " + azure.ClientSecretEnvironmentVariable + " is not set on the web server");
            }
            return problems;
        }

        private async Task<string> TokenAsync(HttpClient http, string scope, CancellationToken cancellation)
        {
            HttpResponseMessage response;
            if (!string.IsNullOrWhiteSpace(azure.ClientSecretEnvironmentVariable))
            {
                var form = new FormUrlEncodedContent(new Dictionary<string, string> {
                    { "grant_type", "client_credentials" }, { "client_id", azure.ClientId },
                    { "client_secret", Environment.GetEnvironmentVariable(azure.ClientSecretEnvironmentVariable) }, { "scope", scope } });
                response = await http.PostAsync("https://login.microsoftonline.com/" + Uri.EscapeDataString(azure.TenantId) + "/oauth2/v2.0/token", form, cancellation).ConfigureAwait(false);
            }
            else
            {
                var resource = scope.Replace("/.default", "/");
                var request = new HttpRequestMessage(HttpMethod.Get, "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=" + Uri.EscapeDataString(resource) +
                    (string.IsNullOrWhiteSpace(azure.ClientId) ? "" : "&client_id=" + Uri.EscapeDataString(azure.ClientId)));
                request.Headers.Add("Metadata", "true");
                response = await http.SendAsync(request, cancellation).ConfigureAwait(false);
            }
            using (response)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) throw new IvtOperationException("Azure sign-in failed (HTTP " + (int)response.StatusCode + "). Check the Azure identity configuration.");
                var token = (string)JObject.Parse(body)["access_token"];
                if (string.IsNullOrEmpty(token)) throw new IvtOperationException("Azure sign-in returned no access token.");
                return token;
            }
        }

        public async Task<IReadOnlyList<string>> UploadFolderAsync(string localDirectory, string blobPrefix, CancellationToken cancellation)
        {
            var uploaded = new List<string>();
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            {
                var token = await TokenAsync(http, StorageScope, cancellation).ConfigureAwait(false);
                // Top-level files only, overwriting blobs with the same name (legacy behaviour).
                foreach (var path in Directory.GetFiles(localDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    var blob = blobPrefix + Path.GetFileName(path);
                    var url = azure.StorageAccountUrl.TrimEnd('/') + "/" + Uri.EscapeDataString(azure.Container) + "/" +
                              string.Join("/", blob.Split('/').Select(Uri.EscapeDataString));
                    using (var stream = File.OpenRead(path))
                    using (var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = new StreamContent(stream) })
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        request.Headers.Add("x-ms-version", "2021-08-06");
                        request.Headers.Add("x-ms-blob-type", "BlockBlob");
                        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                        using (var response = await http.SendAsync(request, cancellation).ConfigureAwait(false))
                            if (!response.IsSuccessStatusCode)
                                throw new IvtOperationException("Upload of " + blob + " failed (HTTP " + (int)response.StatusCode + "). Uploaded before the failure: " + uploaded.Count + ".");
                    }
                    uploaded.Add(blob);
                }
            }
            return uploaded;
        }

        private string FactoryUrl()
        {
            return "https://management.azure.com/subscriptions/" + Uri.EscapeDataString(azure.SubscriptionId) + "/resourceGroups/" + Uri.EscapeDataString(azure.ResourceGroup) +
                   "/providers/Microsoft.DataFactory/factories/" + Uri.EscapeDataString(azure.Factory);
        }

        public async Task<string> StartPipelineAsync(CancellationToken cancellation)
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
            {
                var token = await TokenAsync(http, ManagementScope, cancellation).ConfigureAwait(false);
                using (var request = new HttpRequestMessage(HttpMethod.Post, FactoryUrl() + "/pipelines/" + Uri.EscapeDataString(azure.Pipeline) + "/createRun?api-version=2018-06-01")
                    { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") })
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    using (var response = await http.SendAsync(request, cancellation).ConfigureAwait(false))
                    {
                        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode) throw new IvtOperationException("Data Factory refused the pipeline run (HTTP " + (int)response.StatusCode + ").");
                        var runId = (string)JObject.Parse(body)["runId"];
                        if (string.IsNullOrEmpty(runId)) throw new IvtOperationException("Data Factory accepted the request but returned no run id.");
                        return runId;
                    }
                }
            }
        }

        public async Task<string> PipelineRunStatusAsync(string runId, CancellationToken cancellation)
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(1) })
            {
                var token = await TokenAsync(http, ManagementScope, cancellation).ConfigureAwait(false);
                using (var request = new HttpRequestMessage(HttpMethod.Get, FactoryUrl() + "/pipelineruns/" + Uri.EscapeDataString(runId) + "?api-version=2018-06-01"))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    using (var response = await http.SendAsync(request, cancellation).ConfigureAwait(false))
                    {
                        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode) throw new IvtOperationException("Pipeline run lookup failed (HTTP " + (int)response.StatusCode + ").");
                        var json = JObject.Parse(body);
                        var message = (string)json["message"];
                        return (string)json["status"] + (string.IsNullOrEmpty(message) ? "" : ": " + message);
                    }
                }
            }
        }
    }
}
