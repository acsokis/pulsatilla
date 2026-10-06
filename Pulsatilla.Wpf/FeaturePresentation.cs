using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Pulsatilla.Wpf;

/// <summary>Shared semantic presentation; static resources and text badges, with no animations or shadows.</summary>
public static class FeaturePresentation
{
    public static object Header(FeatureDescriptor feature)
    {
        if (feature.Tier == FeatureTier.Community) return feature.Name;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = feature.Name, VerticalAlignment = VerticalAlignment.Center });
        var label = feature.Tier == FeatureTier.ExperimentalCore ? "CORE · EXPERIMENTAL" : "CORE";
        var text = new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold };
        text.SetResourceReference(TextBlock.ForegroundProperty, "CoreAccentBrush");
        var badge = new Border { Child = text, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(9, 0, 0, 0), CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1) };
        badge.SetResourceReference(Border.BorderBrushProperty, "CoreBorderBrush");
        badge.SetResourceReference(Border.BackgroundProperty, "CoreHeaderBrush");
        AutomationProperties.SetName(badge, feature.Name + ": " + label);
        row.Children.Add(badge); return row;
    }
    public static UIElement Card(FeatureDescriptor feature, UIElement content)
    {
        if (feature.Tier == FeatureTier.Community) return content;
        var layout = new DockPanel();
        var header = new ContentControl { Content = Header(feature), Margin = new Thickness(12, 10, 12, 10) };
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header); layout.Children.Add(content);
        var card = new Border { Child = layout, BorderThickness = new Thickness(1), Margin = new Thickness(8), CornerRadius = new CornerRadius(5) };
        card.SetResourceReference(Border.BorderBrushProperty, "CoreBorderBrush");
        card.SetResourceReference(Border.BackgroundProperty, "Panel");
        AutomationProperties.SetName(card, feature.Name + (feature.Tier == FeatureTier.ExperimentalCore ? ": Core experimental feature" : ": Core feature"));
        return card;
    }
}
