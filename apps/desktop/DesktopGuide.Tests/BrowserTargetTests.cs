using DesktopGuide.Core;
using DesktopGuide.Platform;
using Xunit;

namespace DesktopGuide.Tests;

public sealed class BrowserTargetTests
{
    private static ScreenFrame Frame(int dpi = 144, int left = -1920, int top = 40)
    {
        var geometry = new Geometry(Guid.NewGuid(), Guid.NewGuid(), new(left, top, 1920, 1080), 1600, 900, dpi,
            "left-monitor", Guid.NewGuid(), new(0, 0, 1, 1), new(0, 0, 1, 1));
        return new(new(Guid.NewGuid(), DateTimeOffset.UtcNow, geometry.ScopeId, geometry.Id, 1600, 900, true), geometry, [1], "fixture");
    }
    private static GuidanceStep Step(ScreenFrame frame, string label = "New Project", string action = "click")
        => new(Guid.NewGuid(), frame.Observation.Id, "Open a new project", "Start a project", new("rectangle", "normalized_source_image",
            frame.Geometry.Id, new(.1, .1, .3, .2), label), action, [new("visible_text", "Create project")], [], [], []);
    private static BrowserControl Button => new("page.button1", "New Project", "button", new(-410, 160, 140, 40));

    [Theory]
    [InlineData(96)] [InlineData(120)] [InlineData(144)] [InlineData(192)]
    public void BrowserPixelsCorrectAMisplacedVisualBoxWithoutDoubleScaling(int dpi)
    {
        var frame = Frame(dpi); var step = Step(frame);
        var match = BrowserTargetMatcher.Match(frame, step, [Button], true);
        Assert.NotNull(match.Target);
        var bounds = match.Target.Bounds;
        Assert.Equal((-410, 160), frame.Geometry.Map(bounds.Left, bounds.Top));
        Assert.Equal((-271, 199), frame.Geometry.Map(bounds.Right, bounds.Bottom));
        Assert.NotEqual(step.Target!.Bounds, bounds);
        Assert.Equal(step.Target.Bounds, new Bounds(.1, .1, .3, .2));
    }
    [Fact]
    public void DuplicateLabelsNeverUseVisualProximityToGuess()
    {
        var frame = Frame();
        var second = Button with { Id = "page.button2", Rectangle = new(-1200, 700, 140, 40) };
        var match = BrowserTargetMatcher.Match(frame, Step(frame), [Button, second], true);
        Assert.Null(match.Target); Assert.Contains("Several", match.Message);
    }
    [Fact]
    public void CaseAndWhitespaceNormalizeButSimilarLabelsNeverFuzzyMatch()
    {
        var frame = Frame();
        Assert.NotNull(BrowserTargetMatcher.Match(frame, Step(frame, " new  PROJECT "), [Button], true).Target);
        Assert.Null(BrowserTargetMatcher.Match(frame, Step(frame, "Project"), [Button], true).Target);
        Assert.Null(BrowserTargetMatcher.Match(frame, Step(frame, "New Project button"), [Button], true).Target);
    }
    [Fact]
    public void DisabledHiddenPasswordClippedAndMaskedControlsCannotBeTargets()
    {
        var frame = Frame(); var step = Step(frame);
        foreach (var control in new[] { Button with { Enabled = false }, Button with { Offscreen = true },
            Button with { Password = true }, Button with { Rectangle = new(-2000, 160, 140, 40) } })
            Assert.Null(BrowserTargetMatcher.Match(frame, step, [control], true).Target);
        var masked = frame with { PrivacyMasks = [new(.78, .1, .79, .2)] };
        Assert.Null(BrowserTargetMatcher.Match(masked, step, [Button], true).Target);
    }
    [Fact]
    public void LearnerOnlyManualButtonCanBeGroundedWithoutInputPermission()
    {
        var frame = Frame();
        Assert.NotNull(BrowserTargetMatcher.Match(frame, Step(frame, action: "manual"), [Button], true).Target);
    }
    [Fact]
    public void TypeTargetMustBeAnEditableControlAndCannotBePassword()
    {
        var frame = Frame(); var step = Step(frame, "New Project", "type");
        Assert.Null(BrowserTargetMatcher.Match(frame, step, [Button], true).Target);
        Assert.NotNull(BrowserTargetMatcher.Match(frame, step, [Button with { Role = "edit" }], true).Target);
        Assert.Null(BrowserTargetMatcher.Match(frame, step, [Button with { Role = "edit", Password = true }], true).Target);
    }
    [Fact]
    public void IncompleteTreeAndStaleObservationCannotConfirmAControl()
    {
        var frame = Frame(); var step = Step(frame);
        Assert.Null(BrowserTargetMatcher.Match(frame, step, [Button], false).Target);
        Assert.Null(BrowserTargetMatcher.Match(frame, step with { SourceObservationId = Guid.NewGuid() }, [Button], true).Target);
    }
    [Fact]
    public void ComputerUsePointCanGroundAnUnlabeledIconWithoutGuessingDuplicateLabels()
    {
        var frame = Frame();
        var bounds = BrowserTargetMatcher.ImageBounds(frame, Button.Rectangle)!;
        var center = bounds.Center;
        var step = Step(frame) with { Target = new("rectangle", "normalized_source_image", frame.Geometry.Id,
            new(center.X, center.Y, center.X, center.Y), "") };
        var icon = Button with { Label = "" };
        var match = BrowserTargetMatcher.Match(frame, step, [icon], true);
        Assert.Equal(icon, match.Control); Assert.Equal(bounds, match.Target!.Bounds);
        Assert.Null(BrowserTargetMatcher.Match(frame, step, [icon, icon with { Id = "overlap" }], true).Target);
        Assert.Null(BrowserTargetMatcher.Match(frame, step, [icon with { Enabled = false }], true).Target);
    }
    [Fact]
    public void PaddedCroppedImagesInvertTheExistingGeometryMapping()
    {
        var frame = Frame();
        frame = frame with { Geometry = frame.Geometry with { CropBounds = new(.5, 0, 1, .5), ContentBounds = new(.1, .2, .9, .8) } };
        var bounds = BrowserTargetMatcher.ImageBounds(frame, Button.Rectangle);
        Assert.NotNull(bounds);
        Assert.Equal((-410, 160), frame.Geometry.Map(bounds.Left, bounds.Top));
        Assert.Equal((-271, 199), frame.Geometry.Map(bounds.Right, bounds.Bottom));
        Assert.Null(BrowserTargetMatcher.ImageBounds(frame, new(-1800, 160, 140, 40)));
    }
}
