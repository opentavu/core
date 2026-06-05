using System;
using Microsoft.Xrm.Sdk;
using OpenTavu.Dataverse.Common;

namespace Pl.Opportunity.CustomerSync
{
	/// <summary>
	/// Mirrors the polymorphic tavu_customerid lookup to the typed lookups
	/// tavu_accountid / tavu_contactid, and validates the customer entity type
	/// against the tenant-wide Customer Mode (B2B Only / B2C Only / Mixed)
	/// stored in the tavu_systemsettings singleton.
	///
	/// Runs server-side so the rule applies to every write path:
	/// UI, imports, Power Automate, API, other apps.
	/// </summary>
	/// <remarks>
	/// Plugin Registration:
	///   Message:              Create AND Update (register one step per message)
	///   Primary Entity:       tavu_opportunity
	///   Filtering Attributes: tavu_customerid    (Update step only)
	///   Stage:                20 (Pre-operation)
	///   Execution Mode:       Synchronous
	///   Deployment:           Server
	/// </remarks>
	public class CustomerSync : PluginBase
	{
		// ----- Schema constants -----
		private const string TargetEntityName = "tavu_opportunity";

		private const string AttrCustomer = "tavu_customer";
		private const string AttrAccount = "tavu_account";
		private const string AttrContact = "tavu_contact";

		private const string EntityAccount = "account";
		private const string EntityContact = "contact";

		private const string SettingsEntityName = "tavu_systemsettings";
		private const string AttrCustomerMode = "tavu_customermode";

		// tavu_customermode option set values
		private const int MODE_B2B_ONLY = 576600000;
		private const int MODE_B2C_ONLY = 576600001;
		private const int MODE_MIXED = 576600002;

		public CustomerSync() : base(typeof(CustomerSync)) { }

		protected override void ExecuteInternal(LocalPluginContext localContext)
		{
			if (localContext == null)
				throw new ArgumentNullException(nameof(localContext));

			localContext.Trace("CustomerSync: ExecuteInternal entered.");

			if (!(localContext.PluginExecutionContext.InputParameters.Contains("Target")
				  && localContext.PluginExecutionContext
								 .InputParameters["Target"] is Entity target))
			{
				localContext.Trace("Target missing or not an Entity. Exiting.");
				return;
			}

			if (!string.Equals(target.LogicalName, TargetEntityName, StringComparison.Ordinal))
			{
				localContext.Trace(
					"Unexpected entity '{0}'. Only '{1}' supported. Exiting.",
					target.LogicalName, TargetEntityName);
				return;
			}

			localContext.Trace("Target acquired. Id={0}", target.Id);

			HandleCustomerMirroring(localContext, target);

			localContext.Trace("CustomerSync: ExecuteInternal exiting.");
		}

		/// <summary>
		/// If tavu_customerid is part of the update, validates its entity type
		/// against the current Customer Mode and mirrors the value to the
		/// matching typed lookup (account or contact). The opposite lookup
		/// is cleared so only one is ever populated.
		/// </summary>
		private void HandleCustomerMirroring(LocalPluginContext localContext, Entity target)
		{
			localContext.Trace("HandleCustomerMirroring: entered.");

			// On Update, tavu_customerid is only present if it was actually changed.
			// On Create, it may be present or absent depending on input.
			if (!target.Contains(AttrCustomer))
			{
				localContext.Trace("tavu_customerid not in Target. Skipping.");
				return;
			}

			var customerRef = target.GetAttributeValue<EntityReference>(AttrCustomer);

			// Customer cleared: null both typed lookups in the same write.
			if (customerRef == null)
			{
				localContext.Trace("Customer cleared. Nulling typed lookups.");
				target[AttrAccount] = null;
				target[AttrContact] = null;
				return;
			}

			localContext.Trace(
				"Customer set. Type='{0}', Id={1}",
				customerRef.LogicalName, customerRef.Id);

			// Validate against Customer Mode before mirroring.
			int mode = GetCustomerMode(localContext);

			if (!IsCustomerTypeAllowed(customerRef.LogicalName, mode))
			{
				string message = BuildRejectionMessage(customerRef.LogicalName, mode);
				localContext.Trace("Customer type rejected by mode {0}. Message: {1}", mode, message);
				throw new InvalidPluginExecutionException(message);
			}

			// Mirror to the matching typed lookup, clear the opposite.
			if (string.Equals(customerRef.LogicalName, EntityAccount, StringComparison.Ordinal))
			{
				target[AttrAccount] = new EntityReference(EntityAccount, customerRef.Id);
				target[AttrContact] = null;
				localContext.Trace("Mirrored to tavu_accountid. tavu_contactid cleared.");
			}
			else if (string.Equals(customerRef.LogicalName, EntityContact, StringComparison.Ordinal))
			{
				target[AttrContact] = new EntityReference(EntityContact, customerRef.Id);
				target[AttrAccount] = null;
				localContext.Trace("Mirrored to tavu_contactid. tavu_accountid cleared.");
			}
			else
			{
				// Customer lookup natively only accepts account/contact, so this is unreachable
				// under normal Dataverse behavior. Trace it just in case.
				localContext.Trace(
					"Unexpected customer type '{0}'. Not mirroring.",
					customerRef.LogicalName);
			}

			localContext.Trace("HandleCustomerMirroring: exiting.");
		}

		/// <summary>
		/// Reads tavu_customermode from the tavu_systemsettings singleton row.
		/// Falls back to MIXED (most permissive) if the row is missing or the
		/// value is null, so the plugin never blocks a write due to misconfiguration.
		/// </summary>
		private int GetCustomerMode(LocalPluginContext localContext)
		{
			var query = new Microsoft.Xrm.Sdk.Query.QueryExpression(SettingsEntityName)
			{
				ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(AttrCustomerMode),
				TopCount = 1
			};

			var result = localContext.SystemService.RetrieveMultiple(query);

			if (result.Entities.Count == 0)
			{
				localContext.Trace("No tavu_systemsettings row. Defaulting to MIXED.");
				return MODE_MIXED;
			}

			var modeValue = result.Entities[0].GetAttributeValue<OptionSetValue>(AttrCustomerMode);
			if (modeValue == null)
			{
				localContext.Trace("tavu_customermode is null. Defaulting to MIXED.");
				return MODE_MIXED;
			}

			localContext.Trace("Customer Mode resolved: {0}", modeValue.Value);
			return modeValue.Value;
		}

		private bool IsCustomerTypeAllowed(string entityType, int mode)
		{
			if (mode == MODE_MIXED) return true;
			if (mode == MODE_B2B_ONLY) return entityType == EntityAccount;
			if (mode == MODE_B2C_ONLY) return entityType == EntityContact;
			return true; // unknown mode: permissive
		}

		private string BuildRejectionMessage(string attemptedType, int mode)
		{
			string typeLabel = attemptedType == EntityAccount ? "an Account" : "a Contact";
			string modeLabel = mode == MODE_B2B_ONLY ? "B2B Only" : "B2C Only";
			string allowedLabel = mode == MODE_B2B_ONLY ? "Accounts" : "Contacts";

			return string.Format(
				"This system is configured in {0} mode and only allows {1} as customers. " +
				"You attempted to set {2}. Please select a {1} customer.",
				modeLabel, allowedLabel, typeLabel);
		}
	}
}