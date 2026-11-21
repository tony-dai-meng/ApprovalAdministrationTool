using System.Collections.Generic;

namespace ApprovalAdministrationTool.Models
{
    /// <summary>
    /// The single list of columns the tool shows, in display order: msdyn_flow_approval columns (logical names) plus
    /// columns the tool calculates. Add, remove or reorder columns here only: both the Dataverse query and the grid use it.
    /// Headers come from each column's display name in the table's Dataverse metadata (calculated columns use the name in
    /// <see cref="Calculated"/>), and approval columns that do not exist in the connected environment are left out.
    /// </summary>
    internal static class ApprovalColumns
    {
        public const string EntityName = "msdyn_flow_approval";
        public const string Id = "msdyn_flow_approvalid";

        /// <summary>Calculated: who the approval is waiting on (owners of its active approval requests).</summary>
        public const string CurrentlyWith = "currentlywith";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Id,
            "msdyn_flow_approval_title",
            "ownerid",                      // Owner: who originated the approval
            CurrentlyWith,                  // Currently With: who it is waiting on
            "msdyn_flow_approval_allowcancel",
            "msdyn_flow_approval_allowreassign",
            "msdyn_flow_approval_modeltype",
            "msdyn_flow_approval_itemlink",
            "msdyn_flow_approval_itemlinkdescription",
            "msdyn_flow_approval_requesttype",
            "msdyn_flow_approval_priority",
            "msdyn_flow_approval_sendemail",
            "msdyn_flow_approval_source",
            "msdyn_flow_approval_stage",
            "currentstage",                 // Current Stage: lookup to the stage of a multi-stage approval
            "statuscode",                   // Status Reason: Created, Pending, ... Completed, Expired, Canceled, Abandoned
            "msdyn_flow_approval_result",   // Result: the final outcome
        };

        /// <summary>Columns calculated by the tool rather than read from the approval table, with their headers.</summary>
        public static readonly IReadOnlyDictionary<string, string> Calculated = new Dictionary<string, string>
        {
            [CurrentlyWith] = "Currently With",
        };
    }
}
