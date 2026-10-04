using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Metadata.Query;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;

namespace Pl.Setup.Initialize
{
    /// <summary>
    /// Applies the embedded seed (opentavu-seed.json) idempotently:
    ///   - match by natural key (keyField, possibly composite), never by GUID;
    ///   - create what is missing, fill EMPTY fields of existing rows, never overwrite a value;
    ///   - convert JSON values with the live column metadata (choice, whole number, decimal...);
    ///   - resolve '@ref' lookups by the referenced table's natural key;
    ///   - apply '@i18n.es' overrides when the organization base language is Spanish (3082);
    ///   - stop cleanly when the time budget runs out (re-running continues where it stopped).
    /// Rows the firm deactivated still count as existing, so they are never re-created.
    ///
    /// Errors: the Custom API runs inside one database transaction, and a failed Dataverse call
    /// rolls that transaction back even if caught. So nothing here "skips and continues" after a
    /// failed call: predictable problems (missing table or column, bad value) are detected up front
    /// without failing calls, and anything else stops the run with a message naming the table.
    /// </summary>
    internal sealed class SeedEngine
    {
        private const int SpanishLcid = 3082;
        private const string SingletonKey = "@singleton";

        private readonly IOrganizationService _svc;
        private readonly LocalPluginContext _ctx;
        private readonly SetupReport _report;
        private readonly Stopwatch _clock;
        private readonly TimeSpan _budget;
        private readonly int _languageCode;
        private readonly int _timeZoneCode;
        private readonly HashSet<string> _excludedPacks;

        // table -> natural key field (single-field keys only; used by '@ref')
        private readonly Dictionary<string, string> _tableKeyField = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // table -> (normalized key -> id)
        private readonly Dictionary<string, Dictionary<string, Guid>> _refCache = new Dictionary<string, Dictionary<string, Guid>>(StringComparer.OrdinalIgnoreCase);
        // table -> attribute metadata by logical name
        private readonly Dictionary<string, Dictionary<string, AttributeMetadata>> _meta = new Dictionary<string, Dictionary<string, AttributeMetadata>>(StringComparer.OrdinalIgnoreCase);

        public SeedEngine(IOrganizationService svc, LocalPluginContext ctx, SetupReport report, Stopwatch clock,
                          TimeSpan budget, int languageCode, int timeZoneCode, HashSet<string> excludedPacks)
        {
            _svc = svc;
            _ctx = ctx;
            _report = report;
            _clock = clock;
            _budget = budget;
            _languageCode = languageCode;
            _timeZoneCode = timeZoneCode;
            _excludedPacks = excludedPacks ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public void Run(Dictionary<string, object> seed)
        {
            var tables = seed.ContainsKey("tables") ? seed["tables"] as List<object> : null;
            if (tables == null)
            {
                _report.Fail("Seed", "The embedded seed file has no 'tables' section.");
                return;
            }

            // First pass: remember every table's single-field key for '@ref' resolution,
            // and load the column metadata of all seeded tables in one call.
            var names = new List<string>();
            foreach (object t in tables)
            {
                var def = t as Dictionary<string, object>;
                if (def == null) continue;
                string name = Str(def, "table");
                if (!string.IsNullOrEmpty(name)) names.Add(name);
                string key = def.ContainsKey("keyField") ? def["keyField"] as string : null;
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(key) && key != SingletonKey)
                    _tableKeyField[name] = key;
            }
            LoadMetadata(names);

            foreach (object t in tables)
            {
                if (OutOfTime()) { _report.Complete = false; return; }
                var def = t as Dictionary<string, object>;
                if (def == null) continue;
                try
                {
                    SeedTable(def);
                }
                catch (InvalidPluginExecutionException) { throw; }
                catch (Exception ex)
                {
                    _ctx.Trace("Seed table {0} failed: {1}", Str(def, "table"), ex.ToString());
                    throw new InvalidPluginExecutionException(
                        "Setup stopped while seeding '" + Str(def, "table") + "': " + ex.Message +
                        " Nothing from this run was saved. Fix the cause and run it again.", ex);
                }
            }
        }

        private bool OutOfTime()
        {
            return _clock.Elapsed > _budget;
        }

        // ------------------------------------------------------------------ tables

        private void SeedTable(Dictionary<string, object> def)
        {
            string table = Str(def, "table");
            var rows = def.ContainsKey("rows") ? def["rows"] as List<object> : null;
            if (string.IsNullOrEmpty(table) || rows == null || rows.Count == 0) return; // generated tables (sales periods) have no rows

            string pack = Str(def, "pack");
            if (!string.IsNullOrEmpty(pack) && _excludedPacks.Contains(pack))
            {
                _report.Info(table, "Skipped: optional pack '" + pack + "' was excluded.");
                return;
            }

            Dictionary<string, AttributeMetadata> meta = Metadata(table);
            if (meta == null)
            {
                _report.Fail(table, "This table does not exist in the environment. Is the OpenTavu solution fully imported?");
                return;
            }

            bool singleton = string.Equals(def["keyField"] as string, SingletonKey, StringComparison.Ordinal);
            List<string> keyFields = KeyFields(def["keyField"]);
            // Transition key: rows created before the table had a code are matched once by this
            // field (only among rows whose code is still empty), and then get their code filled.
            string fallbackKey = Str(def, "fallbackKeyField");
            if (fallbackKey != null && !meta.ContainsKey(fallbackKey)) fallbackKey = null;

            // Columns to read for existing rows: every seeded column that exists in the table.
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var missingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object r in rows)
            {
                var row = r as Dictionary<string, object>;
                if (row == null) continue;
                foreach (string f in row.Keys)
                {
                    if (f.StartsWith("@", StringComparison.Ordinal)) continue;
                    if (meta.ContainsKey(f)) columns.Add(f); else missingColumns.Add(f);
                }
            }
            foreach (string f in missingColumns)
                _report.Warn(table, "Column '" + f + "' is in the seed but not in the environment; it was skipped.");
            if (fallbackKey != null) columns.Add(fallbackKey);

            List<Entity> existing = LoadAll(table, columns);
            var byKey = new Dictionary<string, Entity>(StringComparer.Ordinal);
            var byFallback = new Dictionary<string, Entity>(StringComparer.Ordinal);
            if (!singleton)
            {
                foreach (Entity e in existing)
                {
                    string k = KeyOf(e, keyFields);
                    if (k != null) { if (!byKey.ContainsKey(k)) byKey[k] = e; continue; }
                    if (fallbackKey == null) continue;
                    string f = NormValue(e.Contains(fallbackKey) ? e[fallbackKey] : null);
                    if (f != null && !byFallback.ContainsKey(f)) byFallback[f] = e;
                }
                RegisterRefs(table, existing);
            }

            int created = 0, filled = 0, skipped = 0;
            foreach (object r in rows)
            {
                if (OutOfTime()) { _report.Complete = false; break; }
                var row = r as Dictionary<string, object>;
                if (row == null) continue;

                Entity desired = BuildEntity(table, row, meta, true);
                // The untranslated row: '@ref' keys in the seed are always the base-language values,
                // so a row stored under its Spanish name must still be found by its English key.
                Entity baseRow = _languageCode == SpanishLcid ? BuildEntity(table, row, meta, false) : desired;

                Entity current = null;
                if (singleton)
                {
                    current = existing.Count > 0 ? existing[0] : null;
                }
                else
                {
                    string k = KeyOf(desired, keyFields);
                    if (k == null)
                    {
                        _report.Warn(table, "A seed row has no value for its key " + string.Join(" + ", keyFields.ToArray()) + "; it was skipped.");
                        continue;
                    }
                    if (!byKey.TryGetValue(k, out current))
                    {
                        string baseKey = KeyOf(baseRow, keyFields);
                        if (baseKey != null) byKey.TryGetValue(baseKey, out current);
                    }
                    if (current == null && fallbackKey != null)
                    {
                        current = TakeFallback(byFallback, desired, fallbackKey) ?? TakeFallback(byFallback, baseRow, fallbackKey);
                        if (current != null) byKey[k] = current;
                    }
                    if (current != null && !ReferenceEquals(baseRow, desired)) AddRef(table, baseRow, current.Id);
                }

                if (current == null)
                {
                    Guid id = _svc.Create(desired);
                    desired.Id = id;
                    created++;
                    if (singleton) existing.Add(desired);
                    else
                    {
                        byKey[KeyOf(desired, keyFields)] = desired;
                        AddRef(table, desired, id);
                        if (!ReferenceEquals(baseRow, desired)) AddRef(table, baseRow, id);
                    }
                    continue;
                }

                var patch = new Entity(table, current.Id);
                foreach (var kv in desired.Attributes)
                {
                    if (IsEmpty(current, kv.Key)) patch[kv.Key] = kv.Value;
                }
                if (patch.Attributes.Count == 0) { skipped++; continue; }

                _svc.Update(patch);
                foreach (var kv in patch.Attributes) current[kv.Key] = kv.Value;
                if (!singleton) AddRef(table, current, current.Id); // a code may have just been filled
                filled++;
            }

            if (!singleton) KeepAutoNumberAhead(table, keyFields, meta, byKey.Values);

            _report.Created += created;
            _report.Filled += filled;
            _report.Skipped += skipped;
            _ctx.Trace("Seed {0}: created {1}, completed {2}, already set {3}.", table, created, filled, skipped);
            if (created + filled > 0)
                _report.Ok(table, string.Format(CultureInfo.InvariantCulture, "{0} created, {1} completed.", created, filled));
        }

        private static Entity TakeFallback(Dictionary<string, Entity> byFallback, Entity source, string field)
        {
            string f = NormValue(source.Contains(field) ? source[field] : null);
            Entity e;
            if (f == null || !byFallback.TryGetValue(f, out e)) return null;
            byFallback.Remove(f);
            return e;
        }

        /// <summary>
        /// The seed writes explicit codes (CST-1000...), but an autonumber column keeps its own
        /// counter, which explicit values do not move. Without this, the first row a firm creates
        /// would get a code the seed already used. Moves the counter past the highest code in use.
        /// </summary>
        private void KeepAutoNumberAhead(string table, List<string> keyFields, Dictionary<string, AttributeMetadata> meta, IEnumerable<Entity> rows)
        {
            if (keyFields.Count != 1) return;
            AttributeMetadata am;
            if (!meta.TryGetValue(keyFields[0], out am) || string.IsNullOrEmpty(am.AutoNumberFormat)) return;
            if (am.AutoNumberFormat.IndexOf("{SEQNUM:", StringComparison.OrdinalIgnoreCase) < 0) return;

            long max = -1;
            foreach (Entity e in rows)
            {
                string code = e.GetAttributeValue<string>(keyFields[0]);
                long n;
                if (TrailingNumber(code, out n) && n > max) max = n;
            }
            if (max < 0) return;

            var next = (GetNextAutoNumberValueResponse)_svc.Execute(new GetNextAutoNumberValueRequest
            {
                EntityName = table,
                AttributeName = keyFields[0]
            });
            if (next.NextAutoNumberValue > max) return;

            _svc.Execute(new SetAutoNumberSeedRequest { EntityName = table, AttributeName = keyFields[0], Value = max + 1 });
            _ctx.Trace("Autonumber {0}.{1} moved from {2} to {3}.", table, keyFields[0], next.NextAutoNumberValue, max + 1);
        }

        private static bool TrailingNumber(string code, out long n)
        {
            n = 0;
            if (string.IsNullOrEmpty(code)) return false;
            int i = code.Length;
            while (i > 0 && char.IsDigit(code[i - 1])) i--;
            return i < code.Length && long.TryParse(code.Substring(i), NumberStyles.None, CultureInfo.InvariantCulture, out n);
        }

        private Entity BuildEntity(string table, Dictionary<string, object> row, Dictionary<string, AttributeMetadata> meta, bool translate)
        {
            // Base-language values, with Spanish overrides when the organization is in Spanish.
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in row)
                if (!kv.Key.StartsWith("@", StringComparison.Ordinal)) values[kv.Key] = kv.Value;

            if (translate && _languageCode == SpanishLcid && row.ContainsKey("@i18n"))
            {
                var i18n = row["@i18n"] as Dictionary<string, object>;
                var es = i18n != null && i18n.ContainsKey("es") ? i18n["es"] as Dictionary<string, object> : null;
                if (es != null) foreach (var kv in es) values[kv.Key] = kv.Value;
            }

            var e = new Entity(table);
            foreach (var kv in values)
            {
                AttributeMetadata am;
                if (!meta.TryGetValue(kv.Key, out am)) continue; // already reported once per table
                object converted;
                if (TryConvert(table, kv.Key, kv.Value, am, out converted) && converted != null)
                    e[am.LogicalName] = converted;
            }
            return e;
        }

        // ------------------------------------------------------------------ values

        private bool TryConvert(string table, string field, object raw, AttributeMetadata am, out object result)
        {
            result = null;
            if (raw == null) return true;

            var obj = raw as Dictionary<string, object>;
            if (obj != null)
            {
                if (obj.ContainsKey("@ref")) return TryResolveRef(table, field, obj, out result);
                if (obj.ContainsKey("@runtime")) return TryRuntime(table, field, Str(obj, "@runtime"), am, out result);
                _report.Warn(table, "Column '" + field + "' has an unsupported seed value; it was skipped.");
                return false;
            }

            try
            {
                switch (am.AttributeType.GetValueOrDefault())
                {
                    case AttributeTypeCode.Picklist:
                    case AttributeTypeCode.State:
                    case AttributeTypeCode.Status:
                        result = new OptionSetValue(Convert.ToInt32(raw, CultureInfo.InvariantCulture));
                        return true;
                    case AttributeTypeCode.Integer:
                        result = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                        return true;
                    case AttributeTypeCode.BigInt:
                        result = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
                        return true;
                    case AttributeTypeCode.Decimal:
                        result = Convert.ToDecimal(raw, CultureInfo.InvariantCulture);
                        return true;
                    case AttributeTypeCode.Double:
                        result = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                        return true;
                    case AttributeTypeCode.Money:
                        result = new Money(Convert.ToDecimal(raw, CultureInfo.InvariantCulture));
                        return true;
                    case AttributeTypeCode.Boolean:
                        result = Convert.ToBoolean(raw, CultureInfo.InvariantCulture);
                        return true;
                    case AttributeTypeCode.String:
                    case AttributeTypeCode.Memo:
                        result = Convert.ToString(raw, CultureInfo.InvariantCulture);
                        return true;
                    case AttributeTypeCode.DateTime:
                        result = DateTime.Parse(Convert.ToString(raw, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                                                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
                        return true;
                    default:
                        _report.Warn(table, "Column '" + field + "' has type " + am.AttributeType + ", which the seed cannot write; it was skipped.");
                        return false;
                }
            }
            catch (Exception ex)
            {
                _report.Warn(table, "Column '" + field + "': value '" + raw + "' could not be converted (" + ex.Message + ").");
                return false;
            }
        }

        private bool TryRuntime(string table, string field, string name, AttributeMetadata am, out object result)
        {
            result = null;
            if (string.Equals(name, "installerTimeZone", StringComparison.OrdinalIgnoreCase))
            {
                if (am.AttributeType == AttributeTypeCode.Integer) { result = _timeZoneCode; return true; }
                _report.Warn(table, "Column '" + field + "' expects the installer time zone but is not a whole number; it was skipped.");
                return false;
            }
            _report.Warn(table, "Unknown runtime value '" + name + "' for column '" + field + "'.");
            return false;
        }

        private bool TryResolveRef(string table, string field, Dictionary<string, object> r, out object result)
        {
            result = null;
            string target = Str(r, "@ref");
            string key = Convert.ToString(r.ContainsKey("key") ? r["key"] : null, CultureInfo.InvariantCulture);
            bool optional = r.ContainsKey("optional") && r["optional"] is bool && (bool)r["optional"];
            string keyField = Str(r, "keyField");
            if (string.IsNullOrEmpty(keyField)) _tableKeyField.TryGetValue(target, out keyField);

            if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(keyField))
            {
                _report.Warn(table, "Column '" + field + "' has an incomplete reference; it was skipped.");
                return false;
            }

            Guid id;
            if (TryFindRef(target, keyField, key, out id))
            {
                result = new EntityReference(target, id);
                return true;
            }

            if (optional)
                _report.Info(table, "Optional reference " + target + " '" + key + "' was not found; '" + field + "' left empty.");
            else
                _report.Warn(table, "Reference " + target + " '" + key + "' was not found; '" + field + "' left empty.");
            return false;
        }

        private bool TryFindRef(string table, string keyField, string key, out Guid id)
        {
            Dictionary<string, Guid> cache;
            if (!_refCache.TryGetValue(table, out cache))
            {
                cache = new Dictionary<string, Guid>(StringComparer.Ordinal);
                _refCache[table] = cache;
            }
            string norm = Norm(key);
            if (cache.TryGetValue(norm, out id)) return true;

            // Not a seeded table, or a row created outside this run: look it up once.
            var q = new QueryExpression(table) { ColumnSet = new ColumnSet(keyField), TopCount = 1, NoLock = true };
            q.Criteria.AddCondition(keyField, ConditionOperator.Equal, key);
            EntityCollection found = _svc.RetrieveMultiple(q);
            if (found.Entities.Count == 0) { id = Guid.Empty; return false; }
            id = found.Entities[0].Id;
            cache[norm] = id;
            return true;
        }

        private void RegisterRefs(string table, List<Entity> rows)
        {
            string keyField;
            if (!_tableKeyField.TryGetValue(table, out keyField)) return;
            foreach (Entity e in rows) AddRef(table, e, e.Id);
        }

        private void AddRef(string table, Entity e, Guid id)
        {
            string keyField;
            if (!_tableKeyField.TryGetValue(table, out keyField)) return;
            string k = NormValue(e.Contains(keyField) ? e[keyField] : null);
            if (k == null) return;
            Dictionary<string, Guid> cache;
            if (!_refCache.TryGetValue(table, out cache))
            {
                cache = new Dictionary<string, Guid>(StringComparer.Ordinal);
                _refCache[table] = cache;
            }
            if (!cache.ContainsKey(k)) cache[k] = id;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Null when the table does not exist in this environment.</summary>
        private Dictionary<string, AttributeMetadata> Metadata(string table)
        {
            Dictionary<string, AttributeMetadata> m;
            return _meta.TryGetValue(table, out m) ? m : null;
        }

        /// <summary>
        /// One metadata query for all seeded tables. Unlike RetrieveEntity, a missing table simply
        /// does not come back (no failed call, so the transaction stays healthy).
        /// </summary>
        private void LoadMetadata(List<string> tables)
        {
            if (tables.Count == 0) return;
            var filter = new MetadataFilterExpression(LogicalOperator.And);
            filter.Conditions.Add(new MetadataConditionExpression("LogicalName", MetadataConditionOperator.In, tables.ToArray()));
            var query = new EntityQueryExpression
            {
                Criteria = filter,
                Properties = new MetadataPropertiesExpression("LogicalName", "Attributes"),
                AttributeQuery = new AttributeQueryExpression
                {
                    Properties = new MetadataPropertiesExpression("LogicalName", "AttributeType", "AutoNumberFormat")
                }
            };
            var resp = (RetrieveMetadataChangesResponse)_svc.Execute(new RetrieveMetadataChangesRequest
            {
                Query = query,
                ClientVersionStamp = null
            });
            foreach (EntityMetadata em in resp.EntityMetadata)
            {
                var m = new Dictionary<string, AttributeMetadata>(StringComparer.OrdinalIgnoreCase);
                if (em.Attributes != null)
                    foreach (AttributeMetadata a in em.Attributes) m[a.LogicalName] = a;
                _meta[em.LogicalName] = m;
            }
        }

        private List<Entity> LoadAll(string table, HashSet<string> columns)
        {
            var cols = new List<string>(columns);
            var q = new QueryExpression(table)
            {
                ColumnSet = new ColumnSet(cols.ToArray()),
                NoLock = true,
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            var all = new List<Entity>();
            while (true)
            {
                EntityCollection page = _svc.RetrieveMultiple(q);
                all.AddRange(page.Entities);
                if (!page.MoreRecords) break;
                q.PageInfo.PageNumber++;
                q.PageInfo.PagingCookie = page.PagingCookie;
            }
            return all;
        }

        private static List<string> KeyFields(object keyField)
        {
            var list = new List<string>();
            var s = keyField as string;
            if (s != null) { list.Add(s); return list; }
            var arr = keyField as List<object>;
            if (arr != null) foreach (object o in arr) if (o is string) list.Add((string)o);
            return list;
        }

        private static string KeyOf(Entity e, List<string> keyFields)
        {
            var parts = new List<string>();
            foreach (string f in keyFields)
            {
                string v = NormValue(e.Contains(f) ? e[f] : null);
                if (v == null) return null;
                parts.Add(v);
            }
            return parts.Count == 0 ? null : string.Join("|", parts.ToArray());
        }

        private static string NormValue(object v)
        {
            if (v == null) return null;
            var er = v as EntityReference;
            if (er != null) return er.Id.ToString("N");
            var os = v as OptionSetValue;
            if (os != null) return os.Value.ToString(CultureInfo.InvariantCulture);
            var money = v as Money;
            if (money != null) return money.Value.ToString(CultureInfo.InvariantCulture);
            string s = Convert.ToString(v, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(s) ? null : Norm(s);
        }

        private static string Norm(string s)
        {
            return s == null ? null : s.Trim().ToUpperInvariant();
        }

        private static bool IsEmpty(Entity e, string field)
        {
            if (!e.Contains(field) || e[field] == null) return true;
            var s = e[field] as string;
            return s != null && s.Trim().Length == 0;
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as string : null;
        }
    }
}
