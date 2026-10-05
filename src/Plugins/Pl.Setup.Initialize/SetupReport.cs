using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Pl.Setup.Initialize
{
    /// <summary>Severity of one report line, ordered from fine to blocking.</summary>
    internal enum Level { Ok = 0, Info = 1, Warning = 2, Error = 3 }

    /// <summary>One line of the setup report (seed counts or a diagnostic check).</summary>
    internal sealed class ReportItem
    {
        public Level Level;
        public string Area;
        public string Message;
    }

    /// <summary>
    /// Collects what the seed did and what the diagnostic found, and renders it as a short readable
    /// summary plus a JSON document the System Settings button and the daily flow can parse.
    /// </summary>
    internal sealed class SetupReport
    {
        private readonly List<ReportItem> _items = new List<ReportItem>();

        public int Created;
        public int Filled;
        public int Skipped;
        public bool Complete = true;
        /// <summary>True when only sales periods were maintained (Mode = periods): the seed did not run.</summary>
        public bool PeriodsOnly;

        public void Add(Level level, string area, string message)
        {
            foreach (var i in _items)
                if (i.Level == level && i.Area == area && i.Message == message) return; // same finding once
            _items.Add(new ReportItem { Level = level, Area = area, Message = message });
        }

        public void Ok(string area, string message) { Add(Level.Ok, area, message); }
        public void Info(string area, string message) { Add(Level.Info, area, message); }
        public void Warn(string area, string message) { Add(Level.Warning, area, message); }
        public void Fail(string area, string message) { Add(Level.Error, area, message); }

        public int Count(Level level)
        {
            int n = 0;
            foreach (var i in _items) if (i.Level == level) n++;
            return n;
        }

        public string ToSummary()
        {
            var sb = new StringBuilder();
            if (PeriodsOnly)
                sb.AppendFormat(CultureInfo.InvariantCulture, "Sales periods: {0} created.", Created);
            else
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "Configuration: {0} rows created, {1} rows completed, {2} already set.{3}",
                    Created, Filled, Skipped,
                    Complete ? string.Empty : " Not finished yet: run it again to continue.");
            sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "Checks: {0} errors, {1} warnings.", Count(Level.Error), Count(Level.Warning));
            foreach (var i in _items)
            {
                if (i.Level < Level.Warning) continue;
                sb.AppendLine();
                sb.Append(i.Level == Level.Error ? "[Error] " : "[Warning] ");
                sb.Append(i.Area).Append(": ").Append(i.Message);
            }
            return sb.ToString();
        }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"complete\":").Append(Complete ? "true" : "false");
            sb.Append(",\"created\":").Append(Created.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"filled\":").Append(Filled.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"skipped\":").Append(Skipped.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"errors\":").Append(Count(Level.Error).ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"warnings\":").Append(Count(Level.Warning).ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"items\":[");
            for (int k = 0; k < _items.Count; k++)
            {
                if (k > 0) sb.Append(',');
                var i = _items[k];
                sb.Append("{\"level\":\"").Append(i.Level.ToString().ToLowerInvariant()).Append('"');
                sb.Append(",\"area\":").Append(Quote(i.Area));
                sb.Append(",\"message\":").Append(Quote(i.Message)).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string Quote(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
