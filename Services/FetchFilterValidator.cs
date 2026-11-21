using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace ApprovalAdministrationTool.Services
{
    /// <summary>
    /// Checks the user-editable &lt;filter&gt; part of the approvals query before it is run. Only a filter may be edited:
    /// the elements &lt;filter&gt;, &lt;condition&gt; and &lt;value&gt; with the attributes type, attribute, operator and value.
    /// Conditions must name a column of the approval table and use a FetchXML operator, with a value where one is needed.
    /// Errors stop the query from running; warnings do not.
    /// </summary>
    internal static class FetchFilterValidator
    {
        /// <summary>One problem found in the filter; Line and Column are 1-based (0 when unknown).</summary>
        internal sealed class Problem
        {
            public Problem(int line, int column, string message, bool isError)
            {
                Line = line;
                Column = column;
                Message = message;
                IsError = isError;
            }

            public int Line { get; }

            public int Column { get; }

            public string Message { get; }

            public bool IsError { get; }

            public override string ToString()
            {
                return (Line > 0 ? "Line " + Line + ", column " + Column + ": " : string.Empty) + Message;
            }
        }

        private static readonly Dictionary<string, string[]> AllowedAttributes = new Dictionary<string, string[]>
        {
            ["filter"] = new[] { "type" },
            ["condition"] = new[] { "attribute", "operator", "value" },
            ["value"] = new string[0],
        };

        /// <summary>FetchXML condition operators that take no value.</summary>
        private static readonly HashSet<string> OperatorsWithoutValue = new HashSet<string>
        {
            "null", "not-null", "yesterday", "today", "tomorrow", "last-seven-days", "next-seven-days",
            "last-week", "this-week", "next-week", "last-month", "this-month", "next-month", "last-year", "this-year", "next-year",
            "eq-userid", "ne-userid", "eq-userteams", "eq-useroruserteams", "eq-useroruserhierarchy",
            "eq-useroruserhierarchyandteams", "eq-businessid", "ne-businessid", "eq-userlanguage",
            "this-fiscal-year", "this-fiscal-period", "next-fiscal-year", "next-fiscal-period", "last-fiscal-year", "last-fiscal-period",
        };

        /// <summary>FetchXML condition operators that take several values (as &lt;value&gt; elements).</summary>
        private static readonly HashSet<string> OperatorsWithValueList = new HashSet<string>
        {
            "in", "not-in", "between", "not-between", "contain-values", "not-contain-values", "in-fiscal-period-and-year",
            "in-or-before-fiscal-period-and-year", "in-or-after-fiscal-period-and-year",
        };

        /// <summary>FetchXML condition operators that take one value.</summary>
        private static readonly HashSet<string> OperatorsWithValue = new HashSet<string>
        {
            "eq", "ne", "neq", "gt", "ge", "lt", "le", "like", "not-like", "begins-with", "not-begin-with", "ends-with", "not-end-with",
            "on", "on-or-before", "on-or-after", "last-x-hours", "next-x-hours", "last-x-days", "next-x-days", "last-x-weeks",
            "next-x-weeks", "last-x-months", "next-x-months", "last-x-years", "next-x-years", "olderthan-x-minutes",
            "olderthan-x-hours", "olderthan-x-days", "olderthan-x-weeks", "olderthan-x-months", "olderthan-x-years",
            "last-x-fiscal-years", "last-x-fiscal-periods", "next-x-fiscal-years", "next-x-fiscal-periods", "in-fiscal-year",
            "in-fiscal-period", "under", "eq-or-under", "not-under", "above", "eq-or-above",
        };

        /// <param name="filterXml">The editable part of the query: one &lt;filter&gt; element, or empty for no filter.</param>
        /// <param name="tableColumns">The approval table's column names, or null to skip checking condition columns.</param>
        public static IReadOnlyList<Problem> Validate(string filterXml, ICollection<string> tableColumns)
        {
            var problems = new List<Problem>();
            if (string.IsNullOrWhiteSpace(filterXml))
            {
                return problems; // no filter: every approval
            }

            XDocument document;
            try
            {
                document = XDocument.Parse(filterXml, LoadOptions.SetLineInfo);
            }
            catch (XmlException exception)
            {
                problems.Add(new Problem(exception.LineNumber, exception.LinePosition, "The XML is not valid: " + FirstSentence(exception.Message), true));
                return problems;
            }

            var root = document.Root;
            if (root.Name.LocalName != "filter" || root.Name.NamespaceName.Length > 0)
            {
                problems.Add(Error(root, "The editable part must be a single <filter> element; <" + root.Name.LocalName + "> cannot be used here."));
                return problems;
            }

            Check(root, null, tableColumns, problems);
            return problems.OrderBy(problem => problem.Line).ThenBy(problem => problem.Column).ToList();
        }

        private static void Check(XElement element, XElement parent, ICollection<string> tableColumns, List<Problem> problems)
        {
            var name = element.Name.LocalName;
            if (!AllowedAttributes.ContainsKey(name) || element.Name.NamespaceName.Length > 0)
            {
                problems.Add(Error(element, "<" + name + "> is not allowed. Only <filter>, <condition> and <value> can be used; the rest of the query is fixed."));
                return;
            }

            var parentName = parent?.Name.LocalName;
            if ((name == "filter" && parentName != null && parentName != "filter")
                || (name == "condition" && parentName != "filter")
                || (name == "value" && parentName != "condition"))
            {
                problems.Add(Error(element, "<" + name + "> cannot go inside <" + parentName + ">."));
                return;
            }

            foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
            {
                if (!AllowedAttributes[name].Contains(attribute.Name.LocalName) || attribute.Name.NamespaceName.Length > 0)
                {
                    var allowed = AllowedAttributes[name].Length == 0 ? "no attributes" : string.Join(", ", AllowedAttributes[name]);
                    problems.Add(Error(attribute, "The attribute '" + attribute.Name.LocalName + "' is not allowed on <" + name + "> (allowed: " + allowed + ")."));
                }
            }

            foreach (var text in element.Nodes().OfType<XText>().Where(text => text.Value.Trim().Length > 0 && name != "value"))
            {
                problems.Add(Error(text, "Text is not allowed inside <" + name + ">: \"" + Shorten(text.Value.Trim()) + "\"."));
            }

            switch (name)
            {
                case "filter":
                    var type = element.Attribute("type");
                    if (type != null && type.Value != "and" && type.Value != "or")
                    {
                        problems.Add(Error(type, "A filter's type must be \"and\" or \"or\"."));
                    }

                    break;
                case "condition":
                    CheckCondition(element, tableColumns, problems);
                    break;
                case "value":
                    if (element.Value.Trim().Length == 0)
                    {
                        problems.Add(Error(element, "<value> is empty."));
                    }

                    break;
            }

            foreach (var child in element.Elements())
            {
                Check(child, element, tableColumns, problems);
            }
        }

        private static void CheckCondition(XElement condition, ICollection<string> tableColumns, List<Problem> problems)
        {
            var column = condition.Attribute("attribute");
            if (column == null || column.Value.Trim().Length == 0)
            {
                problems.Add(Error(condition, "A <condition> needs an attribute=\"...\" naming a column of the approval table."));
            }
            else if (tableColumns != null && !tableColumns.Contains(column.Value))
            {
                var match = tableColumns.FirstOrDefault(name => string.Equals(name, column.Value, StringComparison.OrdinalIgnoreCase));
                problems.Add(Error(column, match != null
                    ? "Column names are lower case: use \"" + match + "\"."
                    : "The approval table has no column named \"" + column.Value + "\"."));
            }

            var operatorAttribute = condition.Attribute("operator");
            if (operatorAttribute == null || operatorAttribute.Value.Trim().Length == 0)
            {
                problems.Add(Error(condition, "A <condition> needs an operator=\"...\", for example eq, ne, like, in or null."));
                return;
            }

            var op = operatorAttribute.Value;
            var hasValueAttribute = condition.Attribute("value") != null;
            var valueElements = condition.Elements().Count(element => element.Name.LocalName == "value");

            if (hasValueAttribute && valueElements > 0)
            {
                problems.Add(Error(condition, "Use either value=\"...\" or <value> elements, not both."));
            }
            else if (OperatorsWithoutValue.Contains(op))
            {
                if (hasValueAttribute || valueElements > 0)
                {
                    problems.Add(Warning(operatorAttribute, "The operator \"" + op + "\" takes no value; the value is ignored."));
                }
            }
            else if (OperatorsWithValueList.Contains(op))
            {
                if (!hasValueAttribute && valueElements == 0)
                {
                    problems.Add(Error(operatorAttribute, "The operator \"" + op + "\" needs values: add <value>...</value> elements inside the condition."));
                }
                else if ((op == "between" || op == "not-between") && valueElements != 2)
                {
                    problems.Add(Error(operatorAttribute, "The operator \"" + op + "\" needs exactly two <value> elements."));
                }
            }
            else if (OperatorsWithValue.Contains(op))
            {
                if (!hasValueAttribute && valueElements == 0)
                {
                    problems.Add(Error(operatorAttribute, "The operator \"" + op + "\" needs a value=\"...\"."));
                }
            }
            else
            {
                problems.Add(Warning(operatorAttribute, "\"" + op + "\" is not a FetchXML operator this tool knows; Dataverse will reject it if it is not valid."));
            }
        }

        private static Problem Error(XObject node, string message)
        {
            var info = (IXmlLineInfo)node;
            return new Problem(info.HasLineInfo() ? info.LineNumber : 0, info.HasLineInfo() ? info.LinePosition : 0, message, true);
        }

        private static Problem Warning(XObject node, string message)
        {
            var info = (IXmlLineInfo)node;
            return new Problem(info.HasLineInfo() ? info.LineNumber : 0, info.HasLineInfo() ? info.LinePosition : 0, message, false);
        }

        private static string FirstSentence(string message)
        {
            var end = message.IndexOf(". Line ", StringComparison.Ordinal);
            return end > 0 ? message.Substring(0, end + 1) : message;
        }

        private static string Shorten(string text)
        {
            return text.Length > 30 ? text.Substring(0, 30) + "…" : text;
        }
    }
}
