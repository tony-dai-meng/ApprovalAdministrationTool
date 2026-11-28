using System;
using System.Diagnostics;
using System.Net;
using System.Windows.Forms;
using Markdig;

namespace ApprovalAdministrationTool.Controls
{
    /// <summary>
    /// Shows an approval's details (Markdown, as written in the flow's approval action) rendered as HTML.
    /// Raw HTML in the Markdown is not rendered (shown as text), and links open in the default browser.
    /// </summary>
    public partial class WebContentControl : UserControl
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .DisableHtml()
            .Build();

        private const string Style =
            "body{font-family:'Segoe UI',sans-serif;font-size:10pt;margin:10px 12px;color:#201f1e;}" +
            "h1.title{font-size:14pt;margin:0 0 4px 0;}" +
            "p.meta{color:#605e5c;margin:0 0 10px 0;}" +
            "hr.split{border:0;border-top:1px solid #c8c6c4;margin:8px 0 12px 0;}" +
            "table{border-collapse:collapse;}th,td{border:1px solid #c8c6c4;padding:3px 6px;}" +
            "pre,code{font-family:Consolas,monospace;background:#f3f2f1;}pre{padding:6px;}" +
            "blockquote{border-left:3px solid #c8c6c4;margin-left:0;padding-left:10px;color:#605e5c;}" +
            ".empty{color:#605e5c;font-style:italic;}";

        private const string FlowElementId = "flow";

        private readonly WebBrowser browser;
        private string pendingFlowHtml; // the flow chart for the last page set
        private bool loading; // a page has been set and has not finished loading
        private int pageNumber; // of the last page set, stamped on its body
        private string page; // the last page set
        private int reloads; // times the last page was set again because another replaced it

        public WebContentControl()
        {
            InitializeComponent();

            browser = new WebBrowser
            {
                Dock = DockStyle.Fill,
                AllowWebBrowserDrop = false,
                IsWebBrowserContextMenuEnabled = false,
                ScriptErrorsSuppressed = true,
                WebBrowserShortcutsEnabled = true,
            };
            browser.Navigating += Browser_Navigating;
            browser.DocumentCompleted += Browser_DocumentCompleted;
            Controls.Add(browser);
            ShowMessage("Select an approval to see its details.");
        }

        /// <summary>
        /// Shows an approval: its title, an optional line under it, room for its flow chart (see <see cref="ShowFlow"/>),
        /// then its details rendered from Markdown.
        /// </summary>
        public void ShowApproval(string title, string subtitle, string detailsMarkdown)
        {
            var body = "<h1 class=\"title\">" + WebUtility.HtmlEncode(title ?? string.Empty) + "</h1>";
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                body += "<p class=\"meta\">" + WebUtility.HtmlEncode(subtitle) + "</p>";
            }

            body += "<div id=\"" + FlowElementId + "\"></div>";
            body += "<hr class=\"split\">";
            body += string.IsNullOrWhiteSpace(detailsMarkdown)
                ? "<p class=\"empty\">This approval has no details.</p>"
                : Markdown.ToHtml(detailsMarkdown, Pipeline);
            SetBody(body);
        }

        /// <summary>Shows a short message instead of an approval.</summary>
        public void ShowMessage(string message)
        {
            SetBody("<p class=\"empty\">" + WebUtility.HtmlEncode(message) + "</p>");
        }

        /// <summary>
        /// Puts the approval's flow chart (HTML from <see cref="ApprovalFlowHtml"/>) under its title, replacing the one
        /// shown before, without reloading the page. Ignored if the pane is showing a message.
        /// </summary>
        internal void ShowFlow(string html)
        {
            pendingFlowHtml = html;
            ApplyFlow();
        }

        /// <summary>The HTML in the flow chart's place (for tests), or null if there is none.</summary>
        internal string FlowHtml => browser.Document?.GetElementById(FlowElementId)?.InnerHtml;

        private void SetBody(string body)
        {
            pendingFlowHtml = null;
            loading = true;
            pageNumber++;
            reloads = 0;
            page =
                "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
                "<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">" +
                "<style>" + Style + ApprovalFlowHtml.Style + "</style></head><body data-page=\"" + pageNumber + "\">" + body + "</body></html>";
            browser.DocumentText = page;
        }

        /// <summary>
        /// A page set while another is still loading can be lost, or overwritten by the older one (or by a blank page), so a
        /// page that finishes loading and is not the last one set is replaced by it. Once the last one is in, its flow chart goes in too.
        /// </summary>
        private void Browser_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            // WebBrowser loads about:blank, then the page into it, and only reports the second; a blank page reported
            // here is a leftover navigation that replaced the page.
            var shown = browser.Document?.Body?.GetAttribute("data-page");
            if (shown != pageNumber.ToString())
            {
                if (reloads++ < 3)
                {
                    browser.DocumentText = page;
                }

                return;
            }

            loading = false;
            ApplyFlow();
        }

        /// <summary>Puts the chart into the page once the page has loaded.</summary>
        private void ApplyFlow()
        {
            if (pendingFlowHtml == null || loading)
            {
                return; // DocumentCompleted calls again
            }

            var element = browser.Document?.GetElementById(FlowElementId);
            if (element != null)
            {
                element.InnerHtml = pendingFlowHtml; // kept, in case the page has to be set again
            }
        }

        /// <summary>Keeps the pane on the rendered details: links open in the default browser (or mail app) instead.</summary>
        private void Browser_Navigating(object sender, WebBrowserNavigatingEventArgs e)
        {
            if (e.Url == null || e.Url.ToString() == "about:blank")
            {
                return;
            }

            e.Cancel = true;
            if (e.Url.Scheme == Uri.UriSchemeHttp || e.Url.Scheme == Uri.UriSchemeHttps || e.Url.Scheme == Uri.UriSchemeMailto)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(e.Url.ToString()) { UseShellExecute = true });
                }
                catch (Exception)
                {
                    // No default browser: nothing else to do.
                }
            }
        }
    }
}
