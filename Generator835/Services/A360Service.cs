using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Edi.Generator835.Services.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Polly;
using Polly.Retry;
using Serilog;

namespace Edi.Generator835.Services
{
    public class A360Service : IA360Service
    {
        private readonly string _baseUrl;
        private readonly string _username;
        private readonly string _password;
        private readonly int _botId;
        private readonly int _deploymentDeviceId;
        private readonly string _automationName;
        private readonly string _botRunType;
        private readonly long _runAsUserId;

        // Use static HttpClient to prevent socket exhaustion
        private static readonly HttpClient _httpClient = new HttpClient();

        // Resilience pipeline for retries and timeouts
        private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;

        public A360Service(string baseUrl, string username, string password, int botId, int deploymentDeviceId, string automationName, string botRunType = "A", long runAsUserId = 0, int timeoutSeconds = 120)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _username = username;
            _password = password;
            _botId = botId;
            _deploymentDeviceId = deploymentDeviceId;
            _automationName = automationName;
            _botRunType = botRunType;
            _runAsUserId = runAsUserId;

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
                        Log.Warning("[A360] Retry attempt {Attempt} due to: {Reason}", args.AttemptNumber, args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString());
                        return default;
                    }
                })
                .AddTimeout(TimeSpan.FromSeconds(timeoutSeconds))
                .Build();
        }

        private async Task<string> AuthenticateAsync()
        {
            var authUrl = $"{_baseUrl}/v2/authentication";
            var authPayload = new
            {
                username = _username,
                password = _password
            };

            var json = JsonConvert.SerializeObject(authPayload);

            using var response = await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
            {
                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, authUrl);
                requestMessage.Content = new StringContent(json, Encoding.UTF8, "application/json");
                return await _httpClient.SendAsync(requestMessage, cancellationToken);
            });

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Log.Error("[A360] Authentication Failed: {StatusCode}. Error: {Error}", response.StatusCode, errorBody);
                throw new Exception($"A360 Authentication failed with status {response.StatusCode}");
            }

            var responseStr = await response.Content.ReadAsStringAsync();
            var responseObj = JObject.Parse(responseStr);
            return responseObj["token"]?.ToString() ?? string.Empty;
        }

        public async Task TriggerBotAsync(object Payload)
        {
            // 1. Authenticate to get Token
            Log.Information("Authenticating A360 Bot at {BaseUrl}/v2/authentication", _baseUrl);
            var token = await AuthenticateAsync();

            // 2. Serialize the validation errors payload (like Appsmith) to a string
            var stringPayload = JsonConvert.SerializeObject(
                Payload,
                Formatting.None,
                new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    NullValueHandling = NullValueHandling.Include,
                    ContractResolver = new NullToEmptyStringContractResolver(),
                });

            // 3. Construct the A360 payload based on the predefined format
            var a360Payload = new JObject
            {
                ["botId"] = _botId,
                ["automationName"] = _automationName,
                ["description"] = "",
                ["botLabel"] = "string",
                ["runElevated"] = true,
                ["hideBotAgentUi"] = false,
                ["automationPriority"] = "PRIORITY_MEDIUM",
                ["botInput"] = JObject.FromObject(new Dictionary<string, object>
                {
                    {
                        "pStrEdiDataPayload", new Dictionary<string, string>
                        {
                            { "type", "STRING" },
                            { "string", stringPayload }
                        }
                    }
                })
            };

            if (_botRunType == "U" || _botRunType == "u")
            {
                a360Payload["unattendedRequest"] = JObject.FromObject(new
                {
                    runAsUserIds = new[] { _runAsUserId },
                    deviceUsageType = "RUN_ONLY_ON_DEFAULT_DEVICE"
                });
            }
            else
            {
                a360Payload["attendedRequest"] = JObject.FromObject(new
                {
                    deploymentDeviceId = _deploymentDeviceId,
                    queueDeployment = true,
                    launchInChildWindow = false
                });
            }


            var json = JsonConvert.SerializeObject(a360Payload, Formatting.Indented);
            var deployUrl = $"{_baseUrl}/v4/automations/deploy";

            // Log trigger
            Log.Information("Triggering A360 Bot: {ApiUrl}", deployUrl);
            Log.Debug("A360 Payload: {Payload}", json);

            // 4. Execute Deploy with resilience policy
            using var response = await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
            {
                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, deployUrl);
                requestMessage.Content = new StringContent(json, Encoding.UTF8, "application/json");

                // Providing the A360 Token obtained from v2/authentication
                if (!string.IsNullOrEmpty(token))
                {
                    requestMessage.Headers.Add("X-Authorization", token);
                }

                // Use cancellationToken directly
                var resp = await _httpClient.SendAsync(requestMessage, cancellationToken);

                // Log 5xx errors for debugging
                if (!resp.IsSuccessStatusCode && ((int)resp.StatusCode >= 500 || (int)resp.StatusCode == 429))
                {
                    var error = await resp.Content.ReadAsStringAsync();
                    Log.Warning("[A360] Transient Deploy Failure: {StatusCode} - {Error}", resp.StatusCode, error);
                }

                return resp;
            });

            // 5. Handle final deploy result
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Log.Error("[A360] Final Deploy Failure: {StatusCode}. Error: {Error}", response.StatusCode, errorBody);
                response.EnsureSuccessStatusCode();
            }
            else
            {
                Log.Information("[A360] Bot triggered and deployed successfully.");
            }
        }
    }
}
