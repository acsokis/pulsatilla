using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Pulsatilla.Wpf;

/// <summary>Reparents existing pages without duplicating their controls or operation handlers.</summary>
public sealed class WorkflowNavigation
{
    private readonly TabControl _main;
    private readonly Dictionary<string, TabItem> _pages = new(StringComparer.Ordinal);
    public ContentControl NetworkHost { get; } = new();
    public ContentControl ApplicationsHost { get; } = new();
    public ContentControl AttachmentsHost { get; } = new();
    public ContentControl VpnHost { get; } = new();
    public ContentControl SampleHost { get; } = new();
    public TextBlock PathSummary { get; } = Text("Resolving the local outbound route…");
    public TextBlock InspectionSummary { get; } = Text("Inspection stopped. Select Network Path, then Inspect Traffic.");
    public TextBlock SecuritySummary { get; } = Text("Local review. Findings are indicators, not a malware diagnosis.");
    public Button InspectButton { get; } = Button("Inspect Traffic");
    public Button StopButton { get; } = Button("Stop Inspection");
    public Button PathButton { get; } = Button("Network Path");
    public event EventHandler? NavigationChanged;

    public WorkflowNavigation(Window window, TabControl main)
    {
        _main = main;
        var old = main.Items.Cast<TabItem>().ToArray();
        if (old.Length != 10) throw new InvalidOperationException("Expected the ten checkpoint pages.");
        main.Items.Clear();
        main.TabStripPlacement = Dock.Left;
        var dashboard = old[0];
        dashboard.Tag = "Dashboard";
        main.Items.Add(dashboard); _pages.Add("Dashboard", dashboard);
        var network = Group("Network");
        Add(network, "Network Path", NetworkHost);

        // Move the adapter list out of the unconstrained dashboard scroll content.
        var adapters = (ListBox)window.FindName("AdapterList");
        adapters.SetResourceReference(Control.ForegroundProperty, "Theme_D8E4DA");
        var adapterBorder = Ancestor<Border>(adapters);
        if (adapterBorder?.Parent is Panel dashboardGrid)
        {
            var row = Grid.GetRow(adapterBorder); var column = Grid.GetColumn(adapterBorder);
            dashboardGrid.Children.Remove(adapterBorder);
            adapterBorder.Margin = new Thickness(10);
            Grid.SetRow(adapterBorder, 0); Grid.SetColumn(adapterBorder, 0);
            adapters.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            adapters.SetValue(ScrollViewer.CanContentScrollProperty, true);
            // A star-sized list remains bounded by the Network page viewport.
            if (adapterBorder.Child is DockPanel dock)
            {
                var children = dock.Children.Cast<UIElement>().ToArray(); dock.Children.Clear();
                var bounded = new Grid();
                bounded.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                bounded.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                foreach (var child in children)
                { Grid.SetRow(child, ReferenceEquals(child, adapters) ? 1 : 0); bounded.Children.Add(child); }
                adapterBorder.Child = bounded;
            }
            Add(network, "Adapters", adapterBorder);
            var path = new StackPanel();
            path.Children.Add(Text("CURRENT NETWORK PATH", 17));
            path.Children.Add(PathSummary);
            path.Children.Add(InspectButton); path.Children.Add(PathButton);
            var card = new Border { Padding = new Thickness(16), Child = path };
            card.SetResourceReference(Border.BackgroundProperty, "Panel");
            Grid.SetRow(card, row); Grid.SetColumn(card, column); dashboardGrid.Children.Add(card);
        }
        var inspect = Group("Inspect");
        Move(inspect, "Live Traffic", old[1]);
        Add(inspect, "Applications", ApplicationsHost);
        Move(inspect, "Connections", old[4]);
        Move(inspect, "Packet Inspector", old[6]);
        var protocol = new StackPanel { Margin = new Thickness(18) };
        protocol.Children.Add(Text("Protocol Analysis — passive packet observations", 17));
        var histogram = Text("");
        histogram.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Text") { Source = window.FindName("HistogramLabel") });
        protocol.Children.Add(histogram); Add(inspect, "Protocol Analysis", Scroll(protocol));
        var sources = new StackPanel { Margin = new Thickness(18) };
        foreach (var (name, label) in new[] { ("TopIpSourcesTree", "Top IP Sources"), ("TopSoftwareSourcesTree", "Top Software Sources") })
        {
            var tree = (TreeView)window.FindName(name);
            if (tree.Parent is Panel parent) parent.Children.Remove(tree);
            sources.Children.Add(Text(label, 17)); sources.Children.Add(tree);
        }
        Add(inspect, "Traffic Sources", Scroll(sources));
        var scan = Group("Scan"); Move(scan, "Host / Port / Service Scan", old[3]);
        var scanActions = ((TextBox)window.FindName("NetworkRangeInput")).Parent as Panel;
        foreach (var name in new[] { "SecurityScanButton", "SecurityStatusLabel" })
        {
            var control = (FrameworkElement)window.FindName(name);
            if (control.Parent is Panel parent && scanActions is not null)
            { parent.Children.Remove(control); scanActions.Children.Add(control); }
        }
        if (scanActions is not null)
        {
            var warning = Text("ACTIVE NETWORK OPERATION — host discovery and port/service probes run only when requested. Scan only networks you are authorized to inspect.");
            scanActions.Children.Insert(0, warning);
        }
        Add(scan, "HEX / Sample Analysis", SampleHost);
        var security = Group("Security");
        var overview = new Grid { Margin = new Thickness(10) };
        overview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        overview.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        overview.Children.Add(SecuritySummary);
        var events = (ListBox)window.FindName("EventsList");
        if (events.Parent is Border eventBorder)
        {
            eventBorder.Child = null;
            if (eventBorder.Parent is Grid packetGrid)
            {
                packetGrid.Children.Remove(eventBorder);
                packetGrid.ColumnDefinitions.Clear();
                packetGrid.ColumnDefinitions.Add(new ColumnDefinition());
                foreach (UIElement child in packetGrid.Children) Grid.SetColumn(child, 0);
            }
        }
        if (events.Parent is Panel eventsParent) eventsParent.Children.Remove(events);
        Grid.SetRow(events, 1); overview.Children.Add(events);
        Add(security, "Overview", overview); Move(security, "Events", old[2]);
        var protection = (TabControl)window.FindName("ProtectionTabs");
        var protectionPages = protection.Items.Cast<TabItem>().ToArray();
        protection.Items.Remove(protectionPages[1]); protection.Items.Remove(protectionPages[2]); protection.Items.Remove(protectionPages[3]);
        Move(security, "Firewall", old[8]);
        var email = Group("Email"); Move(email, "Inspector & Sender Rules", protectionPages[2]);
        Add(email, "Attachments", AttachmentsHost); Move(email, "Local Exposure Reports", protectionPages[3]);
        var vpn = Group("VPN"); Add(vpn, "Status / Profiles / Privacy", VpnHost);
        old[5].Header = "History"; old[5].Tag = "History"; main.Items.Add(old[5]); _pages.Add("History", old[5]);
        var settings = Group("Settings"); Move(settings, "System & Diagnostics", old[7]);
        Move(settings, "Appearance & Monitoring", protectionPages[1]);
        old[9].Tag = "About"; main.Items.Add(old[9]); _pages.Add("About", old[9]);
        PathButton.Click += (_, _) => Select("Network Path");
        main.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, main)) NavigationChanged?.Invoke(this, EventArgs.Empty); };
        main.SelectedItem = dashboard;
        StopButton.IsEnabled = false;
        var container = (Grid)main.Parent;
        container.RowDefinitions.Insert(1, new RowDefinition { Height = GridLength.Auto });
        foreach (UIElement child in container.Children) if (Grid.GetRow(child) >= 1) Grid.SetRow(child, Grid.GetRow(child) + 1);
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(StopButton, Dock.Right); bar.Children.Add(StopButton); bar.Children.Add(InspectionSummary);
        Grid.SetRow(bar, 1); container.Children.Add(bar);
    }

    public string CurrentPage => (_main.SelectedItem as TabItem)?.Tag as string ?? "Dashboard";
    public bool Select(string name)
    {
        if (!_pages.TryGetValue(name, out var tab)) return false;
        for (var current = tab; current is not null;)
        {
            if (current.Parent is not TabControl owner) break;
            owner.SelectedItem = current; current = owner.Parent as TabItem;
        }
        return true;
    }

    private TabControl Group(string name)
    {
        var tabs = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        var item = new TabItem { Header = name, Tag = name, Content = tabs, MinWidth = 112, Padding = new Thickness(12, 9, 12, 9) };
        item.SetResourceReference(Control.ForegroundProperty, "Theme_D8E4DA");
        item.SetResourceReference(Control.BackgroundProperty, "Theme_101B13");
        _main.Items.Add(item); _pages.Add(name, item); return tabs;
    }
    private void Add(TabControl owner, string name, object content) => Move(owner, name, new TabItem { Content = content });
    private void Move(TabControl owner, string name, TabItem tab)
    {
        tab.Header = name; tab.Tag = name; owner.Items.Add(tab); _pages[name] = tab;
        tab.SetResourceReference(Control.ForegroundProperty, "Theme_D8E4DA");
        tab.SetResourceReference(Control.BackgroundProperty, "Theme_101B13");
    }
    private static T? Ancestor<T>(DependencyObject node) where T : DependencyObject
    {
        for (var parent = LogicalTreeHelper.GetParent(node); parent is not null; parent = LogicalTreeHelper.GetParent(parent))
            if (parent is T result) return result;
        return null;
    }
    public static TextBlock Text(string value, double size = 13)
    {
        var text = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 7) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Theme_E6F2E8"); return text;
    }
    public static Button Button(string value)
    {
        var button = new Button { Content = value, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 6, 0, 6) };
        button.SetResourceReference(FrameworkElement.StyleProperty, "ActionButton");
        button.SetResourceReference(Control.ForegroundProperty, "Theme_E6F2E8");
        button.SetResourceReference(Control.BackgroundProperty, "Theme_14241A"); return button;
    }
    public static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
}
