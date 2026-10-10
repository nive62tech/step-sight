namespace DesktopGuide.Core;

public sealed record PhysicalRect(int Left, int Top, int Width, int Height)
{
    public bool Contains(int x, int y) => x >= Left && y >= Top && x < Left + Width && y < Top + Height;
}

public sealed record Geometry(Guid Id, Guid ScopeId, PhysicalRect Source, int ImageWidth, int ImageHeight,
    double Dpi, string MonitorId, Guid LocalTargetId, Bounds ContentBounds, Bounds CropBounds)
{
    public (int X, int Y) Map(double x, double y)
    {
        if (Source.Width <= 0 || Source.Height <= 0 || ImageWidth <= 0 || ImageHeight <= 0
            || !double.IsFinite(Dpi) || Dpi <= 0) throw new SafetyException("Invalid capture geometry");
        ContentBounds.Validate(); CropBounds.Validate();
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < ContentBounds.Left || x > ContentBounds.Right
            || y < ContentBounds.Top || y > ContentBounds.Bottom
            || ContentBounds.Left == ContentBounds.Right || ContentBounds.Top == ContentBounds.Bottom)
            throw new SafetyException("Point is invalid or in padding");
        var sourceX = CropBounds.Left + (x - ContentBounds.Left) / (ContentBounds.Right - ContentBounds.Left) * (CropBounds.Right - CropBounds.Left);
        var sourceY = CropBounds.Top + (y - ContentBounds.Top) / (ContentBounds.Bottom - ContentBounds.Top) * (CropBounds.Bottom - CropBounds.Top);
        return (Source.Left + (int)Math.Round(sourceX * (Source.Width - 1), MidpointRounding.AwayFromZero),
            Source.Top + (int)Math.Round(sourceY * (Source.Height - 1), MidpointRounding.AwayFromZero));
    }
}

public sealed record ScreenFrame(Observation Observation, Geometry Geometry, byte[] Png, string PixelDigest)
{
    public IReadOnlyList<Bounds> PrivacyMasks { get; init; } = [];
    public void RequireUnmaskedPoint(double x, double y)
    {
        if (PrivacyMasks.Any(mask => x >= mask.Left && x <= mask.Right && y >= mask.Top && y <= mask.Bottom))
            throw new SafetyException("Input target is inside a privacy mask");
    }
    public void Validate()
    {
        if (!Observation.PrivacyFiltered || Observation.GeometryId != Geometry.Id || Observation.ScopeId != Geometry.ScopeId
            || Observation.ImageWidth != Geometry.ImageWidth || Observation.ImageHeight != Geometry.ImageHeight
            || Png.Length is 0 or > 5 * 1024 * 1024 || string.IsNullOrEmpty(PixelDigest))
            throw new SafetyException("Invalid or unfiltered observation");
    }
}
