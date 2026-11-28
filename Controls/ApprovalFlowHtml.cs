using System;
using System.Linq;
using System.Net;
using System.Text;
using ApprovalAdministrationTool.Services;

namespace ApprovalAdministrationTool.Controls
{
    /// <summary>
    /// Draws an approval's flow as HTML, left to right: the requester, then each step with the people it went to. Each
    /// person is a round profile photo (or their initials) with their name and job title under it and where they stand;
    /// "Currently with" and an arrow point down at those the approval is waiting on. Written for the preview pane's
    /// Internet Explorer engine (tables, no flexbox), and scrolls sideways when it does not fit.
    /// </summary>
    internal static class ApprovalFlowHtml
    {
        public const string Style =
            "div.flow{overflow-x:auto;overflow-y:hidden;margin:4px 0 6px 0;padding-bottom:4px;}" +
            "table.flow{border-collapse:collapse;}table.flow td{border:0;padding:0;vertical-align:top;}" +
            "td.stage{text-align:center;}div.stage{width:110px;}" + // fixed widths: a narrow pane scrolls instead of squeezing
            "div.head{height:20px;font-size:8.5pt;font-weight:600;color:#605e5c;white-space:nowrap;}" +
            "div.person{margin-bottom:10px;}" +
            "div.cue{height:32px;font-size:8pt;font-weight:600;color:#0f6cbd;line-height:13px;white-space:nowrap;}" +
            "div.cue span{font-size:11pt;}" +
            "div.ring{position:relative;width:56px;height:56px;margin:0 auto;}" +
            "div.circle{width:50px;height:50px;border-radius:50%;border:3px solid #c8c6c4;overflow:hidden;background:#f3f2f1;}" +
            "div.circle img{width:50px;height:50px;border-radius:50%;display:block;}" +
            "div.initials{width:50px;height:50px;line-height:50px;color:#fff;font-size:13pt;font-weight:600;}" +
            "div.waiting div.circle{border-color:#0f6cbd;}" +
            "div.approved div.circle{border-color:#107c10;}" +
            "div.rejected div.circle{border-color:#c50f1f;}" +
            "div.responded div.circle{border-color:#605e5c;}" +
            "div.notstarted div.circle{opacity:0.6;filter:alpha(opacity=60);}" +
            "div.badge{position:absolute;right:-2px;bottom:-2px;width:18px;height:18px;line-height:18px;border-radius:50%;" +
            "border:2px solid #fff;color:#fff;font-size:8pt;font-weight:bold;text-align:center;}" +
            "div.approved div.badge{background:#107c10;}div.rejected div.badge{background:#c50f1f;}" +
            "div.name{margin-top:5px;font-weight:600;line-height:14px;word-wrap:break-word;}" +
            "div.job{color:#605e5c;font-size:8.5pt;line-height:13px;word-wrap:break-word;}" +
            "div.state{font-size:8pt;line-height:13px;margin-top:1px;color:#605e5c;}" +
            "div.waiting div.state{color:#0f6cbd;}div.approved div.state{color:#107c10;}div.rejected div.state{color:#c50f1f;}" +
            "div.link{width:34px;}" +
            "div.line{position:relative;margin-top:79px;width:28px;height:0;border-top:2px solid #8a8886;}" +
            "div.line.later{border-top:2px dashed #c8c6c4;}" +
            "div.arrow{position:absolute;right:-1px;top:-6px;width:0;height:0;border-top:5px solid transparent;" +
            "border-bottom:5px solid transparent;border-left:7px solid #8a8886;}" +
            "div.line.later div.arrow{border-left-color:#c8c6c4;}";

        private static readonly string[] InitialsColours = { "#0f6cbd", "#8764b8", "#038387", "#ca5010", "#4f6bed", "#c239b3", "#498205", "#986f0b" };

        /// <param name="photo">The photo to show for a person, or null for their initials.</param>
        public static string Render(ApprovalFlow flow, Func<FlowPerson, byte[]> photo)
        {
            var html = new StringBuilder("<div class=\"flow\"><table class=\"flow\"><tr>");
            if (flow.Requester != null)
            {
                Stage(html, "Requested by", new[] { flow.Requester }, photo);
            }

            var multiStep = flow.Steps.Count > 1;
            foreach (var step in flow.Steps)
            {
                if (flow.Requester != null || step != flow.Steps[0])
                {
                    var later = flow.CurrentStep.HasValue && step.Number > flow.CurrentStep.Value;
                    html.Append("<td><div class=\"line").Append(later ? " later" : string.Empty).Append("\"><div class=\"arrow\"></div></div></td>");
                }

                Stage(html, multiStep ? "Step " + (step.Number + 1) : "Approvers", step.People, photo);
            }

            return html.Append("</tr></table></div>").ToString();
        }

        private static void Stage(StringBuilder html, string heading, System.Collections.Generic.IEnumerable<FlowPerson> people, Func<FlowPerson, byte[]> photo)
        {
            html.Append("<td class=\"stage\"><div class=\"stage\"><div class=\"head\">").Append(Encode(heading)).Append("</div>");
            foreach (var person in people)
            {
                Person(html, person, photo(person));
            }

            html.Append("</div></td>");
        }

        private static void Person(StringBuilder html, FlowPerson person, byte[] photo)
        {
            html.Append("<div class=\"person ").Append(person.State.ToString().ToLowerInvariant()).Append("\">");
            html.Append(person.State == FlowState.Waiting
                ? "<div class=\"cue\">Currently with<br><span>&#9660;</span></div>"
                : "<div class=\"cue\"></div>");

            html.Append("<div class=\"ring\"><div class=\"circle\">");
            if (photo != null && photo.Length > 0)
            {
                html.Append("<img alt=\"\" src=\"data:").Append(ImageType(photo)).Append(";base64,").Append(Convert.ToBase64String(photo)).Append("\">");
            }
            else
            {
                html.Append("<div class=\"initials\" style=\"background:").Append(Colour(person.Name)).Append("\">")
                    .Append(Encode(Initials(person.Name))).Append("</div>");
            }

            html.Append("</div>");
            if (person.State == FlowState.Approved || person.State == FlowState.Rejected)
            {
                html.Append("<div class=\"badge\">").Append(person.State == FlowState.Approved ? "&#10003;" : "&#10005;").Append("</div>");
            }

            html.Append("</div>");
            html.Append("<div class=\"name\">").Append(Encode(person.Name)).Append("</div>");
            if (!string.IsNullOrWhiteSpace(person.JobTitle))
            {
                html.Append("<div class=\"job\">").Append(Encode(person.JobTitle)).Append("</div>");
            }

            var state = StateText(person);
            if (state != null)
            {
                html.Append("<div class=\"state\">").Append(Encode(state)).Append("</div>");
            }

            html.Append("</div>");
        }

        private static string StateText(FlowPerson person)
        {
            switch (person.State)
            {
                case FlowState.Waiting: return "Waiting for response";
                case FlowState.Approved: return "Approved";
                case FlowState.Rejected: return "Rejected";
                case FlowState.Responded: return "Responded: " + person.Response;
                case FlowState.NotStarted: return "Not started";
                case FlowState.NoResponse: return "No response";
                default: return null;
            }
        }

        /// <summary>Up to two initials: of the first and last word of the name.</summary>
        public static string Initials(string name)
        {
            var words = (name ?? string.Empty).Split(new[] { ' ', '\t', '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(word => char.IsLetterOrDigit(word[0]))
                .ToList();
            if (words.Count == 0)
            {
                return "?";
            }

            var initials = words.Count == 1 ? words[0].Substring(0, 1) : words[0].Substring(0, 1) + words[words.Count - 1].Substring(0, 1);
            return initials.ToUpperInvariant();
        }

        /// <summary>The same colour for the same name every time.</summary>
        private static string Colour(string name)
        {
            var hash = 0;
            foreach (var c in name ?? string.Empty)
            {
                hash = unchecked(hash * 31 + c);
            }

            return InitialsColours[(hash & 0x7fffffff) % InitialsColours.Length];
        }

        private static string ImageType(byte[] image)
        {
            return image.Length > 3 && image[0] == 0x89 && image[1] == 0x50 ? "image/png"
                : image.Length > 3 && image[0] == 0x47 && image[1] == 0x49 ? "image/gif"
                : "image/jpeg";
        }

        private static string Encode(string text) => WebUtility.HtmlEncode(text ?? string.Empty);
    }
}
