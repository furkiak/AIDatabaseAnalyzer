namespace AIDatabaseAnalyzer.Controls;

/// <summary>A circular gauge that shows the schema health score (0–100).</summary>
public class ScoreRing : GraphicsView
{
    public static readonly BindableProperty ScoreProperty = BindableProperty.Create(
        nameof(Score), typeof(double), typeof(ScoreRing), 0.0,
        propertyChanged: (bindable, _, _) => ((ScoreRing)bindable).Invalidate());

    public static readonly BindableProperty GradeProperty = BindableProperty.Create(
        nameof(Grade), typeof(string), typeof(ScoreRing), "",
        propertyChanged: (bindable, _, _) => ((ScoreRing)bindable).Invalidate());

    public ScoreRing()
    {
        Drawable = new RingDrawable(this);
        WidthRequest = 132;
        HeightRequest = 132;
        if (Application.Current is { } app) app.RequestedThemeChanged += (_, _) => Invalidate();
    }

    public double Score
    {
        get => (double)GetValue(ScoreProperty);
        set => SetValue(ScoreProperty, value);
    }

    public string Grade
    {
        get => (string)GetValue(GradeProperty);
        set => SetValue(GradeProperty, value);
    }

    private sealed class RingDrawable : IDrawable
    {
        private readonly ScoreRing _owner;

        public RingDrawable(ScoreRing owner) => _owner = owner;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            bool dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            double score = Math.Clamp(_owner.Score, 0, 100);

            float thickness = 11;
            float size = Math.Min(dirtyRect.Width, dirtyRect.Height) - thickness - 2;
            float x = dirtyRect.Center.X - size / 2;
            float y = dirtyRect.Center.Y - size / 2;

            Color arc = score >= 80 ? Color.FromArgb(dark ? "#43D17A" : "#1F9D55")
                : score >= 55 ? Color.FromArgb(dark ? "#FFB340" : "#D98A00")
                : Color.FromArgb(dark ? "#FF6B61" : "#D92D20");

            canvas.StrokeSize = thickness;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeColor = dark ? Color.FromArgb("#2EFFFFFF") : Color.FromArgb("#1F3C3C43");
            canvas.DrawEllipse(x, y, size, size);

            double sweep = score / 100.0 * 360.0;
            if (sweep >= 1)
            {
                canvas.StrokeColor = arc;
                // 90° is twelve o'clock; the arc runs clockwise from there.
                canvas.DrawArc(x, y, size, size, 90, (float)(90 - Math.Min(sweep, 359.5)), true, false);
            }

            canvas.FontColor = dark ? Color.FromArgb("#F5F5F7") : Color.FromArgb("#1D1D1F");
            canvas.FontSize = 36;
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.DrawString(((int)Math.Round(score)).ToString(), dirtyRect.Center.X - 50, dirtyRect.Center.Y - 28, 100, 40,
                HorizontalAlignment.Center, VerticalAlignment.Center);

            canvas.FontColor = dark ? Color.FromArgb("#B7B7C2") : Color.FromArgb("#55555E");
            canvas.FontSize = 12;
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            if (!string.IsNullOrEmpty(_owner.Grade))
                canvas.DrawString($"Grade {_owner.Grade}", dirtyRect.Center.X - 50, dirtyRect.Center.Y + 12, 100, 20,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}
