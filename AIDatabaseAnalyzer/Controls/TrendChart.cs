namespace AIDatabaseAnalyzer.Controls;

/// <summary>A small line chart of health scores (0–100) over time.</summary>
public sealed class TrendChart : GraphicsView
{
    public static readonly BindableProperty PointsProperty = BindableProperty.Create(
        nameof(Points), typeof(IReadOnlyList<TrendPoint>), typeof(TrendChart), Array.Empty<TrendPoint>(),
        propertyChanged: (b, _, _) => ((TrendChart)b).Invalidate());

    public TrendChart()
    {
        Drawable = new ChartDrawable(this);
        HeightRequest = 120;
        if (Application.Current is { } app) app.RequestedThemeChanged += (_, _) => Invalidate();
    }

    public IReadOnlyList<TrendPoint> Points
    {
        get => (IReadOnlyList<TrendPoint>)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    private sealed class ChartDrawable : IDrawable
    {
        private readonly TrendChart _owner;

        public ChartDrawable(TrendChart owner) => _owner = owner;

        public void Draw(ICanvas canvas, RectF rect)
        {
            var points = _owner.Points;
            bool dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            var grid = dark ? Color.FromArgb("#33FFFFFF") : Color.FromArgb("#1A000000");
            var text = dark ? Color.FromArgb("#99FFFFFF") : Color.FromArgb("#8A000000");
            var accent = dark ? Color.FromArgb("#7D7AFF") : Color.FromArgb("#5E5CE6");

            float left = 30, right = rect.Width - 12, top = 10, bottom = rect.Height - 22;
            canvas.FontSize = 10;
            canvas.FontColor = text;
            foreach (int level in new[] { 0, 50, 100 })
            {
                float y = bottom - (bottom - top) * level / 100f;
                canvas.StrokeColor = grid;
                canvas.StrokeSize = 1;
                canvas.DrawLine(left, y, right, y);
                canvas.DrawString(level.ToString(), 0, y - 7, left - 6, 14, HorizontalAlignment.Right, VerticalAlignment.Center);
            }

            if (points.Count == 0) return;
            float X(int i) => points.Count == 1 ? (left + right) / 2 : left + (right - left) * i / (points.Count - 1);
            float Y(double score) => bottom - (bottom - top) * (float)Math.Clamp(score, 0, 100) / 100f;

            if (points.Count > 1)
            {
                var area = new PathF();
                area.MoveTo(X(0), bottom);
                for (int i = 0; i < points.Count; i++) area.LineTo(X(i), Y(points[i].Score));
                area.LineTo(X(points.Count - 1), bottom);
                area.Close();
                canvas.FillColor = accent.WithAlpha(0.14f);
                canvas.FillPath(area);

                var line = new PathF();
                line.MoveTo(X(0), Y(points[0].Score));
                for (int i = 1; i < points.Count; i++) line.LineTo(X(i), Y(points[i].Score));
                canvas.StrokeColor = accent;
                canvas.StrokeSize = 2.5f;
                canvas.StrokeLineJoin = LineJoin.Round;
                canvas.DrawPath(line);
            }

            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                var color = p.Score >= 80 ? Color.FromArgb("#1F9D55") : p.Score >= 55 ? Color.FromArgb("#D98A00") : Color.FromArgb("#D92D20");
                canvas.FillColor = color;
                canvas.FillCircle(X(i), Y(p.Score), 4.5f);
                if (i == 0 || i == points.Count - 1 || points.Count <= 6)
                    canvas.DrawString(p.Label, X(i) - 40, bottom + 4, 80, 14, HorizontalAlignment.Center, VerticalAlignment.Top);
            }
        }
    }
}

public sealed record TrendPoint(double Score, string Label);
