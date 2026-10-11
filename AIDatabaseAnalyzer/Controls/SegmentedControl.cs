namespace AIDatabaseAnalyzer.Controls;

/// <summary>
/// A macOS style segmented control: a glass capsule with one highlighted segment.
/// Colours come from the style classes Segmented / Segment / SegmentSelected so the control follows light and dark mode.
/// </summary>
public class SegmentedControl : ContentView
{
    public static readonly BindableProperty ItemsProperty = BindableProperty.Create(
        nameof(Items), typeof(IEnumerable<string>), typeof(SegmentedControl), null,
        propertyChanged: (bindable, _, _) => ((SegmentedControl)bindable).Rebuild());

    public static readonly BindableProperty SelectedIndexProperty = BindableProperty.Create(
        nameof(SelectedIndex), typeof(int), typeof(SegmentedControl), 0, BindingMode.TwoWay,
        propertyChanged: (bindable, _, _) => ((SegmentedControl)bindable).UpdateSelection());

    private readonly Grid _grid;

    public SegmentedControl()
    {
        _grid = new Grid { ColumnSpacing = 2, Padding = new Thickness(3) };
        Content = new Border { StyleClass = new[] { "Segmented" }, Content = _grid };
        HorizontalOptions = LayoutOptions.Start;
    }

    public IEnumerable<string>? Items
    {
        get => (IEnumerable<string>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    private void Rebuild()
    {
        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();

        int index = 0;
        foreach (string text in Items ?? Array.Empty<string>())
        {
            int captured = index;
            var label = new Label { Text = text };
            var segment = new Border { Content = label };

            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => SelectedIndex = captured;
            segment.GestureRecognizers.Add(tap);

            _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            _grid.Add(segment, index);
            index++;
        }

        UpdateSelection();
    }

    private void UpdateSelection()
    {
        int index = 0;
        foreach (var child in _grid.Children)
        {
            if (child is not Border segment) continue;
            bool selected = index == SelectedIndex;

            segment.StyleClass = selected ? new[] { "Segment", "SegmentSelected" } : new[] { "Segment" };
            if (segment.Content is Label label)
                label.StyleClass = selected ? new[] { "SegmentLabel", "SegmentLabelSelected" } : new[] { "SegmentLabel" };
            index++;
        }
    }
}
