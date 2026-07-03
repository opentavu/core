using System;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace OpenTavu.Dataverse.AI
{
    /// <summary>
    /// IAIProvider that routes the completion through the OpenTavu gateway
    /// (POST {baseUrl}/api/ai/complete) instead of calling the AI vendor directly.
    /// The AI key lives ONLY in the gateway; the tenant holds just the gateway base URL
    /// and a per-tenant key (sent as X-OpenTavu-Tenant-Key). Sandbox-safe
    /// (HttpWebRequest + DataContractJsonSerializer), same convention as OpenAIProvider.
    /// </summary>
    public sealed class GatewayProvider : IAIProvider
    {
        private const int DefaultTimeoutSeconds = 100;
        private const string TenantHeader = "X-OpenTavu-Tenant-Key";

        private readonly string _baseUrl;
        private readonly string _tenantKey;

        public GatewayProvider(string gatewayBaseUrl, string tenantKey)
        {
            _baseUrl = (gatewayBaseUrl ?? string.Empty).TrimEnd('/');
            _tenantKey = tenantKey;
        }

        public AICompletionResult Complete(AICompletionRequest request)
        {
            if (request == null) return AICompletionResult.Fail("AICompletionRequest was null.");
            if (string.IsNullOrEmpty(_baseUrl)) return AICompletionResult.Fail("Gateway URL is missing.");
            if (string.IsNullOrEmpty(_tenantKey)) return AICompletionResult.Fail("Gateway tenant key is missing.");

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                string url = _baseUrl + "/api/ai/complete";

                var body = new GatewayRequest
                {
                    SystemPrompt = request.SystemPrompt ?? string.Empty,
                    UserContent = request.UserContent ?? string.Empty,
                    Temperature = request.Temperature,
                    MaxOutputTokens = request.MaxOutputTokens > 0 ? request.MaxOutputTokens : 800,
                    JsonResponse = request.JsonResponse
                };

                byte[] payload = Encoding.UTF8.GetBytes(Serialize(body));

                var http = (HttpWebRequest)WebRequest.Create(url);
                http.Method = "POST";
                http.ContentType = "application/json";
                http.Accept = "application/json";
                http.Headers[TenantHeader] = _tenantKey;
                http.Timeout = (request.TimeoutSeconds > 0 ? request.TimeoutSeconds : DefaultTimeoutSeconds) * 1000;
                http.ContentLength = payload.Length;

                using (Stream rs = http.GetRequestStream())
                {
                    rs.Write(payload, 0, payload.Length);
                }

                string responseJson;
                using (var response = (HttpWebResponse)http.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    responseJson = reader.ReadToEnd();
                }

                var parsed = Deserialize<GatewayResponse>(responseJson);
                if (parsed == null || parsed.Content == null)
                    return AICompletionResult.Fail("Gateway response had no content.");

                return AICompletionResult.Ok(parsed.Content, parsed.PromptTokens, parsed.CompletionTokens);
            }
            catch (WebException wex)
            {
                string detail = wex.Message;
                try
                {
                    if (wex.Response != null)
                    {
                        using (Stream es = wex.Response.GetResponseStream())
                        using (var er = new StreamReader(es, Encoding.UTF8))
                        {
                            detail = er.ReadToEnd();
                        }
                    }
                }
                catch { /* ignore */ }

                return AICompletionResult.Fail("Gateway HTTP error: " + detail);
            }
            catch (Exception ex)
            {
                return AICompletionResult.Fail("Gateway call failed: " + ex.Message);
            }
        }

        private static string Serialize<T>(T obj)
        {
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(ms, obj);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static T Deserialize<T>(string json)
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(ms);
            }
        }

        [DataContract]
        private class GatewayRequest
        {
            [DataMember(Name = "systemPrompt", Order = 0)] public string SystemPrompt { get; set; }
            [DataMember(Name = "userContent", Order = 1)] public string UserContent { get; set; }
            [DataMember(Name = "temperature", Order = 2)] public double Temperature { get; set; }
            [DataMember(Name = "maxOutputTokens", Order = 3)] public int MaxOutputTokens { get; set; }
            [DataMember(Name = "jsonResponse", Order = 4)] public bool JsonResponse { get; set; }
        }

        [DataContract]
        private class GatewayResponse
        {
            [DataMember(Name = "content")] public string Content { get; set; }
            [DataMember(Name = "promptTokens")] public int PromptTokens { get; set; }
            [DataMember(Name = "completionTokens")] public int CompletionTokens { get; set; }
        }
    }
}
