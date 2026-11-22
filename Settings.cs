namespace ApprovalAdministrationTool
{
    /// <summary>
    /// The tool's settings, saved by XrmToolBox's SettingsManager (Settings\ApprovalAdministrationTool.xml).
    /// </summary>
    /// <remarks>
    /// This class must be XML serializable
    /// </remarks>
    public class Settings
    {
        public string LastUsedOrganizationWebappUrl { get; set; }

        /// <summary>
        /// The application (client) ID of the Entra ID app registration Reassign signs in with to get a token for the Teams
        /// Approvals API. Asked for the first time Reassign is used.
        /// </summary>
        public string TeamsApprovalsClientId { get; set; }

        /// <summary>
        /// The Teams Approvals sign-in (MSAL token cache, including the refresh token), encrypted with Windows DPAPI for the
        /// current Windows user and base64-encoded. Tried first next session; the sign-in window opens only if it fails.
        /// </summary>
        public string TeamsApprovalsTokenCache { get; set; }
    }
}
