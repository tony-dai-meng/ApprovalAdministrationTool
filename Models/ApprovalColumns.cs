using System.Collections.Generic;

namespace ApprovalAdministrationTool.Models
{
    /// <summary>
    /// The single list of msdyn_flow_approval columns the tool queries and shows, in display order.
    /// Add, remove or reorder columns here only: both the Dataverse query and the grid use this list.
    /// </summary>
    internal static class ApprovalColumns
    {
        public const string EntityName = "msdyn_flow_approval";
        public const string Id = "msdyn_flow_approvalid";

        public static readonly IReadOnlyList<ApprovalColumn> All = new[]
        {
            new ApprovalColumn(Id, "Approval ID"),
            new ApprovalColumn("msdyn_flow_approval_title", "Title"),
            new ApprovalColumn("msdyn_flow_approval_allowcancel", "Allow cancel"),
            new ApprovalColumn("msdyn_flow_approval_allowreassign", "Allow reassign"),
            new ApprovalColumn("msdyn_flow_approval_modeltype", "Model type"),
            new ApprovalColumn("msdyn_flow_approval_itemlink", "Item link"),
            new ApprovalColumn("msdyn_flow_approval_itemlinkdescription", "Item link description"),
            new ApprovalColumn("msdyn_flow_approval_requesttype", "Request type"),
            new ApprovalColumn("msdyn_flow_approval_priority", "Priority"),
            new ApprovalColumn("msdyn_flow_approval_sendemail", "Send email"),
            new ApprovalColumn("msdyn_flow_approval_source", "Source"),
            new ApprovalColumn("msdyn_flow_approval_stage", "Stage"),
        };
    }

    /// <summary>A column's logical name and the header shown for it in the grid.</summary>
    internal sealed class ApprovalColumn
    {
        public ApprovalColumn(string logicalName, string header)
        {
            LogicalName = logicalName;
            Header = header;
        }

        public string LogicalName { get; }

        public string Header { get; }
    }
}
