using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace ApprovalAdministrationTool.Services
{
    /// <summary>
    /// Gets a delegated Microsoft Entra ID token, as the person using the tool, for the Teams Approvals API
    /// (resource https://approvals.teams.microsoft.com) in the Dataverse environment's tenant. Reassign calls that API.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The person's XrmToolBox connection cannot be reused: XrmToolBox keeps no password, and its token is only valid for
    /// Dataverse. So the person signs in once more, with their connection's account pre-filled. The sign-in is kept in
    /// <see cref="Settings.TeamsApprovalsTokenCache"/> (encrypted with DPAPI for the current Windows user) and tried first;
    /// the sign-in window only opens when there is no saved sign-in, it no longer works, or it is for another account.
    /// </para>
    /// <para>
    /// Signs in as Microsoft's own Teams client (<see cref="DefaultClientId"/>) unless an app registration is set in
    /// <see cref="Settings.TeamsApprovalsClientId"/>, so most tenants need no app registration. Using Microsoft's client
    /// ID is unofficial: a tenant can block it, or Entra can refuse it for this resource. If so, an app registration is a
    /// public client with the redirect URI <see cref="RedirectUri"/> and a delegated permission on the Approvals API.
    /// </para>
    /// </remarks>
    internal sealed class TeamsApprovalsAuth
    {
        /// <summary>The Microsoft Teams desktop client: a public client Microsoft registers in every tenant.</summary>
        public const string DefaultClientId = "1fec8e78-bce4-4aaf-ab1b-5451cc387264";

        public const string ApiBaseUrl = "https://approvals.teams.microsoft.com";
        public const string RedirectUri = "https://login.microsoftonline.com/common/oauth2/nativeclient";

        private static readonly string[] Scopes = { ApiBaseUrl + "/.default" };
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ApprovalAdministrationTool.TeamsApprovals");

        private readonly IPublicClientApplication app;

        /// <param name="clientId">The app registration's application (client) ID.</param>
        /// <param name="tenantId">The Dataverse environment's tenant.</param>
        /// <param name="settings">Holds the saved sign-in; updated whenever MSAL changes it.</param>
        /// <param name="saveSettings">Saves <paramref name="settings"/> to disk.</param>
        public TeamsApprovalsAuth(string clientId, Guid tenantId, Settings settings, Action saveSettings)
        {
            app = PublicClientApplicationBuilder.Create(clientId)
                .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
                .WithRedirectUri(RedirectUri)
                .Build();

            app.UserTokenCache.SetBeforeAccess(args => args.TokenCache.DeserializeMsalV3(Unprotect(settings.TeamsApprovalsTokenCache)));
            app.UserTokenCache.SetAfterAccess(args =>
            {
                if (args.HasStateChanged)
                {
                    settings.TeamsApprovalsTokenCache = Protect(args.TokenCache.SerializeMsalV3());
                    saveSettings();
                }
            });
        }

        /// <summary>
        /// A token for the Teams Approvals API as <paramref name="userName"/>: from the saved sign-in if it is for that
        /// account and still works, otherwise from the Microsoft sign-in window with that account pre-filled.
        /// Call on the UI thread (the window is shown over <paramref name="owner"/>).
        /// </summary>
        /// <param name="userName">The XrmToolBox connection's account (sign-in name), or null to let the person choose.</param>
        public async Task<AuthenticationResult> GetTokenAsync(string userName, IntPtr owner)
        {
            var accounts = (await app.GetAccountsAsync().ConfigureAwait(true)).ToList();
            var account = string.IsNullOrEmpty(userName)
                ? accounts.FirstOrDefault()
                : accounts.FirstOrDefault(a => string.Equals(a.Username, userName, StringComparison.OrdinalIgnoreCase));

            if (account != null)
            {
                try
                {
                    return await app.AcquireTokenSilent(Scopes, account).ExecuteAsync().ConfigureAwait(true);
                }
                catch (MsalUiRequiredException)
                {
                    // Expired, revoked or needs consent: sign in again below.
                }
            }

            var interactive = app.AcquireTokenInteractive(Scopes).WithParentActivityOrWindow(owner);
            interactive = account != null
                ? interactive.WithAccount(account)
                : !string.IsNullOrEmpty(userName) ? interactive.WithLoginHint(userName) : interactive.WithPrompt(Prompt.SelectAccount);
            return await interactive.ExecuteAsync().ConfigureAwait(true);
        }

        private static string Protect(byte[] data)
        {
            return data == null || data.Length == 0
                ? null
                : Convert.ToBase64String(ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser));
        }

        /// <summary>The saved sign-in, or nothing if there is none or it cannot be decrypted (another user or PC).</summary>
        private static byte[] Unprotect(string saved)
        {
            if (string.IsNullOrEmpty(saved))
            {
                return null;
            }

            try
            {
                return ProtectedData.Unprotect(Convert.FromBase64String(saved), Entropy, DataProtectionScope.CurrentUser);
            }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException)
            {
                return null;
            }
        }
    }
}
