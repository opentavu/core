using System;
using Microsoft.Xrm.Sdk;

namespace OpenTavu.Plugins.Common
{
	/// <summary>
	/// Abstract base class for all OpenTavu plugins.
	/// Handles service extraction, tracing setup, infinite-loop protection,
	/// and centralized error handling so concrete plugins only implement
	/// business logic in ExecuteInternal.
	/// </summary>
	/// <remarks>
	/// Inheritance pattern:
	///   public class MyPlugin : PluginBase
	///   {
	///       public MyPlugin() : base(typeof(MyPlugin)) { }
	///       protected override void ExecuteInternal(LocalPluginContext localContext)
	///       {
	///           // business logic here
	///       }
	///   }
	/// </remarks>
	public abstract class PluginBase : IPlugin
	{
		/// <summary>
		/// Type of the concrete plugin. Used to prefix log messages
		/// and identify the plugin in Plugin Trace Log records.
		/// </summary>
		protected Type ChildClassType { get; }

		/// <summary>
		/// Maximum depth allowed before the plugin self-aborts.
		/// Dataverse's hard limit is 8; OpenTavu uses 1 by default
		/// because lifecycle/audit plugins must never recurse.
		/// Override in derived class if a plugin legitimately needs deeper chains.
		/// </summary>
		protected virtual int MaxDepth => 1;

		protected PluginBase(Type childClassType)
		{
			ChildClassType = childClassType
				?? throw new ArgumentNullException(nameof(childClassType));
		}

		/// <summary>
		/// Entry point invoked by the Dataverse pipeline. Sealed — do not override.
		/// Performs setup, depth check, error handling and delegates to ExecuteInternal.
		/// </summary>
		public void Execute(IServiceProvider serviceProvider)
		{
			if (serviceProvider == null)
				throw new ArgumentNullException(nameof(serviceProvider));

			// Tracing is set up first because we want to log even early failures.
			var tracingService = (ITracingService)serviceProvider
				.GetService(typeof(ITracingService));

			tracingService.Trace("{0}: Execute started", ChildClassType.Name);

			try
			{
				var pluginContext = (IPluginExecutionContext)serviceProvider
					.GetService(typeof(IPluginExecutionContext));

				// Infinite-loop guard: if this plugin's own update triggers itself,
				// depth grows. Abort silently when we exceed the configured maximum.
				if (pluginContext.Depth > MaxDepth)
				{
					tracingService.Trace(
						"{0}: Depth {1} exceeds MaxDepth {2}. Aborting to prevent recursion.",
						ChildClassType.Name,
						pluginContext.Depth,
						MaxDepth);
					return;
				}

				var serviceFactory = (IOrganizationServiceFactory)serviceProvider
					.GetService(typeof(IOrganizationServiceFactory));

				var userService = serviceFactory.CreateOrganizationService(
					pluginContext.UserId);
				var systemService = serviceFactory.CreateOrganizationService(null);

				var localContext = new LocalPluginContext(
					pluginContext, userService, systemService, tracingService);

				// Delegate to the concrete plugin's business logic.
				ExecuteInternal(localContext);

				tracingService.Trace("{0}: Execute completed successfully",
					ChildClassType.Name);
			}
			catch (InvalidPluginExecutionException)
			{
				// Already a Dataverse-friendly exception — rethrow as-is so the
				// user sees the message without "An unexpected error occurred".
				throw;
			}
			catch (Exception ex)
			{
				// Wrap any other exception so Dataverse surfaces it cleanly.
				tracingService.Trace(
					"{0}: Unhandled exception. {1}",
					ChildClassType.Name,
					ex.ToString());

				throw new InvalidPluginExecutionException(
					string.Format(
						"An error occurred in {0}. Please check the Plugin Trace Log " +
						"for details. Message: {1}",
						ChildClassType.Name,
						ex.Message),
					ex);
			}
		}

		/// <summary>
		/// Business logic implemented by each concrete plugin.
		/// Receives a fully-built LocalPluginContext — no service extraction needed.
		/// </summary>
		protected abstract void ExecuteInternal(LocalPluginContext localContext);
	}
}