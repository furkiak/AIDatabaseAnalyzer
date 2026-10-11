namespace AIDatabaseAnalyzer.Services.Infrastructure;

/// <summary>Small layout helpers shared by the screens.</summary>
public static class Layouts
{
    /// <summary>Keeps <paramref name="column"/> centred and no wider than <paramref name="maxWidth"/>, whatever the window size.</summary>
    public static void ConstrainWidth(VisualElement host, View column, double maxWidth, double margin = 56)
    {
        void Apply()
        {
            if (host.Width <= 0) return;
            column.WidthRequest = Math.Clamp(host.Width - margin, 320, maxWidth);
        }

        host.SizeChanged += (_, _) => Apply();
        Apply();
    }
}
