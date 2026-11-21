using System.Collections.Generic;
using System.Data;

namespace ApprovalAdministrationTool.Services
{
    /// <summary>What <see cref="ApprovalService.GetApprovals"/> returns: the approvals and the query that produced them.</summary>
    internal sealed class ApprovalLoadResult
    {
        public ApprovalLoadResult(DataTable approvals, string fetchXmlPrefix, string fetchXmlSuffix, IReadOnlyCollection<string> tableColumns)
        {
            Approvals = approvals;
            FetchXmlPrefix = fetchXmlPrefix;
            FetchXmlSuffix = fetchXmlSuffix;
            TableColumns = tableColumns;
        }

        /// <summary>The approvals, one row each (see <see cref="ApprovalService.GetApprovals"/> for the columns).</summary>
        public DataTable Approvals { get; }

        /// <summary>The fixed start of the FetchXML that was run, before the filter.</summary>
        public string FetchXmlPrefix { get; }

        /// <summary>The fixed end of the FetchXML that was run, after the filter.</summary>
        public string FetchXmlSuffix { get; }

        /// <summary>The logical names of every column on the approval table, for checking filter conditions.</summary>
        public IReadOnlyCollection<string> TableColumns { get; }
    }
}
