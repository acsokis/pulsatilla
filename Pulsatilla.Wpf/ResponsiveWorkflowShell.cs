using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Pulsatilla.Wpf;

/// <summary>One-time layout adaptation after workflow reparenting; retains controls and operation handlers.</summary>
public static class ResponsiveWorkflowShell
{
    public sealed record InitialWindowBounds(double Width, double Height, double MinimumWidth, double MinimumHeight, bool BelowDesignMinimum);
    public static InitialWindowBounds FitInitialBounds(double requestedWidth, double requestedHeight, Rect workArea)
    {
        if (!double.IsFinite(workArea.Width) || !double.IsFinite(workArea.Height) || workArea.Width <= 0 || workArea.Height <= 0)
            throw new ArgumentException("A finite positive logical work area is required.");
        var minimumWidth = Math.Min(640, workArea.Width); var minimumHeight = Math.Min(360, workArea.Height);
        var width = double.IsFinite(requestedWidth) && requestedWidth > 0 ? requestedWidth : 1280;
        var height = double.IsFinite(requestedHeight) && requestedHeight > 0 ? requestedHeight : 800;
        return new(Math.Clamp(width, minimumWidth, workArea.Width), Math.Clamp(height, minimumHeight, workArea.Height),
            minimumWidth, minimumHeight, workArea.Width < 640 || workArea.Height < 360);
    }
    private static readonly DependencyProperty AppliedProperty = DependencyProperty.RegisterAttached("Applied", typeof(bool), typeof(ResponsiveWorkflowShell));
    public static void Apply(Window window, TabControl main)
    {
        if ((bool)window.GetValue(AppliedProperty)) return;
        if (main.Parent is not Grid shell) throw new ArgumentException("Apply responsive layout after workflow navigation while MainTabs still belongs to its shell Grid.");
        window.SetValue(AppliedProperty, true);
        var initial = FitInitialBounds(window.Width, window.Height, SystemParameters.WorkArea);
        window.MinWidth = initial.MinimumWidth; window.MinHeight = initial.MinimumHeight;
        window.Width = initial.Width; window.Height = initial.Height;
        shell.RowDefinitions[0].Height = GridLength.Auto;
        var header = shell.Children.OfType<Grid>().FirstOrDefault(element => Grid.GetRow(element) == 0);
        if (header is not null)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var child in header.Children.Cast<UIElement>().ToArray())
            {
                header.Children.Remove(child);
                if (child is StackPanel { Orientation: Orientation.Horizontal } stack && Grid.GetColumn(stack) == 1)
                {
                    foreach (var action in stack.Children.Cast<UIElement>().ToArray())
                    {
                        stack.Children.Remove(action);
                        if (action is FrameworkElement element) element.Margin = new Thickness(4, 3, 4, 3);
                        wrap.Children.Add(action);
                    }
                }
                else wrap.Children.Add(child);
            }
            header.ColumnDefinitions.Clear(); header.Children.Add(wrap); header.Margin = new Thickness(0);
        }
        foreach (var footer in shell.Children.OfType<DockPanel>().Where(element => Grid.GetRow(element) > Grid.GetRow(main)).ToArray())
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) }; Grid.SetRow(wrap, Grid.GetRow(footer));
            foreach (var child in footer.Children.Cast<UIElement>().ToArray())
            {
                footer.Children.Remove(child);
                if (child is FrameworkElement element) element.Margin = new Thickness(4, 2, 8, 2);
                if (child is TextBlock text) text.TextWrapping = TextWrapping.Wrap;
                wrap.Children.Add(child);
            }
            shell.Children.Remove(footer); shell.Children.Add(wrap); shell.RowDefinitions[Grid.GetRow(wrap)].Height = GridLength.Auto;
        }
        // Existing forms with horizontal action rows can wrap without losing names or delegates.
        foreach (var stack in LogicalElements(main).OfType<StackPanel>().Where(s => s.Orientation == Orientation.Horizontal && s.Parent is Panel && s.Children.OfType<Control>().Any(c => c is Button or ComboBox or TextBox)).ToArray())
        {
            var parent = (Panel)stack.Parent; var index = parent.Children.IndexOf(stack);
            var wrap = new WrapPanel { Margin = stack.Margin, HorizontalAlignment = stack.HorizontalAlignment, VerticalAlignment = stack.VerticalAlignment };
            Grid.SetRow(wrap, Grid.GetRow(stack)); Grid.SetColumn(wrap, Grid.GetColumn(stack)); Grid.SetColumnSpan(wrap, Grid.GetColumnSpan(stack)); Grid.SetRowSpan(wrap, Grid.GetRowSpan(stack));
            DockPanel.SetDock(wrap, DockPanel.GetDock(stack));
            foreach (var child in stack.Children.Cast<UIElement>().ToArray()) { stack.Children.Remove(child); wrap.Children.Add(child); }
            if (stack.Name.Length > 0) { wrap.Margin = new Thickness(0); stack.Orientation = Orientation.Vertical; stack.Children.Add(wrap); }
            else { parent.Children.RemoveAt(index); parent.Children.Insert(index, wrap); }
        }
        Expander? adapterInformation = null;
        Border? adapterBorder = null;
        if (window.FindName("AdapterList") is ListBox { Parent: Grid adapterGrid })
        {
            adapterBorder = adapterGrid.Parent as Border;
            var information = adapterGrid.Children.OfType<StackPanel>().FirstOrDefault(element => Grid.GetRow(element) == 0);
            if (information is not null)
            {
                adapterGrid.Children.Remove(information);
                adapterInformation = new Expander { Header = "Adapter selection information", Content = information, IsExpanded = true };
                adapterInformation.SetResourceReference(Control.ForegroundProperty, "Theme_E6F2E8");
                adapterGrid.Children.Add(adapterInformation);
            }
        }
        var lastCompact = (bool?)null;
        void Adapt()
        {
            var width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
            var compact = width < 980;
            shell.Margin = new Thickness(compact ? 8 : 18);
            if (adapterInformation is not null) adapterInformation.IsExpanded = !compact;
            if (adapterBorder is not null) { adapterBorder.Padding = new Thickness(compact ? 4 : 18); adapterBorder.Margin = new Thickness(compact ? 4 : 10); }
            if (lastCompact == compact) return; lastCompact = compact;
            main.TabStripPlacement = compact ? Dock.Top : Dock.Left;
            main.Template = Template(compact);
            foreach (var item in main.Items.OfType<TabItem>()) item.Padding = new Thickness(compact ? 6 : 12, compact ? 3 : 9, compact ? 6 : 12, compact ? 3 : 9);
            foreach (var tabs in LogicalElements(main).OfType<TabControl>().Where(t => !ReferenceEquals(t, main)).ToArray())
            {
                tabs.TabStripPlacement = Dock.Top; tabs.Template = Template(true);
                foreach (var item in tabs.Items.OfType<TabItem>()) item.Padding = new Thickness(compact ? 6 : 9, compact ? 3 : 6, compact ? 6 : 9, compact ? 3 : 6);
            }
        }
        window.SizeChanged += (_, _) => Adapt(); Adapt();
    }
    public static IEnumerable<DependencyObject> LogicalElements(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in LogicalElements(child)) yield return item;
    }
    private static ControlTemplate Template(bool top)
    {
        var definitions = top ? "<Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/></Grid.RowDefinitions>" : "<Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>";
        var headerPosition = top ? "MaxHeight='58' HorizontalScrollBarVisibility='Auto' VerticalScrollBarVisibility='Disabled'" : "MaxWidth='190' HorizontalScrollBarVisibility='Disabled' VerticalScrollBarVisibility='Auto'";
        var contentPosition = top ? "Grid.Row='1'" : "Grid.Column='1'";
        return (ControlTemplate)XamlReader.Parse($"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TabControl'><Grid ClipToBounds='True'>{definitions}<ScrollViewer x:Name='HeaderScroller' {headerPosition} CanContentScroll='False' Focusable='False'><TabPanel IsItemsHost='True' KeyboardNavigation.TabNavigation='Once' Margin='0,0,0,4'/></ScrollViewer><ContentPresenter {contentPosition} ContentSource='SelectedContent' Margin='0,4,0,0' /></Grid></ControlTemplate>");
    }
}
