using System;
using System.IO;
using System.Net;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;

namespace Pl.MeetingSource.Probe
{
    /// <summary>
    /// Custom API tavu_ProbeMeetingSource. Thin server-side proxy that lets the "Enable Teams sync"
    /// wizard (web resource on the tavu_meetingsource form) read the gateway's setup health without
    /// ever exposing the tenant key to the browser.
    ///
    /// It reads the gateway address + per-tenant key from the SAME environment variables the SLA
    /// plugin uses (tavu_GatewayUrl / tavu_GatewayKey), calls GET /api/teams/health, and returns the
    /// gateway response body VERBATIM. The plugin deliberately does not model the health shape: Graph
    /// error strings (and therefore the gate classification) can change on Microsoft's side, so the
    /// wizard parses the JSON and the plugin never needs a redeploy for that.
    ///
    /// Design + full context: docs/teams-sync-wizard-design.md.
    /// </summary>
    /// <remarks>
    /// Registered as the plugin type of the Custom API tavu_ProbeMeetingSource (global/unbound):
    ///   Request:  SourceName [String, optional, default "Teams"], UserId [String, optional, Entra object id]
    ///   Response: Ok [Boolean], HttpStatus [Integer], HealthJson [String]
    /// No SDK message step; the Custom API message is the trigger.
    ///
    /// Reads config on SystemService (environment variables the triggering user cannot see); performs
    /// no record writes.
    /// </remarks>
    public class Probe : PluginBase
    {
        // ===== Custom API parameters =====
        private const string InSourceName = "SourceName";
        private const string InUserId     = "UserId";
        private const string OutOk         = "Ok";
        private const string OutHttpStatus = "HttpStatus";
        private const string OutHealthJson = "HealthJson";

        // ===== environment variables (shared with Pl.Case.SlaAssignment) =====
        private const string GatewayUrlVar = "tavu_GatewayUrl"; // gateway base URL
        private const string GatewayKeyVar = "tavu_GatewayKey"; // per-tenant key (X-OpenTavu-Tenant-Key)

        private const string TenantHeader = "X-OpenTavu-Tenant-Key";
        private const int TimeoutSeconds = 30;

        private const string DefaultSource = "Teams";

        public Probe() : base(typeof(Probe)) { }

        protected override void ExecuteInternal(LocalPluginContext localContext)
        {
            if (localContext == null) throw new ArgumentNullException(nameof(localContext));
            var ctx = localContext.PluginExecutionContext;
            localContext.Trace("ProbeMeetingSource: entered. Message={0}.", ctx.MessageName);

            string sourceName = ParseOptionalString(ctx, InSourceName);
            if (string.IsNullOrWhiteSpace(sourceName)) sourceName = DefaultSource;
            string userId = ParseOptionalString(ctx, InUserId);

            // Only Teams has a health route today. Other sources (Fathom, ...) get their own route
            // later; the API stays source-generic on purpose.
            if (!string.Equals(sourceName, DefaultSource, StringComparison.OrdinalIgnoreCase))
            {
                SetOutputs(ctx, ok: true, status: 200,
                    json: "{\"healthy\":false,\"reason\":\"SourceNotSupported\",\"message\":\"No health probe is implemented for source '" + JsonEscape(sourceName) + "' yet.\"}");
                return;
            }

            // Config reads on SystemService (the key is not visible to the triggering user).
            IOrganizationService svc = localContext.SystemService;
            string baseUrl = ReadEnvironmentVariable(svc, GatewayUrlVar);
            string tenantKey = ReadEnvironmentVariable(svc, GatewayKeyVar);

            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(tenantKey))
            {
                localContext.Trace("ProbeMeetingSource: gateway env vars not set.");
                SetOutputs(ctx, ok: false, status: 0,
                    json: "{\"healthy\":false,\"reason\":\"GatewayNotConfigured\",\"message\":\"Environment variables tavu_GatewayUrl / tavu_GatewayKey are not set. Configure the gateway before probing.\"}");
                return;
            }

            baseUrl = baseUrl.TrimEnd('/');
            string url = baseUrl + "/api/teams/health";
            if (!string.IsNullOrWhiteSpace(userId))
                url += "?userId=" + Uri.EscapeDataString(userId.Trim());

            try
            {
                int status;
                string body = GetJson(url, tenantKey, out status);
                localContext.Trace("ProbeMeetingSource: gateway responded HTTP {0}.", status);
                SetOutputs(ctx, ok: true, status: status, json: body);
            }
            catch (WebException wex)
            {
                // Non-2xx from the gateway: surface the body + status so the wizard can still show
                // the detail (the health endpoint returns 200 for gate failures, so this is a real
                // transport/auth error against the gateway itself).
                int status = 0;
                string body = ReadError(wex, out status);
                localContext.Trace("ProbeMeetingSource: gateway HTTP error {0}: {1}", status, body);
                SetOutputs(ctx, ok: false, status: status,
                    json: string.IsNullOrEmpty(body)
                        ? "{\"healthy\":false,\"reason\":\"GatewayHttpError\",\"message\":\"" + JsonEscape(wex.Message) + "\"}"
                        : body);
            }
            catch (Exception ex)
            {
                localContext.Trace("ProbeMeetingSource: failed: {0}", ex.Message);
                SetOutputs(ctx, ok: false, status: 0,
                    json: "{\"healthy\":false,\"reason\":\"ProxyError\",\"message\":\"" + JsonEscape(ex.Message) + "\"}");
            }
        }

        private static void SetOutputs(IPluginExecutionContext ctx, bool ok, int status, string json)
        {
            ctx.OutputParameters[OutOk] = ok;
            ctx.OutputParameters[OutHttpStatus] = status;
            ctx.OutputParameters[OutHealthJson] = json ?? string.Empty;
        }

        // ---------- HTTP ----------

        private static string GetJson(string url, string tenantKey, out int status)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var http = (HttpWebRequest)WebRequest.Create(url);
            http.Method = "GET";
            http.Accept = "application/json";
            http.Headers[TenantHeader] = tenantKey;
            http.Timeout = TimeoutSeconds * 1000;

            using (var response = (HttpWebResponse)http.GetResponse())
            {
                status = (int)response.StatusCode;
                using (Stream stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static string ReadError(WebException wex, out int status)
        {
            status = 0;
            try
            {
                if (wex.Response is HttpWebResponse resp)
                {
                    status = (int)resp.StatusCode;
                    using (Stream s = resp.GetResponseStream())
                    using (var r = new StreamReader(s ?? Stream.Null, Encoding.UTF8))
                    {
                        return r.ReadToEnd();
                    }
                }
            }
            catch { /* ignore, fall through to message */ }
            return null;
        }

        // ---------- environment variables (mirrors Pl.Case.SlaAssignment) ----------

        private static string ReadEnvironmentVariable(IOrganizationService svc, string schemaName)
        {
            if (string.IsNullOrEmpty(schemaName)) return null;

            var defQ = new QueryExpression("environmentvariabledefinition")
            {
                ColumnSet = new ColumnSet("environmentvariabledefinitionid", "defaultvalue"),
                TopCount = 1,
                NoLock = true
            };
            defQ.Criteria.AddCondition("schemaname", ConditionOperator.Equal, schemaName);
            var defs = svc.RetrieveMultiple(defQ);
            if (defs.Entities.Count == 0) return null;

            var def = defs.Entities[0];
            string defaultValue = def.GetAttributeValue<string>("defaultvalue");

            var valQ = new QueryExpression("environmentvariablevalue")
            {
                ColumnSet = new ColumnSet("value"),
                TopCount = 1,
                NoLock = true
            };
            valQ.Criteria.AddCondition("environmentvariabledefinitionid", ConditionOperator.Equal, def.Id);
            var vals = svc.RetrieveMultiple(valQ);

            string current = vals.Entities.Count > 0 ? vals.Entities[0].GetAttributeValue<string>("value") : null;
            return !string.IsNullOrEmpty(current) ? current : defaultValue;
        }

        // ---------- input helpers ----------

        private static string ParseOptionalString(IPluginExecutionContext ctx, string paramName)
        {
            if (!ctx.InputParameters.Contains(paramName)) return null;
            return ctx.InputParameters[paramName] as string;
        }

        // ---------- misc ----------

        /// <summary>Minimal JSON string escaper for the small error payloads this proxy builds itself.</summary>
        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
