using System.ComponentModel.Composition;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace BN.WorkflowDoc.XrmToolBox;

[Export(typeof(IXrmToolBoxPlugin))]
[ExportMetadata("Name", "BridgeNexa Workflow Documenter")]
[ExportMetadata("Description", "Document classic workflows, dialogs, and actions from a live Dataverse environment.")]
[ExportMetadata("SmallImageBase64", null)]
[ExportMetadata("BigImageBase64", null)]
[ExportMetadata("BackgroundColor", "#F1EEE8")]
[ExportMetadata("PrimaryFontColor", "#1F2937")]
[ExportMetadata("SecondaryFontColor", "#6B7280")]
public sealed class Plugin : PluginBase
{
    public override IXrmToolBoxPluginControl GetControl()
    {
        return new WorkflowDocumenterControl();
    }
}