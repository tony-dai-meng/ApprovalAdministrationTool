using System;
using System.Collections.Generic;

namespace ApprovalAdministrationTool.Models
{
    /// <summary>
    /// Columns whose stored value is not readable on its own (a choice's number, a lookup's ID, or a table name).
    /// The grid shows these with a friendly value by default and a switch icon in the header that shows the value
    /// stored in Dataverse instead.
    /// </summary>
    internal static class ApprovalColumnValues
    {
        /// <summary>Suffix of the extra table column that holds a column's friendly values.</summary>
        public const string FriendlySuffix = "__friendly";

        /// <summary>
        /// Key of a table column's ExtendedProperties entry holding, for choice and Yes/No columns, each option's label and
        /// its stored value (an IDictionary&lt;string, string&gt;, e.g. "Pending" =&gt; "192350001", "Yes" =&gt; "1").
        /// </summary>
        public const string OptionValuesProperty = "OptionValues";

        /// <summary>Columns shown with friendly values and a switch to their stored values.</summary>
        public static readonly ISet<string> WithFriendlyValues = new HashSet<string>
        {
            "ownerid",                         // lookup (stored value is the user's or team's ID)
            ApprovalColumns.CurrentlyWith,     // calculated list of approvers (stored values are their IDs)
            "msdyn_flow_approval_modeltype",   // table name of the approval model
            "msdyn_flow_approval_requesttype", // choice
            "msdyn_flow_approval_priority",    // choice
            "msdyn_flow_approval_stage",       // choice
            "currentstage",                    // lookup (stored value is the stage record's ID)
            "statuscode",                      // choice
        };

        /// <summary>
        /// Friendly names for stored values that Dataverse has no label for. Choices and lookups get their friendly value
        /// from Dataverse itself (the label or record name it returns with the data).
        /// </summary>
        public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> FriendlyNames =
            new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                // Approval Model Type stores the name of the approval-model table. Microsoft does not document what each
                // value means; these names are inferred from the table names and the approval types offered in a flow
                // (see the column's definition in ApprovalColumnDefinitions).
                ["msdyn_flow_approval_modeltype"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["msdyn_flow_basicapprovalmodel"] = "Approve/Reject – First to respond",
                    ["msdyn_flow_awaitallapprovalmodel"] = "Approve/Reject – Everyone must approve",
                    ["msdyn_flow_actionapprovalmodel"] = "Custom responses – Wait for one response",
                    ["msdyn_flow_awaitallactionapprovalmodel"] = "Custom responses – Wait for all responses",
                    ["msdyn_flow_sequentialapprovalmodel"] = "Sequential approval",
                },
            };
    }
}
