using System.ComponentModel.Composition;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace ApprovalAdministrationTool
{
    // Do not forget to update version number and author (company attribute) in AssemblyInfo.cs class
    // Categories are not ExportMetadata: they are set on the tool's Tool Library listing on xrmtoolbox.com when publishing.
    // Planned: Power Automate, Processes, Security
    // (Tool Library categories: Data, Customizations, Development, Configuration, Security, Migration, Solutions,
    //  Troubleshooting, Documentation, Plugins, User Interface, Comparison, Processes, Reporting, Portal,
    //  Power Automate, Power Pages, Power BI)
    [Export(typeof(IXrmToolBoxPlugin)),
        ExportMetadata("Name", "PA Approval Admin Tool"),
        ExportMetadata("Description", "Empower your Approval Administration Functions: Delegate, Approve on behalf of another user and more!"),
        // 32x32 and 80x80 base64 PNGs of Media\Images\ThumbsUp.png, kept in Resources\PluginIcons.cs
        ExportMetadata("SmallImageBase64", PluginIcons.SmallImageBase64),
        ExportMetadata("BigImageBase64", PluginIcons.BigImageBase64),
        ExportMetadata("BackgroundColor", "Lavender"),
        ExportMetadata("PrimaryFontColor", "Black"),
        ExportMetadata("SecondaryFontColor", "Gray")]
    public class ApprovalPlugin : PluginBase
    {
        // If the tool ever ships a third-party DLL that XrmToolBox does not already provide, either merge it into this
        // assembly (e.g. ILMerge) or add an AppDomain.AssemblyResolve handler that returns null when it cannot find the
        // file. Never throw from that handler: it is shared by the whole XrmToolBox process and every other tool.
        public override IXrmToolBoxPluginControl GetControl()
        {
            return new ApprovalAdminControl();
        }
    }
}
