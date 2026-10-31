using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace OpenTavu.Plugins.Tests.Fakes
{
    /// <summary>
    /// Runs a plugin through IPlugin.Execute, as the Dataverse sandbox does: an execution context, a service factory that
    /// returns <see cref="Service"/> for the user and the system, and a tracing service whose lines a test can read.
    /// </summary>
    public sealed class PluginHarness : IServiceProvider, IOrganizationServiceFactory, ITracingService
    {
        public InMemoryOrganizationService Service { get; } = new InMemoryOrganizationService();
        public FakePluginContext Context { get; } = new FakePluginContext();
        public List<string> Trace { get; } = new List<string>();

        public ParameterCollection Run(IPlugin plugin, string message, IDictionary<string, object> inputs = null)
        {
            Context.MessageName = message;
            Context.InputParameters.Clear();
            Context.OutputParameters.Clear();
            if (inputs != null) foreach (KeyValuePair<string, object> i in inputs) Context.InputParameters[i.Key] = i.Value;
            plugin.Execute(this);
            return Context.OutputParameters;
        }

        object IServiceProvider.GetService(Type serviceType)
        {
            if (serviceType == typeof(IPluginExecutionContext)) return Context;
            if (serviceType == typeof(IOrganizationServiceFactory)) return this;
            if (serviceType == typeof(ITracingService)) return this;
            return null;
        }

        IOrganizationService IOrganizationServiceFactory.CreateOrganizationService(Guid? userId) => Service;

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
}
