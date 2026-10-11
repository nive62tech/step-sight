using System.Drawing;
using System.Drawing.Imaging;
using DesktopGuide.Core;
using DesktopGuide.Windows;
using Xunit;

namespace DesktopGuide.AppTests;

public sealed class GuidanceImageTests
{
    [Fact] public void ActionAllowsRemoteClockNoiseButRejectsTargetChangesOverlaysAndKeyboardDrift()
    {
        using var capture = new WindowsCapture();
        var geometry = new Geometry(Guid.NewGuid(),Guid.NewGuid(),new(0,0,800,450),800,450,96,"fixture",Guid.NewGuid(),new(0,0,1,1),new(0,0,1,1));
        var action = new InputAction("click",.6,.55,"left"); var original = Image(geometry);
        var clockTick = Image(geometry,new(20,20,2,15));
        Assert.True(capture.IsActionTargetCurrent(original,clockTick,action));
        Assert.False(capture.IsActionTargetCurrent(original,Image(geometry,new(470,230,2,2)),action));
        Assert.False(capture.IsActionTargetCurrent(original,Image(geometry,new(10,10,300,180)),action));
        Assert.False(capture.IsActionTargetCurrent(original,Image(geometry with { Id=Guid.NewGuid() }),action));
        Assert.False(capture.IsActionTargetCurrent(original,clockTick,new("key_chord",Chord:"Enter")));
    }
    private static ScreenFrame Image(Geometry geometry, Rectangle? changed = null)
    {
        using var bitmap = new Bitmap(800, 450);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.White);
            g.FillRectangle(Brushes.Green, 400, 200, 150, 80);
            if (changed is { } rect) g.FillRectangle(Brushes.Black, rect);
        }
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
        return new(new(Guid.NewGuid(), DateTimeOffset.UtcNow, geometry.ScopeId, geometry.Id, 800, 450, true), geometry, stream.ToArray(), changed?.ToString() ?? "original");
    }
    [Fact] public void GuidanceAllowsUnrelatedCaretButRejectsTargetOrLayoutChanges()
    {
        using var capture = new WindowsCapture();
        var geometry = new Geometry(Guid.NewGuid(), Guid.NewGuid(), new(0,0,800,450),800,450,96,"fixture",Guid.NewGuid(),new(0,0,1,1),new(0,0,1,1));
        var target = new Target("rectangle", "normalized_source_image", geometry.Id, new(.5,.45,.68,.62),"Synthetic green button");
        var original = Image(geometry);
        Assert.True(capture.IsGuidanceTargetCurrent(original, Image(geometry,new(20,20,2,15)),target));
        Assert.False(capture.IsGuidanceTargetCurrent(original, Image(geometry,new(470,230,2,2)),target));
        Assert.False(capture.IsGuidanceTargetCurrent(original, Image(geometry,new(10,10,300,180)),target));
        Assert.False(capture.IsGuidanceTargetCurrent(original, Image(geometry with { Id=Guid.NewGuid() }),target));
    }
}
