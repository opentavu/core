using System;
using Microsoft.Xrm.Sdk;

namespace OpenTavu.Dataverse.Common
{
    /// <summary>
    /// Bundles all services and context information needed during plugin execution.
    /// Acts as a single parameter for ExecuteInternal, replacing the need for
    /// each plugin to extract services from IServiceProvider manually.
    /// </summary>
    public class LocalPluginContext
    {
        /// <summary>
        /// Plugin execution context provided by the Dataverse pipeline.
        /// Contains the Target entity, message name, stage, depth, and shared variables.
        /// </summary>
        public IPluginExecutionContext PluginExecutionContext { get; }

        /// <summary>
        /// Organization service bound to the user that triggered the operation.
        /// Use this when actions should respect the calling users security privileges.
        /// </summary>
        public IOrganizationService UserService { get; }

        /// <summary>
        /// Organization service running under SYSTEM privileges.
        /// Use this only when bypassing security is justified
        /// (e.g., updating audit fields the user cannot directly write).
        /// </summary>
        public IOrganizationService SystemService { get; }

        /// <summary>
        /// Tracing service for diagnostic logs.
        /// Output appears in the PluginTraceLog table, always trace key checkpoints.
        /// </summary>
        public ITracingService TracingService { get; }

        public LocalPluginContext(
            IPluginExecutionContext pluginContext,
            IOrganizationService userService,
            IOrganizationService systemService,
            ITracingService tracingService)
        {
            PluginExecutionContext = pluginContext
                ?? throw new ArgumentNullException(nameof(pluginContext));
            UserService = userService
                ?? throw new ArgumentNullException(nameof(userService));
            SystemService = systemService
                ?? throw new ArgumentNullException(nameof(systemService));
            TracingService = tracingService
                ?? throw new ArgumentNullException(nameof(tracingService));
        }

        /// <summary>
        /// Helper to write a traced message with a consistent format.
        /// Prepends the plugin step name (Message + Stage + Depth) for easier log scanning.
        /// </summary>
        /// <param name="message">Plain message to log.</param>
        public void Trace(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            TracingService.Trace(
                "[{0} - Stage:{1} - Depth:{2}] {3}",
                PluginExecutionContext.MessageName,
                PluginExecutionContext.Stage,
                PluginExecutionContext.Depth,
                message);
        }

        /// <summary>
        /// Helper to write a traced message with a format string and arguments,
        /// mirroring the signature of ITracingService.Trace(format, args).
        /// Prepends the plugin step name (Message + Stage + Depth) for easier log scanning.
        /// </summary>
        /// <param name="format">Composite format string (e.g., "Id={0}, Name={1}").</param>
        /// <param name="args">Arguments referenced by the format placeholders.</param>
        public void Trace(string format, params object[] args)
        {
            if (string.IsNullOrEmpty(format)) return;

            string formattedMessage = (args == null || args.Length == 0)
                ? format
                : string.Format(format, args);

            TracingService.Trace(
                "[{0} - Stage:{1} - Depth:{2}] {3}",
                PluginExecutionContext.MessageName,
                PluginExecutionContext.Stage,
                PluginExecutionContext.Depth,
                formattedMessage);
        }
    }
}