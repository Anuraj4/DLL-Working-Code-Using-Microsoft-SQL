using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Edi.Generator835.Services.Interfaces;
using Newtonsoft.Json;
using Polly;
using Polly.Retry;
using Serilog;

namespace Edi.Generator835.Services
{
    public class AppsmithService : IAppsmithService
    {
        private readonly string _apiUrl;
        private readonly string _apiKey;

        // Use static HttpClient to prevent socket exhaustion
        private static readonly HttpClient _httpClient = new HttpClient();

        // Resilience pipeline for retries and timeouts
        private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;

        public AppsmithService(string apiUrl, string apiKey)
        {
            _apiUrl = apiUrl;
            _apiKey = apiKey;

            // Enterprise-Grade Resilience Policy
            _resiliencePipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
                .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
                {
                    // Handle network errors and 5xx/408/429 status codes
                    ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                        .Handle<HttpRequestException>()
                        .HandleResult(r => !r.IsSuccessStatusCode && ((int)r.StatusCode >= 500 || r.StatusCode == System.Net.HttpStatusCode.RequestTimeout || (int)r.StatusCode == 429)),
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromSeconds(2),
                    BackoffType = DelayBackoffType.Exponential,
                    OnRetry = args =>
                    {
                        Log.Warning("[Appsmith] Retry attempt {Attempt} due to: {Reason}", args.AttemptNumber, args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString());
                        return default;
                    }
                })
                .AddTimeout(TimeSpan.FromSeconds(30))
                .Build();
        }

        public async Task TriggerWorkflowAsync(object payload)
        {
            // Use specific JSON settings as requested by user
            var json = JsonConvert.SerializeObject(
                payload,
                Formatting.Indented,
                new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    NullValueHandling = NullValueHandling.Include,
                    ContractResolver = new NullToEmptyStringContractResolver(),
                });

            //Console.Write("Final payload: \n\n" + json + "\n\n=========================================");

            //File.WriteAllText(@"C:\\Users\\sonup\\OneDrive - Xalta Technology Services Pvt Ltd\\Documents\\Projects\\EDI Fabric\\Important Codes, Documents\\FInal Library Code\\EOB_TO_EDI_835\\output\data.txt", json);


            // Log trigger
            Log.Information("Triggering Appsmith workflow: {ApiUrl}", _apiUrl);
            Log.Debug("Appsmith Payload: {Payload}", json);

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            //Execute with resilience policy
            using var response = await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
            {
                // Ensure API key is present
                string requestUrl = _apiUrl;
                if (!string.IsNullOrEmpty(_apiKey) && !_apiUrl.Contains(_apiKey))
                {
                    var separator = _apiUrl.Contains("?") ? "&" : "?";
                    requestUrl = $"{_apiUrl}{separator}api-key={_apiKey}";
                }

                // Create request message inside the retry loop
                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                requestMessage.Content = new StringContent(json, Encoding.UTF8, "application/json");

                // Use cancellationToken directly
                var resp = await _httpClient.SendAsync(requestMessage, cancellationToken);

                // Log 5xx errors for debugging
                if (!resp.IsSuccessStatusCode && ((int)resp.StatusCode >= 500 || (int)resp.StatusCode == 429))
                {
                    var error = await resp.Content.ReadAsStringAsync();
                    Log.Warning("[Appsmith] Transient Failure: {StatusCode} - {Error}", resp.StatusCode, error);
                }

                return resp;
            });

            // Handle final result
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Log.Error("[Appsmith] Final Failure: {StatusCode}. Error: {Error}", response.StatusCode, errorBody);
                response.EnsureSuccessStatusCode();
            }
            else
            {
                Log.Information("[Appsmith] Workflow triggered successfully.");
            }
        }
    }
}
