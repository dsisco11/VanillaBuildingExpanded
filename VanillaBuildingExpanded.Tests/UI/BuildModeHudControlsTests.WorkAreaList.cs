using VanillaBuildingExpanded.BuildBrush;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Xunit;

namespace VanillaBuildingExpanded.Tests.UI;

public sealed partial class BuildModeHudControlsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.33)]
    [InlineData(2)]
    public void ListScrollbarReachesBothEndsWithoutSelectingOrLeakingItsCapturedGesture(float scale)
    {
        using var f = WorkAreaFixture(scale);
        var area = ActiveWorkArea();
        var original = area.SelectedId;
        for (int i = 1; i <= 12; i++) area.CreateArea("Area " + i);
        area.SelectArea(original!.Value);
        using var hud = new BuildModeHud(f.Api.Object, () => default, area);
        hud.SetVisible(true);
        var list = Assert.IsType<WorkAreaListBox>(WorkAreaTable(hud).GetElement("select"));
        int x = (int)(list.Bounds.renderX + list.Bounds.OuterWidth - 5 * scale);
        int top = (int)list.Bounds.renderY;
        int bottom = (int)(list.Bounds.renderY + list.Bounds.OuterHeight);
        var press = new MouseEvent(x, top + 2, EnumMouseButton.Left, 0);
        hud.OnMouseDown(press);
        Assert.True(press.Handled);
        Assert.True(hud.ReservesPointer(-100, -100));
        hud.OnMouseMove(new MouseEvent(x, bottom + 100));
        Assert.Equal(area.Areas.Count - 3, list.FirstVisible);
        hud.OnMouseMove(new MouseEvent(x, top - 100));
        Assert.Equal(0, list.FirstVisible);
        Assert.Equal(original, area.SelectedId);
        var release = new MouseEvent(-100, -100, EnumMouseButton.Left, 0);
        hud.OnMouseUp(release);
        Assert.True(release.Handled);
        Assert.False(hud.ReservesPointer(-100, -100));
        hud.OnMouseMove(new MouseEvent(x, bottom + 100));
        Assert.Equal(0, list.FirstVisible);
    }

    [Theory]
    [InlineData("move")]
    [InlineData("wheel")]
    [InlineData("handled")]
    public void InterruptedListRowGesturesNeverSelectAnotherArea(string interruption)
    {
        using var f = WorkAreaFixture();
        var area = ActiveWorkArea();
        var original = area.SelectedId;
        for (int i = 1; i <= 6; i++) area.CreateArea("Area " + i);
        area.SelectArea(original!.Value);
        using var hud = new BuildModeHud(f.Api.Object, () => default, area);
        hud.SetVisible(true);
        var list = Assert.IsType<WorkAreaListBox>(WorkAreaTable(hud).GetElement("select"));
        var press = WorkAreaListMouse(f, list, 1);
        hud.OnMouseDown(press);
        var release = new MouseEvent(press.X, press.Y, EnumMouseButton.Left, 0);
        switch (interruption)
        {
            case "move":
                var otherRow = WorkAreaListMouse(f, list, 0);
                release = new MouseEvent(otherRow.X, otherRow.Y, EnumMouseButton.Left, 0);
                break;
            case "wheel":
                hud.OnMouseWheel(new MouseWheelEventArgs { delta = -1, deltaPrecise = -1 });
                Assert.Equal(1, list.FirstVisible);
                break;
            case "handled": release.Handled = true; break;
        }
        hud.OnMouseUp(release);
        Assert.True(release.Handled);
        Assert.Equal(original, area.SelectedId);
        Assert.False(WorkAreaTable(hud).HasPointerCapture);
        Assert.False(hud.ReservesPointer(-100, -100));
    }

    [Fact]
    public void ListScrollSurvivesRecompositionAndSelectionChangesRevealTheSelectedRow()
    {
        using var f = WorkAreaFixture();
        var area = ActiveWorkArea();
        var original = area.SelectedId!.Value;
        for (int i = 1; i <= 10; i++) area.CreateArea("Area " + i);
        area.SelectArea(original);
        using var hud = new BuildModeHud(f.Api.Object, () => default, area);
        hud.SetVisible(true);
        var list = Assert.IsType<WorkAreaListBox>(WorkAreaTable(hud).GetElement("select"));
        WorkAreaListMouse(f, list, 0);
        for (int i = 0; i < 4; i++)
            hud.OnMouseWheel(new MouseWheelEventArgs { delta = -1, deltaPrecise = -1 });
        Assert.Equal(4, list.FirstVisible);
        area.RenameArea(original, "Renamed");
        hud.OnRenderGUI(0);
        list = Assert.IsType<WorkAreaListBox>(WorkAreaTable(hud).GetElement("select"));
        Assert.Equal(4, list.FirstVisible);
        Assert.Equal("Renamed", list.Areas[0].Name);
        f.ScaleWatcher!(RuntimeEnv.GUIScale);
        list = Assert.IsType<WorkAreaListBox>(WorkAreaTable(hud).GetElement("select"));
        Assert.Equal(4, list.FirstVisible);
        area.SelectArea(area.Areas[^1].Id);
        hud.OnRenderGUI(0);
        list = Assert.IsType<WorkAreaListBox>(WorkAreaTable(hud).GetElement("select"));
        Assert.Equal(8, list.FirstVisible);
        Assert.Equal(area.SelectedId, list.SelectedId);
        foreach (var entry in area.Areas.ToArray()) area.DeleteArea(entry.Id);
        hud.OnRenderGUI(0);
        list = Assert.IsType<WorkAreaListBox>(WorkAreaTable(hud).GetElement("select"));
        Assert.Empty(list.Areas);
        Assert.False(list.Enabled);
        Assert.Equal(1, list.VisibleRows);
        Assert.Equal(0, list.FirstVisible);
        var id = area.CreateArea("Fresh");
        hud.OnRenderGUI(0);
        list = Assert.IsType<WorkAreaListBox>(WorkAreaTable(hud).GetElement("select"));
        Assert.True(list.Enabled);
        Assert.Equal(id, list.SelectedId);
        Assert.Single(list.Areas);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void ListDoesNotUploadUnchangedFramesAndDisposesViewportAndScrollbarTextures(int count)
    {
        using var f = WorkAreaFixture();
        var area = ActiveWorkArea();
        if (count == 0) area.DeleteArea(area.SelectedId!.Value);
        for (int i = 1; i < count; i++) area.CreateArea("Area " + i);
        using var hud = new BuildModeHud(f.Api.Object, () => default, area);
        hud.SetVisible(true);
        f.MouseX = f.MouseY = -100;
        hud.OnRenderGUI(0);
        int uploads = f.Uploads.Count;
        for (int i = 0; i < 3; i++) hud.OnRenderGUI(0);
        Assert.Equal(uploads, f.Uploads.Count);
        hud.Dispose();
        Assert.All(f.Uploads, upload => Assert.True(upload.Texture.Disposed));
    }

    [Fact]
    public void ClickingAListRowCommitsTheOutgoingDimensionDraftAndProjectsTheNewArea()
    {
        using var f = WorkAreaFixture();
        var area = EditableWorkArea(f);
        var first = area.SelectedId!.Value;
        var other = area.CreateArea("Other");
        area.RestoreEndpoints(new(30, 60, 10, 0), new(40, 62, 12, 0));
        area.SelectArea(first);
        using var hud = new BuildModeHud(f.Api.Object, () => default, area);
        hud.SetVisible(true);
        FocusDimension(hud, f, "x").SetValue("9");
        var list = Assert.IsType<WorkAreaListBox>(WorkAreaTable(hud).GetElement("select"));
        var press = WorkAreaListMouse(f, list, 1);
        hud.OnMouseDown(press);
        Assert.Equal(9, area.State.Bounds!.Value.SizeX);
        hud.OnMouseUp(new MouseEvent(press.X, press.Y, EnumMouseButton.Left, 0));
        hud.OnRenderGUI(0);
        Assert.Equal(other, area.SelectedId);
        Assert.Equal("Size: 11, 3, 3", WorkAreaSize(hud));
        Assert.Equal(9, area.Areas[0].Bounds!.Value.SizeX);
        Assert.False(hud.ShouldReceiveKeyboardEvents());
    }
}
