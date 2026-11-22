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

        private readonly WebBrowser browser;

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
            Controls.Add(browser);
            ShowMessage("Select an approval to see its details.");
        }

        /// <summary>Shows an approval: its title, an optional line under it, then its details rendered from Markdown.</summary>
        public void ShowApproval(string title, string subtitle, string detailsMarkdown)
        {
            var body = "<h1 class=\"title\">" + WebUtility.HtmlEncode(title ?? string.Empty) + "</h1>";
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                body += "<p class=\"meta\">" + WebUtility.HtmlEncode(subtitle) + "</p>";
            }

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

        private void SetBody(string body)
        {
            browser.DocumentText =
                "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
                "<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">" +
                "<style>" + Style + "</style></head><body>" + body + "</body></html>";
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
