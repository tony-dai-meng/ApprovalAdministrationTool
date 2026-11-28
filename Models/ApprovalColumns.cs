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

        /// <summary>Approval Model Type: the kind of approval (the name of its approval-model table).</summary>
        public const string ModelType = "msdyn_flow_approval_modeltype";

        /// <summary>
        /// Shown in Currently With for "Approve/Reject - Everyone must approve" approvals (<see cref="NotApplicableModelTypes"/>):
        /// their answered requests stay active until the approval finishes. Who has answered is listed in the action windows.
        /// </summary>
        public const string NotApplicable = "N/A";

        /// <summary>Approval model types whose Currently With shows <see cref="NotApplicable"/>: Approve/Reject - Everyone must approve.</summary>
        public static readonly ISet<string> NotApplicableModelTypes = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "msdyn_flow_awaitallapprovalmodel",
        };

        /// <summary>
        /// "Custom responses - Wait for all responses": Currently With lists the approvers on the current step who have not
        /// responded yet (their answered requests also stay active, so responses are checked).
        /// </summary>
        public const string CustomWaitForAllModelType = "msdyn_flow_awaitallactionapprovalmodel";

        /// <summary>
        /// Approval model types with custom responses ("Wait for one response", "Wait for all responses"). Approve and
        /// Reject mean nothing for them, so the panel shows one Action button instead.
        /// </summary>
        public static readonly ISet<string> CustomResponseModelTypes = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "msdyn_flow_actionapprovalmodel",
            CustomWaitForAllModelType,
        };

        /// <summary>Approval model types that wait for everyone's response ("Everyone must approve", "Wait for all responses").</summary>
        public static readonly ISet<string> WaitForAllModelTypes = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "msdyn_flow_awaitallapprovalmodel",
            "msdyn_flow_awaitallactionapprovalmodel",
        };

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
            "currentstage",                // Current Stage: lookup to the stage of a multi-stage approval
            "statuscode",                   // Status Reason: Created, Pending, ... Completed, Expired, Canceled, Abandoned
            "msdyn_flow_approval_result",   // Result: the final outcome
        };

        /// <summary>Status reasons of finished approvals (Completed, Expired, Canceled, Abandoned): there is nothing left to act on.</summary>
        public static readonly ISet<string> FinishedStatuses = new HashSet<string> { "192350004", "192350005", "192350006", "192350007" };

        /// <summary>Details: the approval's Markdown body, shown rendered in the pane beside the grid.</summary>
        public const string Details = "msdyn_flow_approval_details";

        /// <summary>Columns read with the approvals (after <see cref="All"/>) but not shown as grid columns.</summary>
        public static readonly IReadOnlyList<string> Hidden = new[] { Details };

        /// <summary>Columns calculated by the tool rather than read from the approval table, with their headers.</summary>
        public static readonly IReadOnlyDictionary<string, string> Calculated = new Dictionary<string, string>
        {
            [CurrentlyWith] = "Currently With",
        };
    }
}
