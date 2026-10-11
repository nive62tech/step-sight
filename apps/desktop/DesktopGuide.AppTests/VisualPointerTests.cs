using DesktopGuide.App.Services;
using DesktopGuide.Core;
using DesktopGuide.Platform;
using DesktopGuide.Windows;
using Xunit;

namespace DesktopGuide.AppTests;

public sealed class VisualPointerTests
{
    private sealed class Tracker : IWindowTracker
    {
        public bool Current = true;
        public bool Covered;
        public bool IsLocked => false;
        public bool IsForegroundAndVisible(CaptureSource s) => true;
        public bool IsCurrentGeometry(CaptureSource s, Geometry g) => Volatile.Read(ref Current);
        public bool IsPointVisible(CaptureSource s, int x, int y) => !Covered;
        public bool HasMeaningfulChange(ScreenFrame a, ScreenFrame b) => false;
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task VisualAnimationUsesObservationOnlyAndCancelsBeforeFinalMarker(bool cancel)
    {
        var source = new CaptureSource(Guid.NewGuid(), Guid.NewGuid(), "Synthetic fixture", "window");
        var geometry = new Geometry(Guid.NewGuid(),source.ScopeId,new(0,0,800,450),800,450,96,"fixture",source.TargetId,new(0,0,1,1),new(0,0,1,1));
        var frame = new ScreenFrame(new(Guid.NewGuid(),DateTimeOffset.UtcNow,source.ScopeId,geometry.Id,800,450,true),geometry,[1],"fixture");
        var target = new Target("rectangle","normalized_source_image",geometry.Id,new(.5,.5,.6,.6),"Synthetic target");
        var permission = new PermissionSupervisor(); permission.GrantFromLocalConsent(source.ScopeId,InteractionMode.Guide,true,false,false);
        using var activity = new PreviewEnvironment(); var pointer = new WindowsPointer(permission,new Tracker(),activity);
        var travel=0; var shown=0;
        pointer.TravelShown += _ => { travel++; if (cancel) permission.Revoke(); };
        pointer.TargetShown += _ => shown++;
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pointer.ShowVisualAsync(frame,target,source,permission.Cancellation));
        else await pointer.ShowVisualAsync(frame,target,source,permission.Cancellation);
        Assert.Equal(cancel ? 1 : 23,travel); Assert.Equal(cancel ? 0 : 1,shown); Assert.False(activity.IsAssistantInputActive);
        Assert.Throws<SafetyException>(() => permission.GuideBatch(frame,true,() => throw new Exception("Input must stay disarmed")));
        if (!cancel) Assert.Equal(new PhysicalRect(400,225,80,45), pointer.TargetBounds);
        pointer.Hide(); Assert.Null(pointer.TargetBounds);
    }
    [Fact]
    public async Task VisibleMarkerAndOutlineAreClearedWhenTheWindowLosesFocus()
    {
        var source = new CaptureSource(Guid.NewGuid(), Guid.NewGuid(), "Synthetic fixture", "window");
        var geometry = new Geometry(Guid.NewGuid(),source.ScopeId,new(0,0,800,450),800,450,96,"fixture",source.TargetId,new(0,0,1,1),new(0,0,1,1));
        var frame = new ScreenFrame(new(Guid.NewGuid(),DateTimeOffset.UtcNow,source.ScopeId,geometry.Id,800,450,true),geometry,[1],"fixture");
        var target = new Target("rectangle","normalized_source_image",geometry.Id,new(.5,.5,.6,.6),"Synthetic target");
        var permission = new PermissionSupervisor(); permission.GrantFromLocalConsent(source.ScopeId,InteractionMode.Guide,true,false,false);
        using var activity = new PreviewEnvironment(); var tracker = new Tracker();
        var pointer = new WindowsPointer(permission, tracker, activity) { ReducedMotion = true };
        var hidden = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pointer.TargetHidden += () => hidden.TrySetResult();
        try
        {
            await pointer.ShowVisualAsync(frame, target, source, permission.Cancellation);
            Assert.NotNull(pointer.TargetBounds); Volatile.Write(ref tracker.Current, false);
            await hidden.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Null(pointer.TargetBounds);
        }
        finally { pointer.Hide(); }
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task OwnDockMovesBeforeVisibilityCheckButStopDuringPreparationPreventsMarker(bool stop)
    {
        var source = new CaptureSource(Guid.NewGuid(), Guid.NewGuid(), "Synthetic fixture", "window");
        var geometry = new Geometry(Guid.NewGuid(), source.ScopeId, new(0, 0, 800, 450), 800, 450, 144, "fixture", source.TargetId, new(0, 0, 1, 1), new(0, 0, 1, 1));
        var frame = new ScreenFrame(new(Guid.NewGuid(), DateTimeOffset.UtcNow, source.ScopeId, geometry.Id, 800, 450, true), geometry, [1], "fixture");
        var target = new Target("rectangle", "normalized_source_image", geometry.Id, new(.5, .5, .6, .6), "Synthetic target");
        var permission = new PermissionSupervisor(); permission.GrantFromLocalConsent(source.ScopeId, InteractionMode.Guide, true, false, false);
        using var activity = new PreviewEnvironment(); var tracker = new Tracker { Covered = true };
        var pointer = new WindowsPointer(permission, tracker, activity) { ReducedMotion = true };
        var shown = 0;
        pointer.TargetShown += _ => shown++;
        pointer.PrepareTargetAsync = (bounds, token) =>
        {
            Assert.Equal(new PhysicalRect(400, 225, 80, 45), bounds);
            tracker.Covered = false;
            if (stop) permission.Revoke();
            return Task.CompletedTask;
        };
        try
        {
            if (stop) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pointer.ShowVisualAsync(frame, target, source, permission.Cancellation));
            else await pointer.ShowVisualAsync(frame, target, source, permission.Cancellation);
            Assert.Equal(stop ? 0 : 1, shown); Assert.False(activity.IsAssistantInputActive);
        }
        finally { pointer.Hide(); }
    }
}
