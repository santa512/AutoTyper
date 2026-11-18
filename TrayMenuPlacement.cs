namespace AutoTyper;

internal readonly record struct TrayMenuBounds(int X, int Y, int Width, int Height);

internal static class TrayMenuPlacement
{
    internal static TrayMenuBounds Calculate(int anchorX, int anchorY, TrayMenuBounds workArea, int width, int height, int margin)
    {
        margin = Math.Clamp(margin, 0, Math.Max(0, Math.Min(workArea.Width, workArea.Height) / 4));
        width = Math.Clamp(width, 1, Math.Max(1, workArea.Width - margin * 2));
        height = Math.Clamp(height, 1, Math.Max(1, workArea.Height - margin * 2));
        int x = Math.Clamp(anchorX - width, workArea.X + margin, workArea.X + workArea.Width - margin - width);
        int y = Math.Clamp(Math.Min(anchorY, workArea.Y + workArea.Height) - height - margin,
            workArea.Y + margin, workArea.Y + workArea.Height - margin - height);
        return new(x, y, width, height);
    }
}
