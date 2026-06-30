using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace OpenTavu.Dataverse.AI
{
    /// <summary>
    /// Default IAIProvider implementation: Azure OpenAI Chat Completions.
    /// Sandbox-safe by design — uses only HttpWebRequest (System) and
    /// DataContractJsonSerializer (System.Runtime.Serialization), so it can be
    /// linked into a plugin and deployed as a single assembly with no external
    /// dependency DLLs.
    ///
    /// Stateless: all connection details come from the AICompletionRequest, which
    /// the caller builds after resolving tavu_aimodel + the secret.
    /// </summary>
    public sealed class AzureOpenAIProvider : IAIProvider
    {
        private const int DefaultTimeoutSeconds = 100; // under the ~120s plugin ceiling

        public AICompletionResult Complete(AICompletionRequest request)
        {
            if (request == null)
                return AICompletionResult.Fail("AICompletionRequest was null.");
            if (string.IsNullOrEmpty(request.Endpoint))
                return AICompletionResult.Fail("Endpoint is missing.");
            if (string.IsNullOrEmpty(request.DeploymentOrModel))
                return AICompletionResult.Fail("DeploymentOrModel is missing.");
            if (string.IsNullOrEmpty(request.ApiKey))
                return AICompletionResult.Fail("ApiKey is missing.");

            try
            {
                // Azure requires TLS 1.2; make sure it's enabled without downgrading.
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                string url = request.Endpoint.TrimEnd('/')
                    + "/openai/deployments/" + request.DeploymentOrModel
                    + "/chat/completions?api-version="
                    + (string.IsNullOrEmpty(request.ApiVersion) ? "2024-10-21" : request.ApiVersion);

                var body = new ChatRequest
                {
                    Messages = new List<ChatMessage>
                    {
                        new ChatMessage { Role = "system", Content = request.SystemPrompt ?? string.Empty },
                        new ChatMessage { Role = "user",   Content = request.UserContent ?? string.Empty }
                    },
                    Temperature = request.Temperature,
                    MaxTokens = request.MaxOutputTokens > 0 ? request.MaxOutputTokens : 800,
                    ResponseFormat = request.JsonResponse ? new ResponseFormat { Type = "json_object" } : null
                };

                string requestJson = Serialize(body);
                byte[] payload = Encoding.UTF8.GetBytes(requestJson);

                var http = (HttpWebRequest)WebRequest.Create(url);
                http.Method = "POST";
                http.ContentType = "application/json";
                http.Accept = "application/json";
                http.Headers["api-key"] = request.ApiKey;
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

                var parsed = Deserialize<ChatResponse>(responseJson);

                if (parsed == null || parsed.Choices == null || parsed.Choices.Count == 0
                    || parsed.Choices[0].Message == null)
                {
                    return AICompletionResult.Fail("AI response had no choices/content.");
                }

                int promptTokens = parsed.Usage != null ? parsed.Usage.PromptTokens : 0;
                int completionTokens = parsed.Usage != null ? parsed.Usage.CompletionTokens : 0;

                return AICompletionResult.Ok(
                    parsed.Choices[0].Message.Content,
                    promptTokens,
                    completionTokens);
            }
            catch (WebException wex)
            {
                // Surface the API error body (rate limits, bad deployment, auth, etc.)
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
                catch { /* ignore secondary read failure */ }

                return AICompletionResult.Fail("AI HTTP error: " + detail);
            }
            catch (Exception ex)
            {
                return AICompletionResult.Fail("AI call failed: " + ex.Message);
            }
        }

        // ----- JSON helpers (sandbox-native) -----

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

        // ----- Wire DTOs for the Azure OpenAI Chat Completions contract -----

        [DataContract]
        private class ChatRequest
        {
            [DataMember(Name = "messages", Order = 0)]
            public List<ChatMessage> Messages { get; set; }

            [DataMember(Name = "temperature", Order = 1)]
            public double Temperature { get; set; }

            [DataMember(Name = "max_tokens", Order = 2)]
            public int MaxTokens { get; set; }

            [DataMember(Name = "response_format", Order = 3, EmitDefaultValue = false)]
            public ResponseFormat ResponseFormat { get; set; }
        }

        [DataContract]
        private class ChatMessage
        {
            [DataMember(Name = "role", Order = 0)]
            public string Role { get; set; }

            [DataMember(Name = "content", Order = 1)]
            public string Content { get; set; }
        }

        [DataContract]
        private class ResponseFormat
        {
            [DataMember(Name = "type", Order = 0)]
            public string Type { get; set; }
        }

        [DataContract]
        private class ChatResponse
        {
            [DataMember(Name = "choices")]
            public List<Choice> Choices { get; set; }

            [DataMember(Name = "usage")]
            public Usage Usage { get; set; }
        }

        [DataContract]
        private class Choice
        {
            [DataMember(Name = "message")]
            public ChatMessage Message { get; set; }
        }

        [DataContract]
        private class Usage
        {
            [DataMember(Name = "prompt_tokens")]
            public int PromptTokens { get; set; }

            [DataMember(Name = "completion_tokens")]
            public int CompletionTokens { get; set; }
        }
    }
}
