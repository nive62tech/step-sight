using System.Drawing;
using System.Drawing.Imaging;
using DesktopGuide.Core;
using DesktopGuide.Windows;
using Xunit;

namespace DesktopGuide.AppTests;

public sealed class GuidanceImageTests
{
    [Fact] public void AssistantMasksClipToTheSelectedMonitorIncludingNegativeCoordinates()
    {
        var masks = AssistantCaptureMasks.Project(new(-1920, 0, 1920, 1080), [new(-100, 950, 300, 240), new(100, 0, 100, 100), new(-2000, -50, 180, 200)]);
        Assert.Equal(2, masks.Count);
        foreach (var mask in masks) mask.Validate();
        Assert.Equal(1820 / 1920d, masks[0].Left); Assert.Equal(1, masks[0].Right); Assert.Equal(1, masks[0].Bottom);
        Assert.Equal(0, masks[1].Left); Assert.Equal(0, masks[1].Top); Assert.Equal(100 / 1920d, masks[1].Right);
    }
    [Fact] public void MovingAssistantMasksDoesNotTriggerActivityButCannotAuthorizeInput()
    {
        using var capture = new WindowsCapture();
        var geometry = new Geometry(Guid.NewGuid(), Guid.NewGuid(), new(0,0,800,450),800,450,96,"fixture",Guid.NewGuid(),new(0,0,1,1),new(0,0,1,1));
        var before = Image(geometry, new(100,100,200,180)) with { PrivacyMasks = [new(.125,100/450d,.375,280/450d)] };
        var after = Image(geometry, new(220,100,200,180)) with { PrivacyMasks = [new(.275,100/450d,.525,280/450d)] };
        Assert.False(capture.HasMeaningfulChange(before, after));
        Assert.False(capture.IsActionTargetCurrent(before, after, new("click",.8,.8,"left")));
        Assert.Throws<SafetyException>(() => after.RequireUnmaskedPoint(.35,.4));
        var changedTask = Image(geometry, new(550,10,220,120)) with { PrivacyMasks = after.PrivacyMasks };
        Assert.True(capture.HasMeaningfulChange(before, changedTask));
    }
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
