using System;
using System.Collections.Generic;

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

        /// <summary>
        /// Rows assigned to the admin for an action and not yet given back. Each is added just before the row is assigned
        /// and removed once it is back with its owner, so anything left here (XrmToolBox closed or crashed mid-action) is
        /// offered back the next time the tool connects to that environment.
        /// </summary>
        public List<BorrowedRow> BorrowedRows { get; set; } = new List<BorrowedRow>();
    }

    /// <summary>A row the tool assigned to the admin for an action (see <see cref="Settings.BorrowedRows"/>).</summary>
    public class BorrowedRow
    {
        public string OrganizationUrl { get; set; }
        public string EntityName { get; set; }
        public Guid RowId { get; set; }
        public string Title { get; set; }
        public string OwnerEntityName { get; set; }
        public Guid OwnerId { get; set; }
        public string OwnerName { get; set; }
        public Guid BorrowerId { get; set; }
        public DateTime BorrowedOn { get; set; }
    }
}
