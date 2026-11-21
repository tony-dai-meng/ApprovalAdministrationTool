using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Xml.Linq;
using ApprovalAdministrationTool.Models;

namespace ApprovalAdministrationTool.Services
{
    /// <summary>
    /// The FetchXML the approvals grid runs: a fixed part (the approval table, its columns and the sort order) around a
    /// &lt;filter&gt; element that users can edit in the query panel.
    /// </summary>
    internal static class ApprovalQuery
    {
        /// <summary>The filter used until the user runs their own: no conditions, so every approval is returned.</summary>
        public const string DefaultFilter =
            "<filter type=\"and\">\r\n" +
            "  <!-- Add conditions here, for example only pending approvals:\r\n" +
            "  <condition attribute=\"statuscode\" operator=\"eq\" value=\"192350001\" /> -->\r\n" +
            "</filter>";

        /// <summary>The fixed end of the query, after the filter.</summary>
        public const string Suffix =
            "  </entity>\r\n" +
            "</fetch>";

        /// <summary>The fixed start of the query, before the filter: the table, the given columns and the sort order.</summary>
        public static string Prefix(IEnumerable<string> columns)
        {
            var prefix = new StringBuilder();
            prefix.Append("<fetch>\r\n");
            prefix.Append("  <entity name=\"").Append(ApprovalColumns.EntityName).Append("\">\r\n");
            foreach (var column in columns)
            {
                prefix.Append("    <attribute name=\"").Append(SecurityElement.Escape(column)).Append("\" />\r\n");
            }

            prefix.Append("    <order attribute=\"createdon\" descending=\"true\" />");
            return prefix.ToString();
        }

        /// <summary>The whole query: the fixed start, the filter (may be empty) and the fixed end.</summary>
        public static string Compose(string prefix, string filter, string suffix)
        {
            return prefix + "\r\n" + (filter ?? string.Empty).Trim() + "\r\n" + suffix;
        }

        /// <summary>
        /// The query to send for one page of results: XML comments removed, and the fetch element's page, count and
        /// paging-cookie attributes set.
        /// </summary>
        public static string ForPage(string fetchXml, int page, int count, string pagingCookie)
        {
            var document = XDocument.Parse(fetchXml);
            document.DescendantNodes().OfType<XComment>().Remove();
            var fetch = document.Root;
            fetch.SetAttributeValue("page", page);
            fetch.SetAttributeValue("count", count);
            fetch.SetAttributeValue("paging-cookie", string.IsNullOrEmpty(pagingCookie) ? null : pagingCookie);
            return document.ToString(SaveOptions.DisableFormatting);
        }
    }
}
