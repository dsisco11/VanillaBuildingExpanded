using System.Reflection;
using Moq;
using VanillaBuildingExpanded.UI;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Xunit;

namespace VanillaBuildingExpanded.Tests.UI;

[Collection("Table composition")]
public sealed class TableInputTests
{
    [Fact]
    public void PassiveCells_RemainTransparentEvenWhenNativelyFocusableOrClickable()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var child = fixture.Cell("passive", focusable: true);
        table.AddRow(child);
        fixture.Arrange(table);
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        table.OnMouseMove(fixture.Api.Object, fixture.Mouse());
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse());
        var wheel = new MouseWheelEventArgs();
        table.OnMouseWheel(fixture.Api.Object, wheel);
        table.OnFocusGained();
        table.OnKeyDown(fixture.Api.Object, Key(GlKeys.Enter));
        table.OnKeyUp(fixture.Api.Object, Key(GlKeys.Enter));
        table.OnKeyPress(fixture.Api.Object, Key(GlKeys.Enter));
        Assert.False(table.Focusable);
        Assert.False(table.HasFocus);
        Assert.False(wheel.IsHandled);
        Assert.Empty(fixture.Events);
        Assert.Null(table.MouseOverCursor);
    }

    [Theory]
    [InlineData((int)TableInput.None, true, false)]
    [InlineData((int)TableInput.Pointer, true, false)]
    [InlineData((int)TableInput.Keyboard, false, false)]
    [InlineData((int)TableInput.Keyboard, true, true)]
    [InlineData((int)(TableInput.Pointer | TableInput.Keyboard), true, true)]
    public void GroupFocusability_RequiresBothPermissionAndNativeCapability(int flags, bool nativeFocusable, bool expected)
    {
        using var fixture = new Fixture();
        using var composer = fixture.Composer();
        var table = fixture.Table();
        var child = fixture.Cell("child", focusable: nativeFocusable);
        table.AddRowCells([fixture.Describe(child, (TableInput)flags)]);
        Assert.Equal(expected, table.Focusable);
        composer.TryAddTable(table, "table");
        Assert.Equal(expected ? 0 : -1, table.TabIndex);
        Assert.Equal(expected, table.Focusable);
    }

    [Theory]
    [InlineData(11, 1)]
    [InlineData(1, 11)]
    [InlineData(15, 15)]
    [InlineData(31, 1)]
    public void GapsShortRowsAndOutsidePoints_DoNotConsumePointerInput(int x, int y)
    {
        using var fixture = new Fixture();
        using var table = fixture.Table(new TableOptions(columnGap: 10, rowGap: 10));
        var first = fixture.Cell("first");
        var second = fixture.Cell("second");
        var third = fixture.Cell("third");
        table.AddRowCells([fixture.Describe(first), fixture.Describe(second)]);
        table.AddRowCells([fixture.Describe(third)]);
        fixture.Arrange(table);
        var mouse = fixture.Mouse(x, y);
        table.OnMouseDown(fixture.Api.Object, mouse);
        Assert.False(mouse.Handled);
        Assert.Empty(fixture.Events);
        Assert.Null(table.MouseOverCursor);
    }

    [Fact]
    public void NonFocusableNativeButton_ActivatesOnlyOnNormalReleaseAndCanBeCancelledByHost()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        int clicks = 0;
        var button = new GuiElementCollapseButton(fixture.Api.Object, ElementBounds.Fixed(0, 0, 30, 20),
            collapsed: false, () => clicks++);
        table.AddRowCells([new TableCell(button, input: TableInput.Pointer, cancelInput: button.CancelPress)]);
        fixture.Arrange(table);
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        Assert.True(button.IsPressed);
        Assert.False(table.Focusable);
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse());
        Assert.Equal(1, clicks);
        Assert.False(button.IsPressed);
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        table.CancelInput();
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse());
        Assert.Equal(1, clicks);
        Assert.False(button.IsPressed);
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse(100, 100));
        Assert.Equal(1, clicks);
        Assert.False(button.IsPressed);
    }

    [Fact]
    public void PointerHitTesting_UsesReverseVisualOrderAndReversesTies()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var low = fixture.Cell("low", drawOrder: 0);
        var highFirst = fixture.Cell("high-first", drawOrder: 10);
        var highLast = fixture.Cell("high-last", drawOrder: 10);
        table.AddRowCells([fixture.Describe(low), fixture.Describe(highFirst), fixture.Describe(highLast)]);
        fixture.Arrange(table);
        highFirst.Bounds.absFixedX = highLast.Bounds.absFixedX = 0;
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        Assert.Equal(["down:high-last:Left"], fixture.Events);
        fixture.Events.Clear();
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse());
        Assert.Equal(["up:high-last:Left", "cancel:high-last"], fixture.Events);
    }

    [Fact]
    public void UnhandledPresses_AreStillCapturedAndMatchingReleaseCancelsLowerRecipientsAfterHandling()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var low = fixture.Cell("low");
        var high = fixture.Cell("high", drawOrder: 1);
        high.HandleDown = false;
        table.AddRowCells([fixture.Describe(low), fixture.Describe(high)]);
        fixture.Arrange(table);
        high.Bounds.absFixedX = 0;
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        Assert.True(high.Pressed);
        Assert.True(low.Pressed);
        fixture.Events.Clear();
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse(100, 100));
        Assert.Equal(["up:high:Left", "cancel:high", "cancel:low"], fixture.Events);
        Assert.False(high.Pressed);
        Assert.False(low.Pressed);
        fixture.Events.Clear();
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse());
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public void Captures_AreIndependentByButtonAndMoveIsDeliveredOncePerChild()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var first = fixture.Cell("first");
        var second = fixture.Cell("second");
        table.AddRowCells([fixture.Describe(first), fixture.Describe(second)]);
        fixture.Arrange(table);
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse(11, 1, EnumMouseButton.Right));
        fixture.Events.Clear();
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse(100, 100, EnumMouseButton.Middle));
        Assert.Empty(fixture.Events);
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse(100, 100));
        Assert.Equal(["up:first:Left", "cancel:first"], fixture.Events);
        Assert.True(second.Pressed);
        fixture.Events.Clear();
        table.OnMouseMove(fixture.Api.Object, fixture.Mouse(100, 100));
        Assert.Equal(["move:second"], fixture.Events);
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse(100, 100, EnumMouseButton.Right));
        Assert.False(second.Pressed);
    }

    [Fact]
    public void RepeatedDown_CancelsThePreviousPressBeforeStartingItsReplacement()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var child = fixture.Cell("child");
        table.AddRowCells([fixture.Describe(child)]);
        fixture.Arrange(table);
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        fixture.Events.Clear();
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        Assert.Equal(["cancel:child", "down:child:Left"], fixture.Events);
        fixture.Events.Clear();
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse());
        Assert.Equal(["up:child:Left", "cancel:child"], fixture.Events);
    }

    [Fact]
    public void CapturedMovement_PrecedesHoveredChildrenAndRespectsHandledFlags()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var captured = fixture.Cell("captured");
        var hovered = fixture.Cell("hovered", drawOrder: 10);
        captured.HandleMove = true;
        table.AddRowCells([fixture.Describe(captured), fixture.Describe(hovered)]);
        fixture.Arrange(table);
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        fixture.Events.Clear();
        var move = fixture.Mouse(11, 1);
        table.OnMouseMove(fixture.Api.Object, move);
        Assert.True(move.Handled);
        Assert.Equal(["move:captured"], fixture.Events);
    }

    [Fact]
    public void PointerHitTesting_UsesScaledWorldCoordinatesAtTheTableOrigin()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        RuntimeEnv.GUIScale = 1.5f;
        table.Bounds.fixedX = 20;
        table.Bounds.fixedY = 30;
        table.AddRowCells([fixture.Describe(fixture.Cell("child"))]);
        fixture.Arrange(table);
        var logicalPoint = fixture.Mouse(21, 31);
        table.OnMouseDown(fixture.Api.Object, logicalPoint);
        Assert.False(logicalPoint.Handled);
        var screenPoint = fixture.Mouse(31, 46);
        table.OnMouseDown(fixture.Api.Object, screenPoint);
        Assert.True(screenPoint.Handled);
        Assert.Equal(["down:child:Left"], fixture.Events);
    }

    [Fact]
    public void SameChildMultipleButtons_IsNotSentDuplicateMoveOrCancellation()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var child = fixture.Cell("child");
        table.AddRowCells([fixture.Describe(child)]);
        fixture.Arrange(table);
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse(button: EnumMouseButton.Right));
        fixture.Events.Clear();
        table.OnMouseMove(fixture.Api.Object, fixture.Mouse(100, 100));
        Assert.Equal(["move:child"], fixture.Events);
        fixture.Events.Clear();
        table.CancelInput();
        table.CancelInput();
        Assert.Equal(["cancel:child"], fixture.Events);
    }

    [Fact]
    public void HandledRelease_CancelsWithoutForwardingAnActivatingRelease()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var child = fixture.Cell("child");
        table.AddRowCells([fixture.Describe(child)]);
        fixture.Arrange(table);
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        fixture.Events.Clear();
        var release = fixture.Mouse();
        release.Handled = true;
        table.OnMouseUp(fixture.Api.Object, release);
        Assert.Equal(["cancel:child"], fixture.Events);
        Assert.False(child.Pressed);
    }

    [Fact]
    public void AlreadyHandledEvents_AreNotForwarded()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var child = fixture.Cell("child", focusable: true);
        table.AddRowCells([fixture.Describe(child, TableInput.Pointer | TableInput.Keyboard)]);
        fixture.Arrange(table);
        table.OnFocusGained();
        fixture.Events.Clear();
        var mouse = fixture.Mouse();
        mouse.Handled = true;
        table.OnMouseDown(fixture.Api.Object, mouse);
        table.OnMouseMove(fixture.Api.Object, mouse);
        var wheel = new MouseWheelEventArgs();
        wheel.SetHandled();
        table.OnMouseWheel(fixture.Api.Object, wheel);
        var key = Key(GlKeys.Tab);
        key.Handled = true;
        table.OnKeyDown(fixture.Api.Object, key);
        table.OnKeyUp(fixture.Api.Object, key);
        table.OnKeyPress(fixture.Api.Object, key);
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public void ClippingPreventsNewHitsButNotCapturedMovementAndRelease()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var child = fixture.Cell("child");
        table.AddRowCells([fixture.Describe(child)]);
        fixture.Arrange(table);
        var clip = ElementBounds.Fixed(0, 0, 5, 5).WithEmptyParent();
        clip.CalcWorldBounds();
        table.InsideClipBounds = clip;
        var clipped = fixture.Mouse(8, 1);
        table.OnMouseDown(fixture.Api.Object, clipped);
        Assert.False(clipped.Handled);
        Assert.Empty(fixture.Events);
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        fixture.Events.Clear();
        table.OnMouseMove(fixture.Api.Object, fixture.Mouse(8, 1));
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse(8, 1));
        Assert.Equal(["move:child", "up:child:Left", "cancel:child"], fixture.Events);
        Assert.Null(table.MouseOverCursor);
    }

    [Fact]
    public void CursorAndHoverExit_AreUpdatedWithoutPressCaptureAndClearedOverGaps()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table(new TableOptions(columnGap: 5));
        var first = fixture.Cell("first");
        var passive = fixture.Cell("passive");
        table.AddRowCells([fixture.Describe(first), new TableCell(passive)]);
        fixture.Arrange(table);
        table.OnMouseMove(fixture.Api.Object, fixture.Mouse());
        Assert.Equal("first-cursor", table.MouseOverCursor);
        fixture.Events.Clear();
        table.OnMouseMove(fixture.Api.Object, fixture.Mouse(12, 1));
        Assert.Equal(["move:first"], fixture.Events);
        Assert.Null(table.MouseOverCursor);
        fixture.Events.Clear();
        table.OnMouseMove(fixture.Api.Object, fixture.Mouse(16, 1));
        Assert.Empty(fixture.Events);
        Assert.Null(table.MouseOverCursor);
        table.OnMouseMove(fixture.Api.Object, fixture.Mouse());
        table.InsideClipBounds = ElementBounds.Fixed(0, 0, 1, 1);
        Assert.Null(table.MouseOverCursor);
    }

    [Fact]
    public void WheelRouting_UsesCurrentPointerPositionAndStopsWhenHandled()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var low = fixture.Cell("low");
        var high = fixture.Cell("high", drawOrder: 1);
        table.AddRowCells([fixture.Describe(low), fixture.Describe(high)]);
        fixture.Arrange(table);
        high.Bounds.absFixedX = 0;
        var wheel = new MouseWheelEventArgs();
        table.OnMouseWheel(fixture.Api.Object, wheel);
        Assert.True(wheel.IsHandled);
        Assert.Equal(["wheel:high"], fixture.Events);
        fixture.Events.Clear();
        fixture.MouseX = 100;
        table.OnMouseWheel(fixture.Api.Object, new MouseWheelEventArgs());
        Assert.Empty(fixture.Events);
        Assert.Null(table.MouseOverCursor);
    }

    [Fact]
    public void KeyboardTraversal_IsRowMajorInsteadOfVisualAndLeavesBoundariesUnhandled()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var first = fixture.Cell("first", focusable: true, drawOrder: 10);
        var second = fixture.Cell("second", focusable: true, drawOrder: -10);
        var passive = fixture.Cell("passive", focusable: true);
        var notFocusable = fixture.Cell("not-focusable");
        var last = fixture.Cell("last", focusable: true);
        table.AddRowCells([fixture.Describe(first, TableInput.Keyboard), new TableCell(passive)]);
        table.AddRowCells([fixture.Describe(second, TableInput.Keyboard),
            fixture.Describe(notFocusable, TableInput.Keyboard), fixture.Describe(last, TableInput.Keyboard)]);
        table.OnFocusGained();
        Assert.True(first.HasFocus);
        var tab = Key(GlKeys.Tab);
        table.OnKeyDown(fixture.Api.Object, tab);
        Assert.True(tab.Handled);
        Assert.True(second.HasFocus);
        Assert.False(first.HasFocus);
        table.OnKeyDown(fixture.Api.Object, Key(GlKeys.Tab));
        Assert.True(last.HasFocus);
        var exit = Key(GlKeys.Tab);
        table.OnKeyDown(fixture.Api.Object, exit);
        Assert.False(exit.Handled);
        Assert.True(last.HasFocus);
        var reverse = Key(GlKeys.Tab, shift: true);
        table.OnKeyDown(fixture.Api.Object, reverse);
        Assert.True(reverse.Handled);
        Assert.True(second.HasFocus);
        Assert.False(passive.HasFocus || notFocusable.HasFocus);
    }

    [Fact]
    public void FocusedChild_GetsFirstChanceToHandleTabAndIsTheOnlyKeyboardRecipient()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var first = fixture.Cell("first", focusable: true);
        var second = fixture.Cell("second", focusable: true);
        first.HandleKeys = true;
        table.AddRowCells([fixture.Describe(first, TableInput.Keyboard), fixture.Describe(second, TableInput.Keyboard)]);
        table.OnFocusGained();
        fixture.Events.Clear();
        var tab = Key(GlKeys.Tab);
        table.OnKeyDown(fixture.Api.Object, tab);
        table.OnKeyUp(fixture.Api.Object, Key(GlKeys.Enter));
        table.OnKeyPress(fixture.Api.Object, Key(GlKeys.Enter));
        Assert.True(tab.Handled);
        Assert.True(first.HasFocus);
        Assert.False(second.HasFocus);
        Assert.Equal(["key-down:first", "key-up:first", "key-press:first"], fixture.Events);
        table.OnFocusLost();
        fixture.Events.Clear();
        table.OnKeyDown(fixture.Api.Object, Key(GlKeys.Enter));
        table.OnKeyUp(fixture.Api.Object, Key(GlKeys.Enter));
        table.OnKeyPress(fixture.Api.Object, Key(GlKeys.Enter));
        Assert.Empty(fixture.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeComposer_TabEntersFromEitherDirectionAndExitsTheNestedGroup(bool reverse)
    {
        using var fixture = new Fixture();
        using var composer = fixture.Composer();
        var outsideBefore = fixture.Cell("before", focusable: true);
        var outsideAfter = fixture.Cell("after", focusable: true);
        composer.AddInteractiveElement(outsideBefore, "before");
        var table = fixture.Table();
        var first = fixture.Cell("first", focusable: true);
        var last = fixture.Cell("last", focusable: true);
        table.AddRowCells([fixture.Describe(first, TableInput.Keyboard), fixture.Describe(last, TableInput.Keyboard)]);
        composer.TryAddTable(table, "table");
        composer.AddInteractiveElement(outsideAfter, "after");
        composer.FocusElement(reverse ? outsideAfter.TabIndex : outsideBefore.TabIndex);
        var entry = Key(GlKeys.Tab, reverse);
        composer.OnKeyDown(entry, haveFocus: true);
        Assert.True(entry.Handled);
        Assert.True(table.HasFocus);
        Assert.True((reverse ? last : first).HasFocus);
        Assert.False((reverse ? first : last).HasFocus);
        var internalTab = Key(GlKeys.Tab, reverse);
        composer.OnKeyDown(internalTab, haveFocus: true);
        Assert.True(internalTab.Handled);
        Assert.True((reverse ? first : last).HasFocus);
        var exit = Key(GlKeys.Tab, reverse);
        composer.OnKeyDown(exit, haveFocus: true);
        Assert.True(exit.Handled);
        Assert.False(table.HasFocus);
        Assert.False(first.HasFocus || last.HasFocus);
        Assert.True((reverse ? outsideBefore : outsideAfter).HasFocus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeComposer_SingleGroupPreservesFocusAtItsTraversalBoundary(bool reverse)
    {
        using var fixture = new Fixture();
        using var composer = fixture.Composer();
        var table = fixture.Table();
        var child = fixture.Cell("child", focusable: true);
        table.AddRowCells([fixture.Describe(child, TableInput.Keyboard)]);
        composer.TryAddTable(table, "table");
        composer.FocusElement(table.TabIndex);
        var tab = Key(GlKeys.Tab, reverse);
        composer.OnKeyDown(tab, haveFocus: true);
        Assert.False(tab.Handled);
        Assert.True(table.HasFocus);
        Assert.True(child.HasFocus);
        Assert.Equal(0, child.FocusLostCalls);
    }

    [Fact]
    public void NativeComposer_PointerSelectionIsPreservedWhenItFocusesTheGroup()
    {
        using var fixture = new Fixture();
        using var composer = fixture.Composer();
        var table = fixture.Table();
        var first = fixture.Cell("first", focusable: true);
        var last = fixture.Cell("last", focusable: true);
        table.AddRowCells([fixture.Describe(first, TableInput.Pointer | TableInput.Keyboard),
            fixture.Describe(last, TableInput.Pointer | TableInput.Keyboard)]);
        composer.TryAddTable(table, "table");
        composer.Bounds.CalcWorldBounds();
        composer.OnMouseDown(fixture.Mouse(11, 1));
        Assert.True(table.HasFocus);
        Assert.True(last.HasFocus);
        Assert.False(first.HasFocus);
        Assert.Equal(1, last.FocusGainedCalls);
        table.OnFocusGained();
        Assert.Equal(1, last.FocusGainedCalls);
        composer.OnMouseUp(fixture.Mouse(11, 1));
        Assert.False(last.Pressed);
    }

    [Fact]
    public void NativeComposer_ClickingAnotherControlCancelsTableFocusAndCapture()
    {
        using var fixture = new Fixture();
        using var composer = fixture.Composer();
        var outside = new GuiElementCollapseButton(fixture.Api.Object, ElementBounds.Fixed(50, 0, 20, 20),
            collapsed: false, () => { });
        composer.AddInteractiveElement(outside, "outside");
        var table = fixture.Table();
        var child = fixture.Cell("child", focusable: true);
        table.AddRowCells([fixture.Describe(child, TableInput.Pointer | TableInput.Keyboard)]);
        composer.TryAddTable(table, "table");
        composer.Bounds.CalcWorldBounds();
        composer.OnMouseDown(fixture.Mouse());
        Assert.True(table.HasFocus && child.HasFocus && child.Pressed);
        fixture.Events.Clear();
        composer.OnMouseDown(fixture.Mouse(51, 1));
        Assert.False(table.HasFocus || child.HasFocus || child.Pressed);
        Assert.Equal(["cancel:child", "focus-lost:child"], fixture.Events);
        Assert.True(outside.IsPressed);
    }

    [Fact]
    public void ClickingPointerOnlyChild_DoesNotSelectAnUnrelatedKeyboardChild()
    {
        using var fixture = new Fixture();
        using var composer = fixture.Composer();
        var table = fixture.Table();
        var pointer = fixture.Cell("pointer");
        var keyboard = fixture.Cell("keyboard", focusable: true);
        table.AddRowCells([fixture.Describe(pointer), fixture.Describe(keyboard, TableInput.Keyboard)]);
        composer.TryAddTable(table, "table");
        composer.Bounds.CalcWorldBounds();
        composer.OnMouseDown(fixture.Mouse());
        Assert.True(table.HasFocus);
        Assert.False(keyboard.HasFocus);
        composer.OnKeyDown(Key(GlKeys.Enter), haveFocus: true);
        Assert.DoesNotContain("key-down:keyboard", fixture.Events);
        var tab = Key(GlKeys.Tab);
        composer.OnKeyDown(tab, haveFocus: true);
        Assert.True(tab.Handled);
        Assert.True(keyboard.HasFocus);
    }

    [Fact]
    public void InternalFocusChange_CancelsTheOutgoingChildCaptureWithoutMouseUp()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var first = fixture.Cell("first", focusable: true);
        var second = fixture.Cell("second", focusable: true);
        table.AddRowCells([fixture.Describe(first, TableInput.Pointer | TableInput.Keyboard),
            fixture.Describe(second, TableInput.Keyboard)]);
        fixture.Arrange(table);
        table.OnFocusGained();
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        table.OnKeyDown(fixture.Api.Object, Key(GlKeys.Tab));
        Assert.False(first.Pressed);
        Assert.True(second.HasFocus);
        Assert.DoesNotContain("up:first:Left", fixture.Events);
        fixture.Events.Clear();
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse());
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public void FocusLossAndHostCancellation_ClearFocusCaptureAndCursorWithoutActivation()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var child = fixture.Cell("child", focusable: true);
        table.AddRowCells([fixture.Describe(child, TableInput.Pointer | TableInput.Keyboard)]);
        fixture.Arrange(table);
        table.OnFocusGained();
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        fixture.Events.Clear();
        table.OnFocusLost();
        Assert.Equal(["cancel:child", "focus-lost:child"], fixture.Events);
        Assert.False(table.HasFocus || child.HasFocus || child.Pressed);
        Assert.Null(table.MouseOverCursor);
        fixture.Events.Clear();
        table.CancelInput();
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse());
        table.OnKeyDown(fixture.Api.Object, Key(GlKeys.Enter));
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public void CancellationFailure_StillClearsOtherCapturesAndFocusAndSurfacesAllErrors()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var first = fixture.Cell("first", focusable: true);
        var second = fixture.Cell("second");
        second.HandleDown = false;
        first.ThrowOnCancel = second.ThrowOnCancel = true;
        table.AddRowCells([fixture.Describe(first, TableInput.Pointer | TableInput.Keyboard),
            fixture.Describe(second)]);
        fixture.Arrange(table);
        table.OnFocusGained();
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse(11, 1, EnumMouseButton.Right));
        var error = Assert.Throws<AggregateException>(table.CancelInput);
        Assert.Equal(2, error.Flatten().InnerExceptions.Count);
        Assert.False(first.Pressed || second.Pressed || first.HasFocus || table.HasFocus);
        fixture.Events.Clear();
        table.CancelInput();
        Assert.Empty(fixture.Events);
        first.ThrowOnCancel = second.ThrowOnCancel = false;
    }

    [Fact]
    public void DownFailure_CancelsPrivatePressStateAndRethrowsTheOriginalError()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var child = fixture.Cell("child");
        child.Down = () => throw new InvalidOperationException("press failed");
        table.AddRowCells([fixture.Describe(child)]);
        fixture.Arrange(table);
        Assert.Throws<InvalidOperationException>(() => table.OnMouseDown(fixture.Api.Object, fixture.Mouse()));
        Assert.False(child.Pressed);
        Assert.Contains("cancel:child", fixture.Events);
        fixture.Events.Clear();
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse());
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public void ReleaseFailure_CancelsAllRecipientsWithoutActivatingRemainingCaptures()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var low = fixture.Cell("low");
        var high = fixture.Cell("high", drawOrder: 1);
        high.HandleDown = false;
        high.Up = () => throw new InvalidOperationException("release failed");
        table.AddRowCells([fixture.Describe(low), fixture.Describe(high)]);
        fixture.Arrange(table);
        high.Bounds.absFixedX = 0;
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        fixture.Events.Clear();
        Assert.Throws<AggregateException>(() => table.OnMouseUp(fixture.Api.Object, fixture.Mouse()));
        Assert.Equal(["up:high:Left", "cancel:high", "cancel:low"], fixture.Events);
        Assert.False(low.Pressed || high.Pressed);
    }

    [Fact]
    public void CancellationDuringPress_DoesNotReestablishPendingFocusOrCapture()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var child = fixture.Cell("child", focusable: true);
        child.Down = table.CancelInput;
        table.AddRowCells([fixture.Describe(child, TableInput.Pointer | TableInput.Keyboard)]);
        fixture.Arrange(table);
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        Assert.False(child.Pressed);
        Assert.Null(table.MouseOverCursor);
        fixture.Events.Clear();
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse());
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public void CancellationDuringRelease_CancelsRemainingRecipientsWithoutActivatingThem()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var low = fixture.Cell("low");
        var high = fixture.Cell("high", drawOrder: 1);
        high.HandleDown = false;
        high.Up = table.CancelInput;
        table.AddRowCells([fixture.Describe(low), fixture.Describe(high)]);
        fixture.Arrange(table);
        high.Bounds.absFixedX = 0;
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        fixture.Events.Clear();
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse());
        Assert.Equal(["up:high:Left", "cancel:high", "cancel:low"], fixture.Events);
        Assert.Null(table.MouseOverCursor);
        Assert.False(high.Pressed || low.Pressed);
    }

    [Fact]
    public void CleanupCallbacks_CannotReenterPointerRoutingAndCreateFreshCapture()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var child = fixture.Cell("child");
        table.AddRowCells([new TableCell(child, input: TableInput.Pointer, cancelInput: () =>
        {
            child.Cancel();
            table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
            table.CancelInput();
        })]);
        fixture.Arrange(table);
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        fixture.Events.Clear();
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse());
        Assert.Equal(["up:child:Left", "cancel:child"], fixture.Events);
        fixture.Events.Clear();
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse());
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public void DisposingDuringRelease_DoesNotInvokeDisposedChildrenAgain()
    {
        using var fixture = new Fixture();
        using var table = fixture.Table();
        var child = fixture.Cell("child");
        child.Up = table.Dispose;
        table.AddRowCells([fixture.Describe(child)]);
        fixture.Arrange(table);
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        fixture.Events.Clear();
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse());
        Assert.Equal(["up:child:Left", "cancel:child", "dispose:child"], fixture.Events);
        Assert.Equal(1, child.DisposeCalls);
        fixture.Events.Clear();
        table.OnMouseDown(fixture.Api.Object, fixture.Mouse());
        table.OnMouseMove(fixture.Api.Object, fixture.Mouse());
        table.OnMouseUp(fixture.Api.Object, fixture.Mouse());
        table.OnMouseWheel(fixture.Api.Object, new MouseWheelEventArgs());
        table.OnKeyDown(fixture.Api.Object, Key(GlKeys.Tab));
        table.OnKeyUp(fixture.Api.Object, Key(GlKeys.Tab));
        table.OnKeyPress(fixture.Api.Object, Key(GlKeys.Enter));
        table.OnFocusGained();
        table.OnFocusLost();
        table.CancelInput();
        Assert.Empty(fixture.Events);
        Assert.False(table.HasFocus || table.Focusable);
    }

    private static KeyEvent Key(GlKeys key, bool shift = false) => new()
    {
        KeyCode = (int)key,
        ShiftPressed = shift
    };

    private sealed class Fixture : IDisposable
    {
        private readonly float previousScale = RuntimeEnv.GUIScale;
        public Mock<ICoreClientAPI> Api { get; } = new();
        public Mock<IInputAPI> Input { get; } = new();
        public List<string> Events { get; } = [];
        public int MouseX { get; set; } = 1;
        public int MouseY { get; set; } = 1;

        public Fixture()
        {
            RuntimeEnv.GUIScale = 1;
            Api.SetupGet(api => api.Input).Returns(Input.Object);
            Input.SetupGet(input => input.MouseX).Returns(() => MouseX);
            Input.SetupGet(input => input.MouseY).Returns(() => MouseY);
        }

        public GuiElementTable Table(TableOptions? options = null) =>
            new(Api.Object, ElementBounds.Fixed(0, 0, 0, 0), options);

        public Probe Cell(string name, bool focusable = false, double drawOrder = 0) =>
            new(Api.Object, name, Events, focusable, drawOrder);

        public TableCell Describe(Probe child, TableInput flags = TableInput.Pointer) =>
            new(child, input: flags, cancelInput: (flags & TableInput.Pointer) != 0 ? child.Cancel : null);

        public MouseEvent Mouse(int x = 1, int y = 1, EnumMouseButton button = EnumMouseButton.Left) =>
            new(x, y, button, 0);

        public void Arrange(GuiElementTable table)
        {
            var parent = ElementBounds.Fixed(0, 0, 200, 200).WithEmptyParent();
            parent.WithChild(table.Bounds);
            table.Arrange();
            parent.CalcWorldBounds();
        }

        public GuiComposer Composer() =>
            Assert.IsType<GuiComposer>(Activator.CreateInstance(typeof(GuiComposer),
                BindingFlags.Instance | BindingFlags.NonPublic, binder: null,
                [Api.Object, ElementBounds.Fixed(0, 0, 200, 200).WithEmptyParent(), "table-input-tests"],
                culture: null));

        public void Dispose() => RuntimeEnv.GUIScale = previousScale;
    }

    private sealed class Probe(ICoreClientAPI api, string name, List<string> events,
        bool focusable, double drawOrder) : GuiElement(api, ElementBounds.Fixed(0, 0, 10, 10))
    {
        public override bool Focusable => focusable;
        public override double DrawOrder => drawOrder;
        public bool Pressed { get; private set; }
        public bool HandleDown { get; set; } = true;
        public bool HandleMove { get; set; }
        public bool HandleKeys { get; set; }
        public bool ThrowOnCancel { get; set; }
        public Action? Down { get; set; }
        public Action? Up { get; set; }
        public int FocusGainedCalls { get; private set; }
        public int FocusLostCalls { get; private set; }
        public int DisposeCalls { get; private set; }

        public override void OnMouseDown(ICoreClientAPI api, MouseEvent args)
        {
            events.Add($"down:{name}:{args.Button}");
            Pressed = true;
            args.Handled = HandleDown;
            Down?.Invoke();
        }

        public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
        {
            events.Add($"up:{name}:{args.Button}");
            args.Handled = true;
            Up?.Invoke();
        }

        public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
        {
            events.Add($"move:{name}");
            MouseOverCursor = $"{name}-cursor";
            args.Handled = HandleMove;
        }

        public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
        {
            events.Add($"wheel:{name}");
            args.SetHandled();
        }

        public override void OnFocusGained()
        {
            FocusGainedCalls++;
            events.Add($"focus-gained:{name}");
            base.OnFocusGained();
        }

        public override void OnFocusLost()
        {
            FocusLostCalls++;
            events.Add($"focus-lost:{name}");
            base.OnFocusLost();
        }

        public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
        {
            events.Add($"key-down:{name}");
            args.Handled = HandleKeys;
        }

        public override void OnKeyUp(ICoreClientAPI api, KeyEvent args) => events.Add($"key-up:{name}");
        public override void OnKeyPress(ICoreClientAPI api, KeyEvent args) => events.Add($"key-press:{name}");

        public void Cancel()
        {
            events.Add($"cancel:{name}");
            Pressed = false;
            if (ThrowOnCancel) throw new InvalidOperationException("Cancellation failed.");
        }

        public override void Dispose()
        {
            DisposeCalls++;
            events.Add($"dispose:{name}");
        }
    }
}
