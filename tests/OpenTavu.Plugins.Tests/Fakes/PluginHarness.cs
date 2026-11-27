using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace OpenTavu.Plugins.Tests.Fakes
{
    /// <summary>
    /// Runs a plugin through IPlugin.Execute, as the Dataverse sandbox does: an execution context, a service factory that
    /// returns <see cref="Service"/> for the user and the system, and a tracing service whose lines a test can read.
    /// Every write also lands in <see cref="WritesBy"/> with the service that made it ("user Update tavu_lead" or
    /// "system Create tavu_salesperiod"), so a test can assert which privileges a write runs with.
    /// </summary>
    public sealed class PluginHarness : IServiceProvider, IOrganizationServiceFactory, ITracingService
    {
        public InMemoryOrganizationService Service { get; } = new InMemoryOrganizationService();
        public FakePluginContext Context { get; } = new FakePluginContext();
        public List<string> Trace { get; } = new List<string>();
        public List<string> WritesBy { get; } = new List<string>();

        public ParameterCollection Run(IPlugin plugin, string message, IDictionary<string, object> inputs = null)
        {
            Context.MessageName = message;
            Context.InputParameters.Clear();
            Context.OutputParameters.Clear();
            if (inputs != null) foreach (KeyValuePair<string, object> i in inputs) Context.InputParameters[i.Key] = i.Value;
            plugin.Execute(this);
            // A copy, so the outputs of one run stay readable after the next.
            var outputs = new ParameterCollection();
            foreach (KeyValuePair<string, object> o in Context.OutputParameters) outputs[o.Key] = o.Value;
            return outputs;
        }

        object IServiceProvider.GetService(Type serviceType)
        {
            if (serviceType == typeof(IPluginExecutionContext)) return Context;
            if (serviceType == typeof(IOrganizationServiceFactory)) return this;
            if (serviceType == typeof(ITracingService)) return this;
            return null;
        }

        IOrganizationService IOrganizationServiceFactory.CreateOrganizationService(Guid? userId) =>
            new RecordingService(Service, userId.HasValue ? "user" : "system", WritesBy);

        void ITracingService.Trace(string format, params object[] args) =>
            Trace.Add(args == null || args.Length == 0 ? format : string.Format(format, args));
    }

    /// <summary>A plugin execution context with settable values; depth 1, stage 30 (main operation of a Custom API).</summary>
    public sealed class FakePluginContext : IPluginExecutionContext
    {
        public int Mode { get; set; }
        public int IsolationMode { get; set; } = 2;
        public int Depth { get; set; } = 1;
        public string MessageName { get; set; }
        public string PrimaryEntityName { get; set; } = "none";
        public Guid? RequestId { get; set; } = Guid.NewGuid();
        public string SecondaryEntityName { get; set; } = "none";
        public ParameterCollection InputParameters { get; } = new ParameterCollection();
        public ParameterCollection OutputParameters { get; } = new ParameterCollection();
        public ParameterCollection SharedVariables { get; } = new ParameterCollection();
        public Guid UserId { get; set; } = Guid.NewGuid();
        public Guid InitiatingUserId { get; set; } = Guid.NewGuid();
        public Guid BusinessUnitId { get; set; } = Guid.NewGuid();
        public Guid OrganizationId { get; set; } = Guid.NewGuid();
        public string OrganizationName { get; set; } = "test";
        public Guid PrimaryEntityId { get; set; }
        public EntityImageCollection PreEntityImages { get; } = new EntityImageCollection();
        public EntityImageCollection PostEntityImages { get; } = new EntityImageCollection();
        public EntityReference OwningExtension { get; set; }
        public Guid CorrelationId { get; set; } = Guid.NewGuid();
        public bool IsExecutingOffline { get; set; }
        public bool IsOfflinePlayback { get; set; }
        public bool IsInTransaction { get; set; } = true;
        public Guid OperationId { get; set; } = Guid.NewGuid();
        public DateTime OperationCreatedOn { get; set; } = DateTime.UtcNow;
        public int Stage { get; set; } = 30;
        public IPluginExecutionContext ParentContext { get; set; }
    }

    /// <summary>The in-memory service seen through the user or the system service: records who wrote what.</summary>
    internal sealed class RecordingService : IOrganizationService
    {
        private readonly IOrganizationService _inner;
        private readonly string _who;
        private readonly List<string> _log;

        public RecordingService(IOrganizationService inner, string who, List<string> log)
        {
            _inner = inner;
            _who = who;
            _log = log;
        }

        public Guid Create(Entity entity)
        {
            _log.Add(_who + " Create " + entity.LogicalName);
            return _inner.Create(entity);
        }

        public void Update(Entity entity)
        {
            _log.Add(_who + " Update " + entity.LogicalName);
            _inner.Update(entity);
        }

        public void Delete(string entityName, Guid id)
        {
            _log.Add(_who + " Delete " + entityName);
            _inner.Delete(entityName, id);
        }

        public Entity Retrieve(string entityName, Guid id, Microsoft.Xrm.Sdk.Query.ColumnSet columnSet) => _inner.Retrieve(entityName, id, columnSet);
        public EntityCollection RetrieveMultiple(Microsoft.Xrm.Sdk.Query.QueryBase query) => _inner.RetrieveMultiple(query);
        public OrganizationResponse Execute(OrganizationRequest request) => _inner.Execute(request);
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            _inner.Associate(entityName, entityId, relationship, relatedEntities);
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            _inner.Disassociate(entityName, entityId, relationship, relatedEntities);
    }
}
