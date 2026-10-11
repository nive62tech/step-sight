using Avalonia;
using Avalonia.Media;

namespace DesktopGuide.App;

public static class ModeTheme
{
    public static void Apply(bool autonomous)
    {
        if (Application.Current is not { } app) return;
        var colors = autonomous
            ? new[] { "#FFF0F2", "#541724", "#FFF8F9", "#F7DCE2", "#E6B8C3", "#BB2343", "#815664", "#FFE1E5", "#DBA6B3", "#D43454", "#260A12", "#37121D" }
            : new[] { "#EDF8F0", "#153D29", "#F8FFF9", "#DDEFE2", "#BCD8C5", "#167247", "#526F5D", "#E3F2DD", "#B9D3AC", "#218957", "#0B2115", "#152F20" };
        string[] names = ["ModeBackground", "ModeForeground", "ModeSurface", "ModeSecondary", "ModeBorder", "ModeAccent", "ModeMuted", "ModeNotice", "ModeNoticeBorder", "ModeHover", "ModeDark", "ModeDarkSurface"];
        for (var i = 0; i < names.Length; i++) app.Resources[names[i]] = Brush.Parse(colors[i]);
        app.Resources["SystemAccentColor"] = Color.Parse(colors[5]);
        app.Resources["SystemAccentColorDark1"] = Color.Parse(colors[5]);
        app.Resources["SystemAccentColorLight1"] = Color.Parse(colors[9]);
    }
}
