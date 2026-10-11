using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;
using System.Numerics;

namespace DesktopGuide.App;

/// <summary>A lit, deforming 3D surface projected locally; no web view or downloaded asset.</summary>
public sealed class AgentBlob : Control
{
    public static readonly StyledProperty<bool> AutonomousProperty = AvaloniaProperty.Register<AgentBlob, bool>(nameof(Autonomous));
    public static readonly StyledProperty<bool> ListeningProperty = AvaloniaProperty.Register<AgentBlob, bool>(nameof(Listening));
    public static readonly StyledProperty<bool> BusyProperty = AvaloniaProperty.Register<AgentBlob, bool>(nameof(Busy));
    public static readonly StyledProperty<bool> ReducedMotionProperty = AvaloniaProperty.Register<AgentBlob, bool>(nameof(ReducedMotion));
    public bool Autonomous { get => GetValue(AutonomousProperty); set => SetValue(AutonomousProperty, value); }
    public bool Listening { get => GetValue(ListeningProperty); set => SetValue(ListeningProperty, value); }
    public bool Busy { get => GetValue(BusyProperty); set => SetValue(BusyProperty, value); }
    public bool ReducedMotion { get => GetValue(ReducedMotionProperty); set => SetValue(ReducedMotionProperty, value); }
    private readonly DispatcherTimer animation = new() { Interval = TimeSpan.FromMilliseconds(42) };
    private readonly Stopwatch clock = new();
    private double? previewTime;
    static AgentBlob() => AffectsRender<AgentBlob>(AutonomousProperty, ListeningProperty, BusyProperty, ReducedMotionProperty);
    public AgentBlob()
    {
        IsHitTestVisible = false;
        animation.Tick += (_, _) => { if (IsEffectivelyVisible && !ReducedMotion) InvalidateVisual(); };
        AttachedToVisualTree += (_, _) => { clock.Start(); animation.Start(); };
        DetachedFromVisualTree += (_, _) => { animation.Stop(); clock.Stop(); };
    }
    internal void SetPreviewTime(double seconds) { previewTime = seconds; InvalidateVisual(); }
    private readonly record struct Face(Point A, Point B, Point C, double Depth, double Light);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var time = ReducedMotion ? 0 : previewTime ?? clock.Elapsed.TotalSeconds;
        var cx = Bounds.Width / 2; var cy = Bounds.Height * .46;
        var radius = size * .32; var pulse = Listening ? .05 * Math.Sin(time * 8) : .018 * Math.Sin(time * 1.7);
        var hue = Autonomous ? Color.FromRgb(227, 33, 68) : Color.FromRgb(29, 176, 91);
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(20, hue.R, hue.G, hue.B)), null, new Rect(cx-size*.31, cy+size*.37, size*.62, size*.055));
        var glow = new RadialGradientBrush { GradientStops = new() { new(Color.FromArgb(55,hue.R,hue.G,hue.B),0),new(Colors.Transparent,1) } };
        context.DrawEllipse(glow,null,new Rect(cx-radius*1.65,cy-radius*1.65,radius*3.3,radius*3.3));
        const int rings = 22, segments = 36;
        var vertices = new Vector3[(rings+1)*(segments+1)];
        var lights = new double[vertices.Length]; var points = new Point[vertices.Length];
        var rotation = Matrix4x4.CreateRotationY((float)(time*(Busy ? .6 : .22))) * Matrix4x4.CreateRotationZ(.16f);
        var lightDirection = Vector3.Normalize(new(-.4f, -.55f, .8f));
        for (var j = 0; j <= rings; j++) for (var k = 0; k <= segments; k++)
        {
            var theta = Math.PI*j/rings; var phi = Math.Tau*k/segments;
            var normal = new Vector3((float)(Math.Sin(theta)*Math.Cos(phi)), (float)Math.Cos(theta), (float)(Math.Sin(theta)*Math.Sin(phi)));
            var wobble = 1 + .09*Math.Sin(normal.X*3.5 + time*.75)*Math.Sin(normal.Y*3 - time*.6)
                + .075*Math.Cos(normal.Z*3.2 + time*.6) + pulse;
            var pos = Vector3.Transform(normal*(float)wobble, rotation);
            var n = Vector3.Normalize(Vector3.Transform(normal, rotation));
            var perspective = 3.8/(3.8-pos.Z); var index = j*(segments+1)+k;
            vertices[index] = pos;
            points[index] = new(cx+pos.X*radius*perspective, cy-pos.Y*radius*perspective);
            var lightingNormal = new Vector3(n.X, -n.Y, n.Z);
            var diffuse = Math.Max(0, Vector3.Dot(lightingNormal, lightDirection));
            var specular = Math.Pow(Math.Max(0, Vector3.Dot(lightingNormal, Vector3.Normalize(lightDirection+Vector3.UnitZ))), 25);
            lights[index] = .16+.72*diffuse+.65*specular;
        }
        var faces = new List<Face>(rings*segments*2);
        void Add(int a, int b, int c)
        {
            if ((vertices[a].Z+vertices[b].Z+vertices[c].Z)/3 < -.12) return;
            faces.Add(new(points[a],points[b],points[c],(vertices[a].Z+vertices[b].Z+vertices[c].Z)/3,
                (lights[a]+lights[b]+lights[c])/3));
        }
        for (var j = 0; j < rings; j++) for (var k = 0; k < segments; k++)
        {
            var a = j*(segments+1)+k; var b = a+segments+1;
            Add(a,b,a+1); Add(a+1,b,b+1);
        }
        foreach (var f in faces.OrderBy(x => x.Depth))
        {
            byte Shade(byte channel) => (byte)Math.Clamp(channel*Math.Min(f.Light,1)+Math.Max(0,f.Light-1)*190,0,255);
            var brush = new SolidColorBrush(Color.FromRgb(Shade(hue.R),Shade(hue.G),Shade(hue.B)));
            var geometry = new StreamGeometry();
            using (var g = geometry.Open()) { g.BeginFigure(f.A,true); g.LineTo(f.B); g.LineTo(f.C); g.EndFigure(true); }
            context.DrawGeometry(brush,new Pen(brush,.7),geometry);
        }
        if (Listening)
        {
            var y = cy+size*.34;
            for (var i = 0; i < 5; i++)
            {
                var height = size*(ReducedMotion ? .06 : .035+.06*(.5+.5*Math.Sin(time*7+i*.9)));
                context.DrawRectangle(new SolidColorBrush(hue),null,new Rect(cx+(i-2)*size*.064-size*.015,y-height/2,size*.03,height),size*.015,size*.015);
            }
        }
    }
}
