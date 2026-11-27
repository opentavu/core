using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Pl.Setup.Initialize
{
    /// <summary>
    /// The language and time zone sales periods are built in. Shared by tavu_InitializeConfiguration and
    /// tavu_RunForecastSnapshot (the file is linked into Pl.Forecast.Snapshot together with SalesPeriodGenerator and
    /// SetupReport), so both create the same periods whoever calls them.
    /// </summary>
    internal static class SalesPeriodSettings
    {
        internal const int FallbackTimeZoneCode = 92; // (UTC) Coordinated Universal Time

        /// <summary>The organization's base language (period names in Spanish when it is 3082).</summary>
        internal static int OrganizationLanguage(IOrganizationService svc)
        {
            var q = new QueryExpression("organization") { ColumnSet = new ColumnSet("languagecode"), TopCount = 1, NoLock = true };
            EntityCollection r = svc.RetrieveMultiple(q);
            return r.Entities.Count > 0 ? r.Entities[0].GetAttributeValue<int>("languagecode") : 1033;
        }

        /// <summary>The user's time zone from their settings, or UTC when they have none.</summary>
        internal static int UserTimeZone(IOrganizationService svc, Guid userId)
        {
            var q = new QueryExpression("usersettings") { ColumnSet = new ColumnSet("timezonecode"), TopCount = 1, NoLock = true };
            q.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
            EntityCollection r = svc.RetrieveMultiple(q);
            if (r.Entities.Count == 0 || !r.Entities[0].Contains("timezonecode")) return FallbackTimeZoneCode;
            return r.Entities[0].GetAttributeValue<int>("timezonecode");
        }

        /// <summary>
        /// The time zone sales periods are built in: the active default business calendar's tavu_timezone, else
        /// <paramref name="fallback"/> (the calling user's).
        /// </summary>
        internal static int PeriodTimeZone(IOrganizationService svc, int fallback)
        {
            var q = new QueryExpression("tavu_businesscalendar") { ColumnSet = new ColumnSet("tavu_timezone"), TopCount = 1, NoLock = true };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            q.Criteria.AddCondition("tavu_isdefault", ConditionOperator.Equal, true);
            EntityCollection r = svc.RetrieveMultiple(q);
            int? zone = r.Entities.Count > 0 ? r.Entities[0].GetAttributeValue<int?>("tavu_timezone") : null;
            return zone ?? fallback;
        }
    }
}
