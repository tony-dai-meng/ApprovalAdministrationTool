using System;
using ApprovalAdministrationTool.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ApprovalAdministrationTool.Services
{
    /// <summary>
    /// Which approval requests belong to the step an approval is on. Power Automate creates the requests of every step
    /// of a sequential approval up front, all active, and moves the approval's current step number on as each step is
    /// answered. So a request only counts (is what the approval waits on, and what an action answers or reassigns)
    /// while its step number is the approval's current step. Other approvals have one step, number 0.
    /// </summary>
    internal static class ApprovalSteps
    {
        public const string RequestStep = "msdyn_flow_approvalrequest_stepnumber";
        public const string CurrentStep = "msdyn_flow_approval_currentstepnumber";
        private const string Alias = "approval";

        /// <summary>
        /// Leaves out requests of approvals that are finished (a finished approval keeps its unanswered requests active)
        /// and reads each request's step and its approval's current step, for <see cref="IsCurrent(Entity)"/>.
        /// </summary>
        public static void LinkOpenApproval(QueryExpression requestQuery, string approvalLookup)
        {
            requestQuery.ColumnSet.AddColumn(RequestStep);
            var link = requestQuery.AddLink(ApprovalColumns.EntityName, approvalLookup, ApprovalColumns.Id);
            link.EntityAlias = Alias;
            link.Columns = new ColumnSet(CurrentStep);
            link.LinkCriteria.AddCondition("statecode", ConditionOperator.Equal, 0); // Active
        }

        /// <summary>Whether a request read with <see cref="LinkOpenApproval"/> is on its approval's current step.</summary>
        public static bool IsCurrent(Entity request)
        {
            var current = request.GetAttributeValue<AliasedValue>(Alias + "." + CurrentStep)?.Value as int?;
            return IsCurrent(request.GetAttributeValue<int?>(RequestStep), current);
        }

        /// <summary>
        /// Whether a request on <paramref name="requestStep"/> is on <paramref name="currentStep"/>. A missing number
        /// counts as step 0 (the only step of an approval that is not sequential).
        /// </summary>
        public static bool IsCurrent(int? requestStep, int? currentStep)
        {
            return (requestStep ?? 0) == (currentStep ?? 0);
        }

        /// <summary>Throws if the request is no longer on its approval's current step (that step was answered meanwhile).</summary>
        public static void CheckCurrent(Entity request, Entity approval)
        {
            if (!IsCurrent(request.GetAttributeValue<int?>(RequestStep), approval.GetAttributeValue<int?>(CurrentStep)))
            {
                throw new InvalidOperationException("The approval has moved on to another step in the meantime. Refresh and try again.");
            }
        }
    }
}
