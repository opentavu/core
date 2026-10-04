using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;

namespace Pl.Setup.Initialize
{
    /// <summary>
    /// Keeps sales periods ahead of time so forecasting never runs out of a current period.
    ///   - Runs only when System Settings "Auto-create Sales Periods" is Yes (empty counts as Yes).
    ///   - Granularity: System Settings "Forecast Period Type" (Month or Quarter; default Quarter).
    ///   - Fiscal year start: Company Profile "Fiscal Year Start Month" (1 to 12; empty or no profile = January).
    ///   - Covers the fiscal year that contains today and the next one.
    ///   - Never modifies or deletes a period: a candidate that overlaps an existing period of the same
    ///     type (active or not) is skipped, so a renamed period or a changed fiscal start never duplicates.
    /// Dates are written as local midnight in the caller's time zone, the same convention the
    /// "Date only / User local" columns use when a person enters them in the app.
    /// Naming: the fiscal year is labeled by the calendar year in which it ends ("Q1 2027", or
    /// "Q1 FY2027" when the fiscal year does not start in January; months use the calendar month).
    /// </summary>
    internal sealed class SalesPeriodGenerator
    {
        private const string PeriodEntity = "tavu_salesperiod";
        private const string PeriodName = "tavu_name";
        private const string PeriodType = "tavu_periodtype";
        private const string PeriodStart = "tavu_startdate";
        private const string PeriodEnd = "tavu_enddate";
        private const string PeriodFiscalYear = "tavu_fiscalyear";

        private const string SettingsEntity = "tavu_systemsettings";
        private const string SettingsAutoCreate = "tavu_autocreatesalesperiods";
        private const string SettingsPeriodType = "tavu_forecastperiodtype";

        private const string ProfileEntity = "tavu_companyprofile";
        private const string ProfileFiscalStart = "tavu_fiscalyearstartmonth";

        public const int TypeMonth = 576600000;
        public const int TypeQuarter = 576600001;

        private const int SpanishLcid = 3082;

        private readonly IOrganizationService _svc;
        private readonly LocalPluginContext _ctx;
        private readonly SetupReport _report;
        private readonly int _timeZoneCode;
        private readonly int _languageCode;

        public SalesPeriodGenerator(IOrganizationService svc, LocalPluginContext ctx, SetupReport report, int timeZoneCode, int languageCode)
        {
            _svc = svc;
            _ctx = ctx;
            _report = report;
            _timeZoneCode = timeZoneCode;
            _languageCode = languageCode;
        }

        public void Run()
        {
            Entity settings = Top1(SettingsEntity, new ColumnSet(SettingsAutoCreate, SettingsPeriodType));
            if (settings != null && settings.Contains(SettingsAutoCreate) && settings[SettingsAutoCreate] is bool
                && !(bool)settings[SettingsAutoCreate])
            {
                _report.Info("Sales periods", "Automatic creation is off in System Settings; no periods were created.");
                return;
            }

            int periodType = settings != null && settings.GetAttributeValue<OptionSetValue>(SettingsPeriodType) != null
                ? settings.GetAttributeValue<OptionSetValue>(SettingsPeriodType).Value
                : TypeQuarter;

            int startMonth = FiscalStartMonth();
            DateTime todayLocal = LocalToday();

            List<Period> wanted = Plan(todayLocal, startMonth, periodType);
            List<Entity> existing = LoadExisting(periodType);

            int created = 0;
            foreach (Period p in wanted)
            {
                DateTime startUtc = ToUtc(p.Start);
                DateTime endUtc = ToUtc(p.LastDay);
                if (Overlaps(existing, startUtc, endUtc)) continue;

                var e = new Entity(PeriodEntity);
                e[PeriodName] = p.Name;
                e[PeriodType] = new OptionSetValue(periodType);
                e[PeriodStart] = startUtc;
                e[PeriodEnd] = endUtc;
                e[PeriodFiscalYear] = p.FiscalYear;
                e.Id = _svc.Create(e);
                existing.Add(e);
                created++;
            }

            _ctx.Trace("Sales periods: {0} created (type {1}, fiscal start month {2}).", created, periodType, startMonth);
            if (created > 0)
            {
                _report.Created += created;
                _report.Ok("Sales periods", created.ToString(CultureInfo.InvariantCulture) + " periods created.");
            }
        }

        // ------------------------------------------------------------------ planning (pure, testable)

        internal sealed class Period
        {
            public string Name;
            public DateTime Start;    // local date, first day
            public DateTime LastDay;  // local date, last day (inclusive)
            public int FiscalYear;    // calendar year in which the fiscal year ends
        }

        internal List<Period> Plan(DateTime todayLocal, int startMonth, int periodType)
        {
            int fyStartYear = todayLocal.Month >= startMonth ? todayLocal.Year : todayLocal.Year - 1;
            var result = new List<Period>();
            for (int fy = 0; fy < 2; fy++)
            {
                var fyStart = new DateTime(fyStartYear + fy, startMonth, 1);
                int label = fyStart.AddMonths(11).Year;
                int step = periodType == TypeMonth ? 1 : 3;
                for (int i = 0; i < 12; i += step)
                {
                    DateTime start = fyStart.AddMonths(i);
                    DateTime last = start.AddMonths(step).AddDays(-1);
                    result.Add(new Period
                    {
                        Start = start,
                        LastDay = last,
                        FiscalYear = label,
                        Name = periodType == TypeMonth
                            ? MonthName(start)
                            : string.Format(CultureInfo.InvariantCulture, startMonth == 1 ? "Q{0} {1}" : "Q{0} FY{1}", i / 3 + 1, label)
                    });
                }
            }
            return result;
        }

        private string MonthName(DateTime d)
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(_languageCode == SpanishLcid ? "es-ES" : "en-US");
            string m = culture.DateTimeFormat.GetMonthName(d.Month);
            if (m.Length > 0) m = char.ToUpper(m[0], culture) + m.Substring(1);
            return m + " " + d.Year.ToString(CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ data

        private int FiscalStartMonth()
        {
            Entity profile = Top1(ProfileEntity, new ColumnSet(ProfileFiscalStart));
            if (profile == null || !profile.Contains(ProfileFiscalStart) || profile[ProfileFiscalStart] == null) return 1;
            int m = profile.GetAttributeValue<int>(ProfileFiscalStart);
            if (m < 1 || m > 12)
            {
                _report.Warn("Company profile", "Fiscal Year Start Month must be between 1 and 12; January was used.");
                return 1;
            }
            return m;
        }

        private List<Entity> LoadExisting(int periodType)
        {
            var q = new QueryExpression(PeriodEntity)
            {
                ColumnSet = new ColumnSet(PeriodStart, PeriodEnd, PeriodType),
                NoLock = true
            };
            q.Criteria.AddCondition(PeriodType, ConditionOperator.Equal, periodType);
            return new List<Entity>(_svc.RetrieveMultiple(q).Entities);
        }

        private static bool Overlaps(List<Entity> existing, DateTime startUtc, DateTime endUtc)
        {
            foreach (Entity e in existing)
            {
                if (!e.Contains(PeriodStart) || !e.Contains(PeriodEnd)) continue;
                DateTime s = e.GetAttributeValue<DateTime>(PeriodStart);
                DateTime f = e.GetAttributeValue<DateTime>(PeriodEnd);
                if (s <= endUtc && f >= startUtc) return true;
            }
            return false;
        }

        private DateTime LocalToday()
        {
            var resp = (LocalTimeFromUtcTimeResponse)_svc.Execute(new LocalTimeFromUtcTimeRequest
            {
                TimeZoneCode = _timeZoneCode,
                UtcTime = DateTime.UtcNow
            });
            return resp.LocalTime.Date;
        }

        private DateTime ToUtc(DateTime localDate)
        {
            var resp = (UtcTimeFromLocalTimeResponse)_svc.Execute(new UtcTimeFromLocalTimeRequest
            {
                TimeZoneCode = _timeZoneCode,
                LocalTime = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified)
            });
            return DateTime.SpecifyKind(resp.UtcTime, DateTimeKind.Utc);
        }

        private Entity Top1(string entity, ColumnSet columns)
        {
            var q = new QueryExpression(entity) { ColumnSet = columns, TopCount = 1, NoLock = true };
            EntityCollection r = _svc.RetrieveMultiple(q);
            return r.Entities.Count > 0 ? r.Entities[0] : null;
        }
    }
}
