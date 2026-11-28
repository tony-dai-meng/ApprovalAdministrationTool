using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace ApprovalAdministrationTool.Services
{
    /// <summary>
    /// Reminds an approver of an approval waiting on them with a Teams chat message from the admin, through Microsoft
    /// Graph (as the admin: a delegated token), or an email from the admin where chats are not allowed. The message has
    /// the approval's title, the admin's note and a link to the approvals the approver has received. Nothing in
    /// Dataverse changes. No UI code: call from inside WorkAsync.
    /// </summary>
    internal sealed class ReminderService
    {
        public const string GraphResource = "https://graph.microsoft.com";

        private static readonly HttpClient Http = new HttpClient { BaseAddress = new Uri(GraphResource) };

        private readonly string accessToken;

        public ReminderService(string accessToken)
        {
            this.accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
        }

        /// <summary>The page in Power Automate listing the approvals the signed-in person has received.</summary>
        public static string ReceivedApprovalsLink(string environmentId)
        {
            return "https://make.powerautomate.com/environments/" + Uri.EscapeDataString(environmentId ?? string.Empty) + "/approvals/received";
        }

        /// <summary>The message posted to the approver, as HTML.</summary>
        public static string MessageHtml(string approvalTitle, string note, string link)
        {
            var html = new StringBuilder();
            html.Append("<p><b>Reminder:</b> the approval <b>").Append(WebUtility.HtmlEncode(approvalTitle ?? "(untitled)")).Append("</b> is waiting on your response.</p>");
            if (!string.IsNullOrWhiteSpace(note))
            {
                html.Append("<p>").Append(WebUtility.HtmlEncode(note.Trim()).Replace("\r\n", "\n").Replace("\n", "<br>")).Append("</p>");
            }

            html.Append("<p><a href=\"").Append(WebUtility.HtmlEncode(link)).Append("\">Open your approvals</a> (or the Approvals app in Teams).</p>");
            return html.ToString();
        }

        /// <summary>
        /// Sends the reminder from <paramref name="adminObjectId"/> to <paramref name="approverObjectId"/> (Entra ID object
        /// IDs). Tries, in order: the one-on-one Teams chat the two already share (needs only ChatMessage.Send), a new
        /// one-on-one chat (needs Chat.Create too), then an email from the admin (Mail.Send). Returns null when a Teams chat
        /// was sent, or a note saying it went by email instead.
        /// </summary>
        public string Send(Guid adminObjectId, Guid approverObjectId, string approvalTitle, string note, string link)
        {
            if (adminObjectId == approverObjectId)
            {
                throw new InvalidOperationException("That approver is you, so there is no one to remind.");
            }

            var html = MessageHtml(approvalTitle, note, link);
            var message = "{\"body\":{\"contentType\":\"html\",\"content\":" + Json(html) + "}}";
            var refusals = new List<string>();

            // A one-on-one chat's ID is made from the two people's object IDs, so an existing chat can be posted in
            // without being allowed to list or create chats. Which ID comes first is not documented, so try both.
            var a = adminObjectId.ToString("D").ToLowerInvariant();
            var b = approverObjectId.ToString("D").ToLowerInvariant();
            var first = string.CompareOrdinal(a, b) < 0 ? a : b;
            var second = first == a ? b : a;
            foreach (var chatId in new[] { "19:" + first + "_" + second + "@unq.gbl.spaces", "19:" + second + "_" + first + "@unq.gbl.spaces" })
            {
                if (TrySend(HttpMethod.Post, "/v1.0/chats/" + chatId + "/messages", message, refusals, "existing chat") != null)
                {
                    return null;
                }
            }

            var created = TryOpenChat(adminObjectId, approverObjectId, refusals);
            if (created != null && TrySend(HttpMethod.Post, "/v1.0/chats/" + created + "/messages", message, refusals, "new chat") != null)
            {
                return null;
            }

            // No chat could be used: email the approver instead, from the admin's mailbox.
            var user = TrySend(HttpMethod.Get, "/v1.0/users/" + approverObjectId + "?$select=mail,userPrincipalName", null, refusals, "approver's email address");
            var address = user == null ? null : (Field(user, "mail") ?? Field(user, "userPrincipalName"));
            if (address != null)
            {
                var mail = "{\"message\":{\"subject\":" + Json("Reminder: " + (approvalTitle ?? "(untitled)") + " is waiting on your response")
                    + ",\"body\":{\"contentType\":\"HTML\",\"content\":" + Json(html) + "}"
                    + ",\"toRecipients\":[{\"emailAddress\":{\"address\":" + Json(address) + "}}]},\"saveToSentItems\":true}";
                if (TrySend(HttpMethod.Post, "/v1.0/me/sendMail", mail, refusals, "email") != null)
                {
                    return "Teams would not let the tool send a chat, so the reminder was emailed to " + address + " instead.";
                }
            }

            throw new InvalidOperationException("Microsoft Graph refused the reminder, so nothing was sent. Your sign-in may not be allowed to send "
                + "Teams chats (ChatMessage.Send, plus Chat.Create for a first chat) or email (Mail.Send).\n" + string.Join("\n", refusals));
        }

        /// <summary>Creates the one-on-one chat (or gets the existing one), or null if Graph refuses.</summary>
        private string TryOpenChat(Guid adminObjectId, Guid approverObjectId, List<string> refusals)
        {
            string Member(Guid user) => "{\"@odata.type\":\"#microsoft.graph.aadUserConversationMember\",\"roles\":[\"owner\"],"
                + "\"user@odata.bind\":\"https://graph.microsoft.com/v1.0/users('" + user + "')\"}";

            var reply = TrySend(HttpMethod.Post, "/v1.0/chats", "{\"chatType\":\"oneOnOne\",\"members\":[" + Member(adminObjectId) + "," + Member(approverObjectId) + "]}", refusals, "create chat");
            if (reply == null)
            {
                return null;
            }

            var id = Field(reply, "id");
            if (id == null || id.IndexOfAny(new[] { '/', '?', '#' }) >= 0)
            {
                refusals.Add("create chat: Teams did not say which chat to post in.");
                return null;
            }

            return id;
        }

        /// <summary>The first string value of <paramref name="name"/> in a JSON reply, or null.</summary>
        private static string Field(string json, string name)
        {
            var match = Regex.Match(json, "\"" + Regex.Escape(name) + "\"\\s*:\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>Sends one Graph request. Returns the reply, or null (with the reason added to <paramref name="refusals"/>) if Graph refused it.</summary>
        private string TrySend(HttpMethod method, string path, string body, List<string> refusals, string step)
        {
            using (var request = new HttpRequestMessage(method, path))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (body != null)
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                }

                using (var response = Http.SendAsync(request).GetAwaiter().GetResult())
                {
                    var text = response.Content == null ? string.Empty : response.Content.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
                    if (response.IsSuccessStatusCode)
                    {
                        return text;
                    }

                    var reason = Field(text, "message");
                    refusals.Add(step + ": " + (int)response.StatusCode + " " + response.ReasonPhrase
                        + (reason == null ? string.Empty : " (" + (reason.Length > 160 ? reason.Substring(0, 160) + "..." : reason) + ")"));
                    return null;
                }
            }
        }

        private static string Json(string value)
        {
            var builder = new StringBuilder("\"");
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            return builder.Append('"').ToString();
        }
    }
}
