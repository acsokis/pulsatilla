using System.Windows;

namespace Pulsatilla.Wpf;

public partial class MainWindow
{
    private WorkflowNavigation? _workflow;
    private void InitializeWorkflow()
    {
        _workflow = new WorkflowNavigation(this, MainTabs);
        _workflow.InspectButton.Click += StartCaptureButton_Click;
        _workflow.StopButton.Click += StopCaptureButton_Click;
        _workflow.NetworkHost.Content = WorkflowNavigation.Text("Network Path — route-based selection is being integrated.");
        _workflow.ApplicationsHost.Content = WorkflowNavigation.Text("Application details — local signature and hash inspection is being integrated.");
        _workflow.AttachmentsHost.Content = WorkflowNavigation.Text("Attachment inspection — local bounded analysis is being integrated.");
        _workflow.VpnHost.Content = WorkflowNavigation.Text("VPN — provider integration is not configured. No tunnel is started.");
        _workflow.SampleHost.Content = WorkflowNavigation.Text("HEX / Sample Analysis — bounded local viewer is being integrated.");
    }
}
