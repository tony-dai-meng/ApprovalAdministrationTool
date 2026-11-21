using System.Collections.Generic;

namespace ApprovalAdministrationTool.Models
{
    /// <summary>
    /// Hover-text definitions for the approval columns, shown on the grid's column headers.
    /// </summary>
    /// <remarks>
    /// Sources (read November 2026):
    /// - Microsoft Learn, Dataverse table reference for msdyn_flow_approval: column descriptions, types, lengths,
    ///   defaults and choice values. https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/msdyn_flow_approval
    /// - Microsoft Learn, Approvals connector reference: action fields (item link, notifications, reassignment) and
    ///   notification email behaviour. https://learn.microsoft.com/connectors/approvals/
    /// - Microsoft Learn, approval flow how-to: reassigning and cancelling requests. https://learn.microsoft.com/power-automate/approvals-howto
    /// - Microsoft Learn, approval types. https://learn.microsoft.com/power-automate/all-assigned-must-approve
    /// - Matthew Devaney, "Extend a Power Automate approval over the 30 day limit": using Source to find a flow's approvals.
    ///   https://matthewdevaney.com/extend-a-power-automate-approval-over-the-30-day-limit.
    /// - SharePains, "Power Automate approvals and Dataverse": the Name column is often blank.
    ///   https://sharepains.com/2021/06/04/power-automate-approvals-and-dataverse/
    /// - Matthew Devaney (same post): Status Reason changes to Completed (192350004) when an approval completes.
    /// - Microsoft Learn, Dataverse table reference for msdyn_flow_approvalrequest: one request per approver, its Owner,
    ///   and the Reassigned status reason. https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/msdyn_flow_approvalrequest
    /// Not documented by these sources (inferred, and worded as such): the mapping of Approval Model Type values to
    /// approval types, the sequential approval model, and what a custom-response approval stores as its Result.
    /// </remarks>
    internal static class ApprovalColumnDefinitions
    {
        public static readonly IReadOnlyDictionary<string, string> ByLogicalName = new Dictionary<string, string>
        {
            ["msdyn_flow_approval_title"] =
                "The title of the approval request: the short heading approvers see in the Power Automate Approvals center, " +
                "in the approval email and in Microsoft Teams. It comes from the required Title field of the approval action " +
                "in the flow (for example “Start and wait for an approval”) and is usually built from dynamic content, such as " +
                "an item name or request number. Dataverse stores it as text of up to 2,048 characters and requires it. It is the " +
                "most reliable way to recognise an approval, because the table’s Name column is often left blank.",

            ["ownerid"] =
                "Who originated the approval: the user (or team) that owns the approval record. The approval table has no " +
                "separate requester column, and Microsoft documents this one only as “Owner Id”. It is the “approval " +
                "owner” that Allow Cancel refers to, and is normally the person whose flow sent the approval (Microsoft notes " +
                "that the creator of the flow is always shown in the approval details). Its friendly value is the owner’s " +
                "name, and its stored value is their user or team ID.",

            [ApprovalColumns.CurrentlyWith] =
                "Who the approval is currently waiting on. Power Automate creates one Approval Request " +
                "(msdyn_flow_approvalrequest, “an individual request for approval”) for each assigned approver, owned by " +
                "that approver. A request stops being active once it is answered or reassigned; reassigning moves the request " +
                "to the new approver and marks the old one with the status reason Reassigned. This column lists the owners of " +
                "the approval’s active requests, so it is empty once the approval no longer waits on anyone. It is " +
                "calculated by this tool rather than stored on the approval. Its friendly value lists names, and its stored " +
                "value lists the approvers’ user IDs.",

            ["msdyn_flow_approval_allowcancel"] =
                "Whether the owner of the approval is allowed to cancel it. Microsoft describes it as a Yes/No field that " +
                "“allows the approval owner to cancel the approval”, and its default in Dataverse is No. In Power Automate, " +
                "the maker of the flow that sent the approval can cancel requests that have not been answered yet from the Sent " +
                "tab of the Approvals center. A cancelled approval ends with the status reason Canceled, so its approvers can no " +
                "longer respond to it.",

            ["msdyn_flow_approval_allowreassign"] =
                "Whether approvers are allowed to reassign their approval request to someone else. Microsoft describes it as a " +
                "Yes/No field that “allows the approvers to reassign approval requests”, and its default in Dataverse is No; " +
                "it matches the “Enable reassignment” option of the approval action in the flow. When it is allowed, an approver " +
                "can open the request in the Power Automate Approvals center, select the three dots and choose Reassign to hand " +
                "it to another person. The requester cannot reassign a request: they have to cancel it and change the approvers " +
                "in the flow instead.",

            ["msdyn_flow_approval_modeltype"] =
                "The name of the Dataverse table that holds this approval’s model, which is how Dataverse records the kind of " +
                "approval that was created. Microsoft describes it as the “table name of the approval model”; together with " +
                "the Approval Model Id column it identifies the model record. Microsoft does not document the individual values, " +
                "but their names line up with the approval types chosen in a flow: msdyn_flow_basicapprovalmodel with " +
                "“Approve/Reject – First to respond”, msdyn_flow_awaitallapprovalmodel with “Approve/Reject – Everyone must " +
                "approve”, msdyn_flow_actionapprovalmodel with “Custom responses – Wait for one response”, and " +
                "msdyn_flow_awaitallactionapprovalmodel with “Custom responses – Wait for all responses”. " +
                "msdyn_flow_sequentialapprovalmodel appears to be used for sequential approvals, where approvers respond one " +
                "after another.",

            ["msdyn_flow_approval_itemlink"] =
                "An optional web link to the item being approved, such as a SharePoint list item, a document or a Dataverse " +
                "record. It comes from the Item link field of the approval action (“Add a link to the item to approve”) and is " +
                "shown to approvers as a clickable link in the approval email, the Approvals center and Teams, so they can review " +
                "the item before they respond. Dataverse stores it as a URL of up to 2,048 characters; it is empty when the flow " +
                "did not provide one.",

            ["msdyn_flow_approval_itemlinkdescription"] =
                "The optional text shown for the Item Link, so approvers see a readable label instead of a raw web address " +
                "(for example “Open the expense report”). It comes from the Item link description field of the approval action " +
                "and is stored as text of up to 2,048 characters. It is only meaningful together with Item Link, and it is empty " +
                "when the flow did not provide one.",

            ["msdyn_flow_approval_requesttype"] =
                "What kind of process created the approval. Microsoft describes it as “the type of request that created the " +
                "approval whether from an approval template, esignature process, etc.” The possible values are Basic (the " +
                "default, used for ordinary approvals such as those sent by the approval actions in a flow), Templates " +
                "(created from an approval template), eSign (created by an electronic signature process) and Other.",

            ["msdyn_flow_approval_priority"] =
                "How urgent the approval is: Urgent, Important, Medium or Low, with Medium as the default. Microsoft’s table " +
                "reference describes it only as “the priority of the approval”, and the documentation for the flow approval " +
                "actions does not list a priority input, so it mainly serves as a label for sorting and filtering approvals by " +
                "urgency.",

            ["msdyn_flow_approval_sendemail"] =
                "Whether system-generated email notifications are sent for this approval (Microsoft: “whether to send " +
                "system-generated email notifications for this approval”); the default is Yes. In a flow it matches the " +
                "“Enable notifications” option of the approval action, which covers email, push and Teams notifications. " +
                "Approval emails are always sent from a standard Microsoft account, and actionable approval emails in Outlook are " +
                "not supported for guest users. When it is No, the approval is still created, but approvers have to find it in the " +
                "Approvals center or wherever the flow posts it.",

            ["msdyn_flow_approval_source"] =
                "A free-text label for where the approval request came from. Microsoft describes it as the “source of the " +
                "request that created the approval”; it holds up to 500 characters and is often empty. Flows can set it so that " +
                "other processes can find their own approvals: for example, a second flow that watches the Approvals table and " +
                "filters on Source to pick up only the approvals it is responsible for.",

            ["msdyn_flow_approval_stage"] =
                "The approval’s stage, one of Not Specified, Basic or Complete. Microsoft’s description is only “the stage”, " +
                "and the column is required. It is not the outcome: whether the approval was approved or rejected is in the " +
                "Result column, and its overall state (Created, Pending, Suspended, Completed, Expired, Canceled or Abandoned) " +
                "is in Status Reason. The same choice values are used by the Stage column of each Approval Step, and multi-stage " +
                "approvals also link to their current stage through the Current Stage column.",

            ["currentstage"] =
                "A link to the stage that a multi-stage approval is currently at. Microsoft describes it as “the link to the " +
                "current stage of the multi stage approvals”; it points to a record in the Approval Stage Order table. " +
                "Approvals that do not use stages leave it empty. Its friendly value is the stage record’s name, and its " +
                "stored value is that record’s ID.",

            ["statuscode"] =
                "Where the approval is in its life cycle. Microsoft describes it as “the reason for the status of the " +
                "approval”. While the approval is active, the value is Created, Pending or Suspended; once it is inactive, it " +
                "is Completed, Expired, Canceled or Abandoned (the separate Status column holds Active or Inactive). An approval " +
                "that has received the responses it needs changes to Completed, which is why flows that wait for approvals in " +
                "Dataverse often watch for that value (stored as 192350004). Microsoft does not document exactly when Suspended, " +
                "Expired or Abandoned are set.",

            ["msdyn_flow_approval_result"] =
                "The final outcome of the approval (Microsoft: “final outcome of the approval”), stored as text of up to " +
                "100 characters. For Approve/Reject approvals it is Approve or Reject; for approvals with custom responses it is " +
                "the response option that completed the approval. It is empty until the approval has an outcome. Flows that act " +
                "on finished approvals check this value; each approver’s individual response and comments are stored " +
                "separately, in the Approval Responses table.",
        };
    }
}
