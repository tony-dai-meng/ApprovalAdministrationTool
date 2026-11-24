using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using ApprovalAdministrationTool.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
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
        /// entry that exists in the connected environment. Each column is named by its logical name and captioned with its
        /// Dataverse display name. The ID column holds the record Guid.
        /// Columns in <see cref="ApprovalColumnValues.WithFriendlyValues"/> hold the value stored in Dataverse (a choice's
        /// number, a lookup's ID, a table name), with the friendly value (a choice's label, a lookup's name, a friendly
        /// approval type) in an extra column named logical name + <see cref="ApprovalColumnValues.FriendlySuffix"/>.
        /// Every other column holds display text (Yes/No labels, lookup names, text).
        /// Also calculates Currently With (who each approval is waiting on) from the active approval requests.
        /// The approvals are read with FetchXML: the fixed part from <see cref="ApprovalQuery"/> around
        /// <paramref name="filterXml"/>, a &lt;filter&gt; element (null or empty for no filter).
        /// </summary>
        public ApprovalLoadResult GetApprovals(string filterXml)
        {
            var metadata = GetColumnMetadata();
            var columns = ApprovalColumns.All
                .Concat(ApprovalColumns.Hidden)
                .Where(column => metadata.ContainsKey(column) || ApprovalColumns.Calculated.ContainsKey(column))
                .ToList();
            var optionLabels = columns
                .Where(metadata.ContainsKey)
                .Select(column => new { Column = column, Labels = OptionLabels(metadata[column]) })
                .Where(column => column.Labels.Count > 0)
                .ToDictionary(column => column.Column, column => column.Labels);
            var table = CreateTable(columns, metadata, optionLabels);
            var currentApprovers = columns.Contains(ApprovalColumns.CurrentlyWith)
                ? GetCurrentApprovers()
                : new Dictionary<Guid, List<EntityReference>>();

            var prefix = ApprovalQuery.Prefix(columns.Where(column => !ApprovalColumns.Calculated.ContainsKey(column)));
            var fetchXml = ApprovalQuery.Compose(prefix, filterXml, ApprovalQuery.Suffix);
            foreach (var approval in RetrieveAll(fetchXml))
            {
                AddRow(table, columns, approval, optionLabels, currentApprovers);
            }

            return new ApprovalLoadResult(table, prefix, ApprovalQuery.Suffix, metadata.Keys.ToList());
        }

        /// <summary>Runs a FetchXML query page by page (5,000 records each) and returns the records of every page.</summary>
        private IEnumerable<Entity> RetrieveAll(string fetchXml)
        {
            var pageNumber = 1;
            string pagingCookie = null;
            EntityCollection page;
            do
            {
                page = service.RetrieveMultiple(new FetchExpression(ApprovalQuery.ForPage(fetchXml, pageNumber, 5000, pagingCookie)));
                foreach (var record in page.Entities)
                {
                    yield return record;
                }

                pageNumber++;
                pagingCookie = page.PagingCookie;
            }
            while (page.MoreRecords);
        }

        /// <summary>
        /// Who each approval is currently waiting on: the owners of its active approval requests. Power Automate creates one
        /// approval request (msdyn_flow_approvalrequest) per assigned approver, owned by that approver; a request stops being
        /// active once it is answered or reassigned, but not when its approval is abandoned or cancelled, so only requests of
        /// approvals that are still open count. Keyed by approval ID; names sorted, each person once.
        /// </summary>
        private Dictionary<Guid, List<EntityReference>> GetCurrentApprovers()
        {
            const string ApprovalLookup = "msdyn_flow_approvalrequest_approval";
            var query = new QueryExpression("msdyn_flow_approvalrequest")
            {
                ColumnSet = new ColumnSet(ApprovalLookup, "ownerid"),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 },
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0); // Active
            query.AddLink(ApprovalColumns.EntityName, ApprovalLookup, ApprovalColumns.Id)
                .LinkCriteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var approvers = new Dictionary<Guid, List<EntityReference>>();
            foreach (var request in RetrieveAll(query))
            {
                var approval = request.GetAttributeValue<EntityReference>(ApprovalLookup);
                var owner = request.GetAttributeValue<EntityReference>("ownerid");
                if (approval == null || owner == null)
                {
                    continue;
                }

                if (owner.Name == null && request.FormattedValues.TryGetValue("ownerid", out var ownerName))
                {
                    owner.Name = ownerName;
                }

                if (!approvers.TryGetValue(approval.Id, out var list))
                {
                    approvers[approval.Id] = list = new List<EntityReference>();
                }

                if (list.All(existing => existing.Id != owner.Id))
                {
                    list.Add(owner);
                }
            }

            foreach (var list in approvers.Values)
            {
                list.Sort((a, b) => string.Compare(a.Name ?? a.Id.ToString(), b.Name ?? b.Id.ToString(), StringComparison.CurrentCultureIgnoreCase));
            }

            return approvers;
        }

        /// <summary>Runs a paged query and returns the records of every page.</summary>
        private IEnumerable<Entity> RetrieveAll(QueryExpression query)
        {
            EntityCollection page;
            do
            {
                page = service.RetrieveMultiple(query);
                foreach (var record in page.Entities)
                {
                    yield return record;
                }

                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
            while (page.MoreRecords);
        }

        /// <summary>The metadata of every column on the approval table, by logical name.</summary>
        private Dictionary<string, AttributeMetadata> GetColumnMetadata()
        {
            var response = (RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest
            {
                LogicalName = ApprovalColumns.EntityName,
                EntityFilters = EntityFilters.Attributes,
            });

            return response.EntityMetadata.Attributes.ToDictionary(attribute => attribute.LogicalName);
        }

        /// <summary>
        /// The label of each option of a choice column (keyed by its stored number) or a Yes/No column (1 = Yes, 0 = No),
        /// in the user's language; empty for other columns.
        /// </summary>
        private static Dictionary<int, string> OptionLabels(AttributeMetadata attribute)
        {
            var options = new List<OptionMetadata>();
            switch (attribute)
            {
                case EnumAttributeMetadata choice when choice.OptionSet?.Options != null:
                    options.AddRange(choice.OptionSet.Options);
                    break;
                case BooleanAttributeMetadata yesNo when yesNo.OptionSet != null:
                    options.Add(yesNo.OptionSet.TrueOption);
                    options.Add(yesNo.OptionSet.FalseOption);
                    break;
            }

            var labels = new Dictionary<int, string>();
            foreach (var option in options.Where(option => option?.Value != null))
            {
                var label = LabelText(option.Label);
                if (label != null)
                {
                    labels[option.Value.Value] = label;
                }
            }

            return labels;
        }

        /// <summary>A metadata label's text in the user's language (or the first language available), or null.</summary>
        private static string LabelText(Label label)
        {
            var text = label?.UserLocalizedLabel?.Label ?? label?.LocalizedLabels?.FirstOrDefault()?.Label;
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static DataTable CreateTable(
            IEnumerable<string> columns,
            Dictionary<string, AttributeMetadata> metadata,
            Dictionary<string, Dictionary<int, string>> optionLabels)
        {
            var table = new DataTable(ApprovalColumns.EntityName);
            foreach (var logicalName in columns)
            {
                var caption = metadata.TryGetValue(logicalName, out var attribute)
                    ? LabelText(attribute.DisplayName) ?? logicalName
                    : ApprovalColumns.Calculated[logicalName];
                var type = logicalName == ApprovalColumns.Id ? typeof(Guid) : typeof(string);
                var column = table.Columns.Add(logicalName, type);
                column.Caption = caption;

                // Label => stored value for choice and Yes/No columns, so the grid can turn its filters into FetchXML.
                if (optionLabels.TryGetValue(logicalName, out var labels))
                {
                    column.ExtendedProperties[ApprovalColumnValues.OptionValuesProperty] = labels
                        .GroupBy(option => option.Value)
                        .ToDictionary(group => group.Key, group => group.First().Key.ToString());
                }

                if (ApprovalColumnValues.WithFriendlyValues.Contains(logicalName))
                {
                    table.Columns.Add(logicalName + ApprovalColumnValues.FriendlySuffix, typeof(string)).Caption = caption;
                }
            }

            return table;
        }

        private static void AddRow(
            DataTable table,
            IEnumerable<string> columns,
            Entity approval,
            Dictionary<string, Dictionary<int, string>> optionLabels,
            Dictionary<Guid, List<EntityReference>> currentApprovers)
        {
            var row = table.NewRow();
            foreach (var logicalName in columns)
            {
                if (logicalName == ApprovalColumns.Id)
                {
                    row[logicalName] = approval.Id;
                }
                else if (logicalName == ApprovalColumns.CurrentlyWith)
                {
                    // Stored: the approvers' IDs; friendly: their names. Empty when the approval is not waiting on anyone.
                    if (currentApprovers.TryGetValue(approval.Id, out var approvers) && approvers.Count > 0)
                    {
                        row[logicalName] = string.Join("; ", approvers.Select(approver => approver.Id.ToString()));
                        row[logicalName + ApprovalColumnValues.FriendlySuffix] = string.Join("; ", approvers.Select(approver => approver.Name ?? approver.Id.ToString()));
                    }
                }
                else if (ApprovalColumnValues.WithFriendlyValues.Contains(logicalName))
                {
                    row[logicalName] = (object)GetStoredText(approval, logicalName) ?? DBNull.Value;
                    row[logicalName + ApprovalColumnValues.FriendlySuffix] = (object)GetFriendlyText(approval, logicalName, optionLabels) ?? DBNull.Value;
                }
                else
                {
                    row[logicalName] = (object)GetDisplayText(approval, logicalName, optionLabels) ?? DBNull.Value;
                }
            }

            table.Rows.Add(row);
        }

        /// <summary>
        /// The value as stored in Dataverse: a choice's number, a lookup's record ID, otherwise the value as text.
        /// </summary>
        private static string GetStoredText(Entity entity, string logicalName)
        {
            if (!entity.Attributes.TryGetValue(logicalName, out var value) || value == null)
            {
                return null;
            }

            switch (value)
            {
                case OptionSetValue option:
                    return option.Value.ToString();
                case EntityReference reference:
                    return reference.Id.ToString();
                case Money money:
                    return money.Value.ToString();
                default:
                    return value.ToString();
            }
        }

        /// <summary>
        /// The friendly value: a name from <see cref="ApprovalColumnValues.FriendlyNames"/> if there is one for the stored
        /// value, otherwise the display text (a choice's label, a lookup's record name).
        /// </summary>
        private static string GetFriendlyText(Entity entity, string logicalName, Dictionary<string, Dictionary<int, string>> optionLabels)
        {
            var stored = GetStoredText(entity, logicalName);
            if (stored != null
                && ApprovalColumnValues.FriendlyNames.TryGetValue(logicalName, out var names)
                && names.TryGetValue(stored, out var friendly))
            {
                return friendly;
            }

            return GetDisplayText(entity, logicalName, optionLabels);
        }

        /// <summary>
        /// Display text: for choice and Yes/No columns, the option's label from the column's metadata; otherwise (or if the
        /// metadata has no label for the value) the formatted value Dataverse sends; otherwise the raw value as text.
        /// </summary>
        private static string GetDisplayText(Entity entity, string logicalName, Dictionary<string, Dictionary<int, string>> optionLabels)
        {
            if (!entity.Attributes.TryGetValue(logicalName, out var value) || value == null)
            {
                return entity.FormattedValues.TryGetValue(logicalName, out var formattedOnly) ? formattedOnly : null;
            }

            int? optionValue = value is OptionSetValue option ? option.Value : value is bool yes ? (yes ? 1 : 0) : (int?)null;
            if (optionValue.HasValue
                && optionLabels.TryGetValue(logicalName, out var labels)
                && labels.TryGetValue(optionValue.Value, out var label))
            {
                return label;
            }

            if (entity.FormattedValues.TryGetValue(logicalName, out var formatted))
            {
                return formatted;
            }

            switch (value)
            {
                case EntityReference reference:
                    return reference.Name ?? reference.Id.ToString();
                case OptionSetValue unlabeled:
                    return unlabeled.Value.ToString();
                case Money money:
                    return money.Value.ToString();
                default:
                    return value.ToString();
            }
        }
    }
}
