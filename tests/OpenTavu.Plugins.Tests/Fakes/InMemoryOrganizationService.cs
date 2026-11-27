using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OpenTavu.Plugins.Tests.Fakes
{
    /// <summary>
    /// An in-memory Dataverse for plugin tests: rows by table and id, and the QueryExpression subset the Core plugins
    /// use (and/or filters, the common condition operators, one level of inner or outer LinkEntity, ordering, TopCount,
    /// and paging with PageInfo and a paging cookie). Anything else throws NotSupportedException, so a test never
    /// passes on a query the fake does not understand. String comparisons ignore case, as Dataverse's collation does.
    /// </summary>
    public sealed class InMemoryOrganizationService : IOrganizationService
    {
        private readonly Dictionary<string, Dictionary<Guid, Entity>> _tables = new Dictionary<string, Dictionary<Guid, Entity>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The clock for createdon and modifiedon.</summary>
        public DateTime UtcNow { get; set; } = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>Every Create, Update and Delete, in order ("Update tavu_lead"), so a test can assert what was written.</summary>
        public List<string> Writes { get; } = new List<string>();

        /// <summary>Adds a row as it would already be in the environment; not counted in <see cref="Writes"/>.</summary>
        public Guid Seed(Entity row)
        {
            Guid id = row.Id == Guid.Empty ? Guid.NewGuid() : row.Id;
            Entity copy = Copy(row);
            copy.Id = id;
            copy[row.LogicalName + "id"] = id;
            if (!copy.Contains("statecode")) copy["statecode"] = new OptionSetValue(0);
            if (!copy.Contains("createdon")) copy["createdon"] = UtcNow;
            if (!copy.Contains("modifiedon")) copy["modifiedon"] = copy["createdon"];
            Table(row.LogicalName)[id] = copy;
            return id;
        }

        public IEnumerable<Entity> Rows(string table) => Table(table).Values.Select(Copy);

        public Guid Create(Entity entity)
        {
            if (entity.Id != Guid.Empty && Table(entity.LogicalName).ContainsKey(entity.Id))
                throw new InvalidOperationException("A " + entity.LogicalName + " with id " + entity.Id + " already exists.");
            Entity row = Copy(entity);
            row.Attributes.Remove("createdon");
            row.Attributes.Remove("modifiedon");
            Guid id = Seed(row);
            Writes.Add("Create " + entity.LogicalName);
            return id;
        }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            if (!Table(entityName).TryGetValue(id, out Entity row))
                throw new InvalidOperationException(entityName + " with id " + id + " does not exist.");
            return Project(row, columnSet);
        }

        public void Update(Entity entity)
        {
            if (!Table(entity.LogicalName).TryGetValue(entity.Id, out Entity row))
                throw new InvalidOperationException(entity.LogicalName + " with id " + entity.Id + " does not exist.");
            foreach (KeyValuePair<string, object> a in entity.Attributes) row[a.Key] = a.Value;
            row["modifiedon"] = UtcNow;
            Writes.Add("Update " + entity.LogicalName);
        }

        public void Delete(string entityName, Guid id)
        {
            if (!Table(entityName).Remove(id))
                throw new InvalidOperationException(entityName + " with id " + id + " does not exist.");
            Writes.Add("Delete " + entityName);
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var q = query as QueryExpression;
            if (q == null) throw new NotSupportedException("The in-memory service supports QueryExpression only, not " + query.GetType().Name + ".");
            Validate(q.Criteria);
            foreach (LinkEntity link in q.LinkEntities) Validate(link.LinkCriteria);

            IEnumerable<Entity> rows = Table(q.EntityName).Values.Where(r => Matches(r, q.Criteria));
            foreach (LinkEntity link in q.LinkEntities) rows = Join(rows, link);

            List<Entity> list = rows.ToList();
            foreach (OrderExpression order in q.Orders.Reverse())
            {
                OrderExpression o = order;
                list = (o.OrderType == OrderType.Descending
                    ? list.OrderByDescending(r => Sortable(r, o.AttributeName))
                    : list.OrderBy(r => Sortable(r, o.AttributeName))).ToList();
            }

            var result = new EntityCollection { EntityName = q.EntityName };
            if (q.TopCount.HasValue) list = list.Take(q.TopCount.Value).ToList();
            else if (q.PageInfo != null && q.PageInfo.Count > 0)
            {
                int page = Math.Max(1, q.PageInfo.PageNumber);
                int skip = (page - 1) * q.PageInfo.Count;
                result.MoreRecords = list.Count > skip + q.PageInfo.Count;
                result.PagingCookie = result.MoreRecords ? "<cookie page=\"" + page.ToString(CultureInfo.InvariantCulture) + "\" />" : null;
                list = list.Skip(skip).Take(q.PageInfo.Count).ToList();
            }
            foreach (Entity r in list) result.Entities.Add(Project(r, q.ColumnSet));
            return result;
        }

        /// <summary>
        /// Hours from UTC of the time zone codes a test uses, for LocalTimeFromUtcTime and UtcTimeFromLocalTime (fixed
        /// offsets, no daylight saving): 92 UTC, 45 Bogota, 105 Madrid in winter.
        /// </summary>
        public Dictionary<int, int> TimeZoneHours { get; } = new Dictionary<int, int> { [92] = 0, [45] = -5, [105] = 1 };

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            switch (request.RequestName)
            {
                case "LocalTimeFromUtcTime":
                {
                    var utc = (DateTime)request["UtcTime"];
                    var response = new LocalTimeFromUtcTimeResponse();
                    response.Results["LocalTime"] = DateTime.SpecifyKind(utc.AddHours(Hours(request)), DateTimeKind.Unspecified);
                    return response;
                }
                case "UtcTimeFromLocalTime":
                {
                    var local = (DateTime)request["LocalTime"];
                    var response = new UtcTimeFromLocalTimeResponse();
                    response.Results["UtcTime"] = DateTime.SpecifyKind(local.AddHours(-Hours(request)), DateTimeKind.Utc);
                    return response;
                }
                default:
                    throw new NotSupportedException("The in-memory service does not execute " + request.RequestName + ".");
            }
        }

        private int Hours(OrganizationRequest request)
        {
            int code = (int)request["TimeZoneCode"];
            if (!TimeZoneHours.TryGetValue(code, out int hours))
                throw new NotSupportedException("The in-memory service has no offset for time zone " + code + ".");
            return hours;
        }

        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new NotSupportedException("Associate is not supported.");

        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new NotSupportedException("Disassociate is not supported.");

        // ------------------------------------------------------------------ query evaluation

        private Dictionary<Guid, Entity> Table(string name)
        {
            if (!_tables.TryGetValue(name, out Dictionary<Guid, Entity> t)) _tables[name] = t = new Dictionary<Guid, Entity>();
            return t;
        }

        private IEnumerable<Entity> Join(IEnumerable<Entity> rows, LinkEntity link)
        {
            if (link.LinkEntities.Count > 0) throw new NotSupportedException("Nested LinkEntity is not supported.");
            if (link.JoinOperator != JoinOperator.Inner && link.JoinOperator != JoinOperator.LeftOuter)
                throw new NotSupportedException("JoinOperator " + link.JoinOperator + " is not supported.");
            foreach (Entity row in rows)
            {
                object from = Comparable(row.Contains(link.LinkFromAttributeName) ? row[link.LinkFromAttributeName] : null);
                List<Entity> matches = Table(link.LinkToEntityName).Values
                    .Where(t => Equals(Comparable(t.Contains(link.LinkToAttributeName) ? t[link.LinkToAttributeName] : null), from) && Matches(t, link.LinkCriteria))
                    .ToList();
                if (matches.Count == 0)
                {
                    if (link.JoinOperator == JoinOperator.LeftOuter) yield return row;
                    continue;
                }
                foreach (Entity m in matches)
                {
                    Entity joined = Copy(row);
                    string alias = string.IsNullOrEmpty(link.EntityAlias) ? link.LinkToEntityName : link.EntityAlias;
                    IEnumerable<string> cols = link.Columns.AllColumns ? m.Attributes.Keys : link.Columns.Columns;
                    foreach (string c in cols)
                        if (m.Contains(c)) joined[alias + "." + c] = new AliasedValue(link.LinkToEntityName, c, m[c]);
                    yield return joined;
                }
            }
        }

        private static readonly HashSet<ConditionOperator> Supported = new HashSet<ConditionOperator>
        {
            ConditionOperator.Null, ConditionOperator.NotNull, ConditionOperator.Equal, ConditionOperator.NotEqual,
            ConditionOperator.In, ConditionOperator.NotIn, ConditionOperator.LessThan, ConditionOperator.LessEqual,
            ConditionOperator.GreaterThan, ConditionOperator.GreaterEqual, ConditionOperator.BeginsWith,
        };

        /// <summary>Rejects an unsupported operator before any row is read, so an empty table cannot hide it.</summary>
        private static void Validate(FilterExpression filter)
        {
            if (filter == null) return;
            foreach (ConditionExpression c in filter.Conditions)
            {
                if (!Supported.Contains(c.Operator)) throw new NotSupportedException("ConditionOperator " + c.Operator + " is not supported.");
                if (!string.IsNullOrEmpty(c.EntityName)) throw new NotSupportedException("Conditions on a linked table's alias are not supported.");
            }
            foreach (FilterExpression f in filter.Filters) Validate(f);
        }

        private static bool Matches(Entity row, FilterExpression filter)
        {
            if (filter == null) return true;
            var results = new List<bool>();
            foreach (ConditionExpression c in filter.Conditions) results.Add(Matches(row, c));
            foreach (FilterExpression f in filter.Filters) results.Add(Matches(row, f));
            if (results.Count == 0) return true;
            return filter.FilterOperator == LogicalOperator.Or ? results.Any(r => r) : results.All(r => r);
        }

        private static bool Matches(Entity row, ConditionExpression c)
        {
            if (!string.IsNullOrEmpty(c.EntityName)) throw new NotSupportedException("Conditions on a linked table's alias are not supported.");
            object value = Comparable(row.Contains(c.AttributeName) ? row[c.AttributeName] : null);
            List<object> args = c.Values.Select(Comparable).ToList();
            switch (c.Operator)
            {
                case ConditionOperator.Null: return value == null;
                case ConditionOperator.NotNull: return value != null;
                case ConditionOperator.Equal: return value != null && Compare(value, args[0]) == 0;
                // As in Dataverse, ne and not-in never match a null column; ask for null explicitly.
                case ConditionOperator.NotEqual: return value != null && Compare(value, args[0]) != 0;
                case ConditionOperator.In: return value != null && args.Any(a => Compare(value, a) == 0);
                case ConditionOperator.NotIn: return value != null && args.All(a => Compare(value, a) != 0);
                case ConditionOperator.LessThan: return value != null && Compare(value, args[0]) < 0;
                case ConditionOperator.LessEqual: return value != null && Compare(value, args[0]) <= 0;
                case ConditionOperator.GreaterThan: return value != null && Compare(value, args[0]) > 0;
                case ConditionOperator.GreaterEqual: return value != null && Compare(value, args[0]) >= 0;
                case ConditionOperator.BeginsWith:
                    return value is string s && s.StartsWith((string)args[0], StringComparison.OrdinalIgnoreCase);
                default: throw new NotSupportedException("ConditionOperator " + c.Operator + " is not supported.");
            }
        }

        /// <summary>Option sets, lookups and money compare by their value; numbers as decimals.</summary>
        private static object Comparable(object v)
        {
            switch (v)
            {
                case null: return null;
                case OptionSetValue o: return (decimal)o.Value;
                case EntityReference r: return r.Id;
                case Money m: return m.Value;
                case AliasedValue a: return Comparable(a.Value);
                case int i: return (decimal)i;
                case long l: return (decimal)l;
                case double d: return (decimal)d;
                case float f: return (decimal)f;
                case decimal m2: return m2;
                case DateTime t: return t.ToUniversalTime();
                case Enum e: return (decimal)Convert.ToInt32(e, CultureInfo.InvariantCulture);
                default: return v;
            }
        }

        private static int Compare(object a, object b)
        {
            if (a is string sa && b is string sb) return string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
            if (a.GetType() != b.GetType()) throw new NotSupportedException("Cannot compare " + a.GetType().Name + " with " + b.GetType().Name + ".");
            return ((IComparable)a).CompareTo(b);
        }

        private static object Sortable(Entity row, string attribute) => Comparable(row.Contains(attribute) ? row[attribute] : null);

        private static Entity Project(Entity row, ColumnSet columns)
        {
            var e = new Entity(row.LogicalName, row.Id);
            IEnumerable<string> keys = columns == null || columns.AllColumns
                ? row.Attributes.Keys.ToList()
                : columns.Columns.Concat(row.Attributes.Keys.Where(k => k.Contains("."))).ToList();
            foreach (string k in keys) if (row.Contains(k)) e[k] = row[k];
            return e;
        }

        private static Entity Copy(Entity row)
        {
            var e = new Entity(row.LogicalName, row.Id);
            foreach (KeyValuePair<string, object> a in row.Attributes) e[a.Key] = a.Value;
            return e;
        }
    }
}
