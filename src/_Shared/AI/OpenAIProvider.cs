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
    /// IAIProvider for the OpenAI platform API (api.openai.com). Sandbox-safe
    /// (HttpWebRequest + DataContractJsonSerializer). Differs from Azure OpenAI:
    /// Bearer auth, model in the body, no deployment/api-version in the URL.
    /// </summary>
    public sealed class OpenAIProvider : IAIProvider
    {
        private const int DefaultTimeoutSeconds = 100;
        private const string DefaultEndpoint = "https://api.openai.com";

        public AICompletionResult Complete(AICompletionRequest request)
        {
            if (request == null)
                return AICompletionResult.Fail("AICompletionRequest was null.");
            if (string.IsNullOrEmpty(request.DeploymentOrModel))
                return AICompletionResult.Fail("Model is missing.");
            if (string.IsNullOrEmpty(request.ApiKey))
                return AICompletionResult.Fail("ApiKey is missing.");

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                string baseUrl = string.IsNullOrEmpty(request.Endpoint)
                    ? DefaultEndpoint
                    : request.Endpoint.TrimEnd('/');
                string url = baseUrl + "/v1/chat/completions";

                var body = new ChatRequest
                {
                    Model = request.DeploymentOrModel,
                    Messages = new List<ChatMessage>
                    {
                        new ChatMessage { Role = "system", Content = request.SystemPrompt ?? string.Empty },
                        new ChatMessage { Role = "user",   Content = request.UserContent ?? string.Empty }
                    },
                    Temperature = request.Temperature,
                    MaxTokens = request.MaxOutputTokens > 0 ? request.MaxOutputTokens : 800,
                    ResponseFormat = request.JsonResponse ? new ResponseFormat { Type = "json_object" } : null
                };

                byte[] payload = Encoding.UTF8.GetBytes(Serialize(body));

                var http = (HttpWebRequest)WebRequest.Create(url);
                http.Method = "POST";
                http.ContentType = "application/json";
                http.Accept = "application/json";
                http.Headers["Authorization"] = "Bearer " + request.ApiKey;
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

                return AICompletionResult.Ok(parsed.Choices[0].Message.Content, promptTokens, completionTokens);
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

                return AICompletionResult.Fail("AI HTTP error: " + detail);
            }
            catch (Exception ex)
            {
                return AICompletionResult.Fail("AI call failed: " + ex.Message);
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
        private class ChatRequest
        {
            [DataMember(Name = "model", Order = 0)]
            public string Model { get; set; }

            [DataMember(Name = "messages", Order = 1)]
            public List<ChatMessage> Messages { get; set; }

            [DataMember(Name = "temperature", Order = 2)]
            public double Temperature { get; set; }

            [DataMember(Name = "max_tokens", Order = 3)]
            public int MaxTokens { get; set; }

            [DataMember(Name = "response_format", Order = 4, EmitDefaultValue = false)]
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
