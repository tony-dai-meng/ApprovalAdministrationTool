using System;
using System.Data;
using System.Linq;
using ApprovalAdministrationTool.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ApprovalAdministrationTool.Services
{
    /// <summary>
    /// All Dataverse work for Power Automate approvals. It contains no UI code, so call it from inside WorkAsync.
    /// Approval actions (approve on behalf, reassign, cancel...) belong here too.
    /// </summary>
    internal class ApprovalService
    {
        private readonly IOrganizationService service;

        public ApprovalService(IOrganizationService service)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>
        /// Retrieves every approval (all pages, newest first) as a table with one column per <see cref="ApprovalColumns.All"/>
        /// entry, captioned with its header. The ID column holds the record Guid; every other column holds the display text
        /// Dataverse returns (choice labels, Yes/No, lookup names), so no metadata lookups are needed.
        /// </summary>
        public DataTable GetApprovals()
        {
            var query = new QueryExpression(ApprovalColumns.EntityName)
            {
                ColumnSet = new ColumnSet(ApprovalColumns.All.Select(c => c.LogicalName).ToArray()),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 },
            };
            query.AddOrder("createdon", OrderType.Descending);

            var table = CreateTable();
            EntityCollection page;
            do
            {
                page = service.RetrieveMultiple(query);
                foreach (var approval in page.Entities)
                {
                    AddRow(table, approval);
                }

                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
            while (page.MoreRecords);

            return table;
        }

        private static DataTable CreateTable()
        {
            var table = new DataTable(ApprovalColumns.EntityName);
            foreach (var column in ApprovalColumns.All)
            {
                var type = column.LogicalName == ApprovalColumns.Id ? typeof(Guid) : typeof(string);
                table.Columns.Add(column.LogicalName, type).Caption = column.Header;
            }

            return table;
        }

        private static void AddRow(DataTable table, Entity approval)
        {
            var row = table.NewRow();
            foreach (var column in ApprovalColumns.All)
            {
                row[column.LogicalName] = column.LogicalName == ApprovalColumns.Id
                    ? approval.Id
                    : (object)GetDisplayText(approval, column.LogicalName) ?? DBNull.Value;
            }

            table.Rows.Add(row);
        }

        /// <summary>
        /// The formatted value Dataverse sends for choices, Yes/No, lookups, money and dates; otherwise the raw value as text.
        /// </summary>
        private static string GetDisplayText(Entity entity, string logicalName)
        {
            if (entity.FormattedValues.TryGetValue(logicalName, out var formatted))
            {
                return formatted;
            }

            if (!entity.Attributes.TryGetValue(logicalName, out var value) || value == null)
            {
                return null;
            }

            switch (value)
            {
                case EntityReference reference:
                    return reference.Name ?? reference.Id.ToString();
                case OptionSetValue option:
                    return option.Value.ToString();
                case Money money:
                    return money.Value.ToString();
                default:
                    return value.ToString();
            }
        }
    }
}
