using System.Reflection;
using System.Runtime.CompilerServices;

using Moq;
using Newtonsoft.Json.Linq;

using VanillaBuildingExpanded.BuildBrush;
using VanillaBuildingExpanded.Gizmos;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

[CollectionDefinition("Work area config files", DisableParallelization = true)]
public sealed class WorkAreaConfigFileCollection;

[Collection("Work area config files")]
public sealed class WorkAreaPersistenceTests
{
    [Fact]
    public void NamedAreas_RoundTripNamesIdsAndBoundsButNotSelectionOrVisibility()
    {
        using var fixture = new Fixture();
        using var persistence = fixture.Open();
        var first = fixture.Controller.SelectedId!.Value;
        fixture.Controller.RenameArea(first, "Workshop");
        Commit(fixture.Controller, true, new BlockPos(-10, 65, 20, 0));
        Commit(fixture.Controller, false, new BlockPos(10, 75, 30, 0));
        var second = fixture.Controller.CreateArea("Garden");
        Commit(fixture.Controller, true, new BlockPos(30, 65, 40, 2));
        var expected = fixture.Controller.CommittedAreas.Select(area => area with { IsVisible = false }).ToArray();
        int writes = fixture.Writes.Count;
        fixture.Controller.SelectArea(first);
        fixture.Controller.SetVisibility(first, true);
        fixture.Controller.SetVisibility(second, false);
        Assert.Equal(writes, fixture.Writes.Count);
        var restored = new WorkAreaController();
        using var reopened = fixture.Open(restored);
        Assert.Equal(expected, restored.Areas);
        Assert.Equal(first, restored.SelectedId);
        restored.SelectArea(second);
        Assert.Equal(new WorkAreaEndpoint(30, 65, 40, 2), restored.State.Start);
        Assert.Null(restored.State.End);
        Assert.False(restored.State.ShowWorkArea);
        var record = fixture.Read()["worlds"]!["world-a"]!["areas"]![0]!;
        Assert.Equal(new[] { "id", "name", "start", "end" }, ((JObject)record).Properties().Select(p => p.Name));
    }

    [Fact]
    public void MetadataSavesDuringPreviewUseCommittedBoundsAndDeletionOfLastAreaRoundTripsEmpty()
    {
        using var fixture = new Fixture();
        using var persistence = fixture.Open();
        Commit(fixture.Controller, true, new BlockPos(10, 65, 20, 0));
        Commit(fixture.Controller, false, new BlockPos(20, 70, 30, 0));
        Assert.True(fixture.Controller.BeginTranslation());
        Assert.True(fixture.Controller.PreviewTranslation(100, 0, 0, fixture.Accessor.Object));
        fixture.Controller.RenameArea(fixture.Controller.SelectedId!.Value, "Renamed during drag");
        Assert.True(fixture.Controller.IsEditing);
        Assert.Equal(10, fixture.Read()["worlds"]!["world-a"]!["areas"]![0]!["start"]!["x"]!.Value<int>());
        fixture.Controller.DeleteArea(fixture.Controller.SelectedId!.Value);
        Assert.Empty((JArray)fixture.Read()["worlds"]!["world-a"]!["areas"]!);
        var restored = new WorkAreaController();
        using var reopened = fixture.Open(restored);
        Assert.Empty(restored.Areas);
        Assert.Null(restored.SelectedId);
        Assert.False(restored.State.HasActiveArea);
        restored.CreateArea("First again");
        Assert.Single((JArray)fixture.Read()["worlds"]!["world-a"]!["areas"]!);
    }

    [Fact]
    public void SingleAreaSchemaIsNotMigratedAndIsReplacedOnlyOnAnExplicitNamedAreaCommit()
    {
        using var fixture = new Fixture();
        var old = new JObject { ["schemaVersion"] = 1, ["worlds"] = new JObject
        {
            ["world-a"] = new JObject { ["start"] = Endpoint(100, 65, 200), ["end"] = Endpoint(110, 75, 210) }
        } };
        string original = fixture.Write(old);
        using var persistence = fixture.Open();
        Assert.Null(fixture.Controller.State.Start);
        Assert.Null(fixture.Controller.State.End);
        Assert.Single(fixture.Controller.Areas);
        Assert.Equal(original, File.ReadAllText(fixture.ConfigPath));
        Assert.Empty(fixture.Errors);
        Assert.Empty(fixture.Writes);
        fixture.Controller.RenameArea(fixture.Controller.SelectedId!.Value, "Fresh");
        Assert.Equal(2, fixture.Read()["schemaVersion"]!.Value<int>());
        Assert.Equal("Fresh", fixture.Read()["worlds"]!["world-a"]!["areas"]![0]!["name"]!.Value<string>());
        Assert.Equal(JTokenType.Null, fixture.Read()["worlds"]!["world-a"]!["areas"]![0]!["start"]!.Type);
    }

    [Theory]
    [InlineData("id", "not-an-id")]
    [InlineData("id", "00000000000000000000000000000000")]
    [InlineData("name", " ")]
    [InlineData("name", " bad padding ")]
    [InlineData("name", "bad\nname")]
    public void InvalidNamedAreaMetadataIsReportedWithoutPartiallyLoadingTheWorld(string property, string value)
    {
        using var fixture = new Fixture();
        var entry = Pair(Endpoint(10, 65, 20), null);
        entry["areas"]![0]![property] = value;
        string original = fixture.Write(Config(("world-a", entry)));
        using var persistence = fixture.Open();
        Assert.Null(fixture.Controller.State.Start);
        Assert.Contains("vanillabuildingexpanded:workarea-persistence-invalid-entry", fixture.Errors);
        Assert.Equal(original, File.ReadAllText(fixture.ConfigPath));
        Assert.Empty(fixture.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DuplicateIdsOrCaseInsensitiveNamesRejectTheWholeSavedCollection(bool duplicateId)
    {
        using var fixture = new Fixture();
        var entry = Pair(Endpoint(10, 65, 20), null);
        var duplicate = (JObject)entry["areas"]![0]!.DeepClone();
        duplicate[duplicateId ? "name" : "id"] = duplicateId ? "Other" : Guid.NewGuid().ToString("N");
        if (!duplicateId) duplicate["name"] = "FIRST";
        ((JArray)entry["areas"]!).Add(duplicate);
        fixture.Write(Config(("world-a", entry)));
        using var persistence = fixture.Open();
        Assert.Null(fixture.Controller.State.Start);
        Assert.Single(fixture.Controller.Areas);
        Assert.Contains("vanillabuildingexpanded:workarea-persistence-invalid-entry", fixture.Errors);
    }

    [Fact]
    public void FailedNameSaveRetainsTheCollectionAndRetriesWithSubsequentMetadataChanges()
    {
        using var fixture = new Fixture();
        using var persistence = fixture.Open();
        fixture.FailWrites = true;
        fixture.Controller.RenameArea(fixture.Controller.SelectedId!.Value, "Workshop");
        Assert.True(persistence.IsDirty);
        Assert.Equal("Workshop", fixture.Controller.Areas[0].Name);
        Assert.Contains("vanillabuildingexpanded:workarea-persistence-save-failed", fixture.Errors);
        fixture.FailWrites = false;
        var second = fixture.Controller.CreateArea("Garden");
        Assert.False(persistence.IsDirty);
        var restored = new WorkAreaController();
        using var reopened = fixture.Open(restored);
        Assert.Equal(new[] { "Workshop", "Garden" }, restored.Areas.Select(area => area.Name));
        restored.DeleteArea(second);
        Assert.Single((JArray)fixture.Read()["worlds"]!["world-a"]!["areas"]!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviousConfigIsIgnoredAndUnchangedWhileWorkAreaStartsFreshAndPersistsIndependently(bool malformed)
    {
        using var fixture = new Fixture();
        const string previousName = "vanillabuildingexpanded-rulers.json";
        string previousPath = Path.Combine(GamePaths.ModConfig, previousName);
        string original = malformed ? "not-json" : Config(("world-a",
            Pair(Endpoint(100, 65, 200), Endpoint(110, 75, 210)))).ToString();
        File.WriteAllText(previousPath, original);
        var previousWriteTime = File.GetLastWriteTimeUtc(previousPath);
        using var persistence = fixture.Open();
        Assert.Equal("vanillabuildingexpanded-workareas.json", WorkAreaPersistence.ConfigFileName);
        Assert.Null(fixture.Controller.State.Start);
        Assert.Null(fixture.Controller.State.End);
        Assert.Empty(fixture.Errors);
        Assert.Empty(fixture.Writes);
        Assert.False(File.Exists(fixture.ConfigPath));
        Commit(fixture.Controller, true, new BlockPos(-10, 70, -20, 0));
        Commit(fixture.Controller, false, new BlockPos(-5, 75, -15, 0));
        persistence.Dispose();
        Assert.Equal(original, File.ReadAllText(previousPath));
        Assert.Equal(previousWriteTime, File.GetLastWriteTimeUtc(previousPath));
        fixture.Api.Verify(api => api.LoadModConfig(previousName), Times.Never);
        fixture.Api.Verify(api => api.StoreModConfig(It.IsAny<JsonObject>(), previousName), Times.Never);
        Assert.All(fixture.Writes, name => Assert.StartsWith(WorkAreaPersistence.ConfigFileName + ".", name));
        var restored = new WorkAreaController();
        using var reopened = fixture.Open(restored);
        Assert.Equal(new WorkAreaEndpoint(-10, 70, -20, 0), restored.State.Start);
        Assert.Equal(new WorkAreaEndpoint(-5, 75, -15, 0), restored.State.End);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WholeBoxGizmo_PersistsOnlyCompleteTranslationAndRollsBackCanceledPairBeforeDirtyRetry(bool grabbed)
    {
        using var fixture = new Fixture();
        fixture.Write(Config(("world-a", Pair(Endpoint(-30, 65, -200), Endpoint(30, 75, -200)))));
        using var persistence = fixture.Open();
        using var h = new WorkAreaGizmoSessionTests.Harness(fixture.Api, fixture.World,
            fixture.Accessor, fixture.Controller, grabbed);
        h.Enable();
        Move(4, 2);
        Assert.Empty(fixture.Writes);
        Assert.Equal(new WorkAreaEndpoint(-26, 67, -200, 0), h.WorkArea.State.Start);
        Assert.Equal(new WorkAreaEndpoint(34, 77, -200, 0), h.WorkArea.State.End);
        h.Up();
        Assert.Single(fixture.Writes);
        var saved = h.WorkArea.CommittedEndpoints;
        Move(-10, -1);
        Assert.Equal(saved, h.WorkArea.CommittedEndpoints);
        Assert.Single(fixture.Writes);
        h.Key();
        h.Up();
        Assert.Equal(saved, h.WorkArea.CommittedEndpoints);
        Assert.Single(fixture.Writes);
        fixture.FailWrites = true;
        Move(2, 1);
        h.Up();
        Assert.True(persistence.IsDirty);
        Assert.Equal(2, fixture.Writes.Count);
        var committed = h.WorkArea.CommittedEndpoints;
        Move(-6, -2);
        Assert.Equal(2, fixture.Writes.Count);
        fixture.FailWrites = false;
        h.Key();
        h.Up();
        Assert.Equal(committed, h.WorkArea.CommittedEndpoints);
        Assert.True(persistence.IsDirty);
        Move(0, 0);
        h.Up();
        Assert.False(persistence.IsDirty);
        Assert.Equal(3, fixture.Writes.Count);
        var restored = new WorkAreaController();
        using var reopened = fixture.Open(restored);
        Assert.Equal(committed, restored.CommittedEndpoints);
        Assert.Equal((61L, 11L, 1L), h.Size);
        h.AssertNoTargeting();

        void Move(int x, int y)
        {
            h.Frame();
            var center = h.Session.Center!;
            var origin = center.Value.Coordinates;
            h.Aim(origin);
            Assert.True(h.Down().Handled);
            Assert.Same(center, h.Session.Manager.CapturedGizmo);
            h.Aim(origin + Gizmos.PositionGizmoTests.V(x, y, 0));
            h.Move(h.X, h.Y);
            for (int frame = 0; frame < 120; frame++) h.Frame();
            h.AssertLiveBox();
        }
    }

    [Fact]
    public void LegacyMixedCorners_LoadNormalizedAndNextEditPersistsCanonicalPairWithoutSchemaChange()
    {
        using var fixture = new Fixture();
        fixture.Write(Config(("world-a", Pair(Endpoint(30, 60, -10), Endpoint(-20, 80, -40)))));
        using var persistence = fixture.Open();
        var controller = fixture.Controller;
        Assert.Equal(new WorkAreaEndpoint(-20, 60, -40, 0), controller.State.Start);
        Assert.Equal(new WorkAreaEndpoint(30, 80, -10, 0), controller.State.End);
        Assert.Empty(fixture.Writes);
        Assert.False(persistence.IsDirty);
        controller.SetContext(true, true);
        controller.BeginEndpointEdit(WorkAreaEndpointRole.Start);
        controller.PreviewEndpoint(new(50, 70, -100, 0), fixture.Accessor.Object);
        Assert.Empty(fixture.Writes);
        controller.CompleteEndpointEdit();
        var saved = fixture.Read();
        Assert.Single(fixture.Writes);
        Assert.Equal(2, saved["schemaVersion"]!.Value<int>());
        Assert.Equal(30, saved["worlds"]!["world-a"]!["areas"]![0]!["start"]!["x"]!.Value<int>());
        Assert.Equal(50, saved["worlds"]!["world-a"]!["areas"]![0]!["end"]!["x"]!.Value<int>());
        var restored = new WorkAreaController();
        using var reopened = fixture.Open(restored);
        Assert.Equal(controller.State.Start, restored.State.Start);
        Assert.Equal(controller.State.End, restored.State.End);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionSession_RepeatedAdapterDragsWriteFinalCellsOnlyAndRetryDirtyBaseline(bool grabbed)
    {
        using var fixture = new Fixture();
        fixture.Write(Config(("world-a", Pair(Endpoint(-30, 65, -200), Endpoint(30, 65, -200)))));
        using var persistence = fixture.Open();
        using var h = new WorkAreaGizmoSessionTests.Harness(fixture.Api, fixture.World,
            fixture.Accessor, fixture.Controller, grabbed);
        h.Enable();
        for (int i = 0; i < 8; i++)
        {
            h.Frame();
            bool start = i % 2 == 0;
            var instance = start ? h.Session.Start! : h.Session.End!;
            h.Aim(instance.Value.Coordinates);
            Assert.True(h.Down().Handled);
            Assert.Same(instance, h.Session.Manager.CapturedGizmo);
            var cell = new WorkAreaEndpoint(start ? -60 - i : 60 + i, 65 + i, -200, 0);
            for (int preview = 0; preview < 5; preview++)
            {
                h.MoveCenterTo(cell with { X = cell.X + preview });
                h.Frame();
                Assert.Equal(i, fixture.Writes.Count);
            }
            Assert.True(h.Up().Handled);
            Assert.Equal(i + 1, fixture.Writes.Count);
            var record = fixture.Read()["worlds"]!["world-a"]!["areas"]![0]![start ? "start" : "end"]!;
            var committedCell = (start ? h.WorkArea.State.Start : h.WorkArea.State.End)!.Value;
            Assert.Equal(committedCell.X, record["x"]!.Value<int>());
            Assert.Equal(committedCell.Y, record["y"]!.Value<int>());
            Assert.Equal(committedCell.Z, record["z"]!.Value<int>());
            Assert.Equal(h.WorkArea.State.Bounds!.Value.MinCell, h.WorkArea.State.Start);
            Assert.Equal(h.WorkArea.State.Bounds.Value.MaxCell, h.WorkArea.State.End);
            h.Frame();
            h.Aim(instance.Value.Coordinates);
            Assert.True(h.Down().Handled);
            for (int frame = 0; frame < 120; frame++) h.Frame();
            Assert.Equal(i + 1, fixture.Writes.Count);
            Assert.True(h.Up().Handled);
            Assert.Equal(i + 1, fixture.Writes.Count);
        }
        fixture.FailWrites = true;
        h.Frame();
        h.Aim(h.Session.Start!.Value.Coordinates);
        h.Down();
        h.MoveCenterTo(new(-90, 75, -200, 0));
        h.Frame();
        h.Up();
        Assert.True(persistence.IsDirty);
        var committed = h.WorkArea.State.Start;
        var committedEnd = h.WorkArea.State.End;
        Assert.Equal(9, fixture.Writes.Count);
        fixture.FailWrites = false;
        h.Frame();
        h.Aim(h.Session.Start.Value.Coordinates);
        h.Down();
        h.MoveCenterTo(new(100, 80, -200, 0));
        h.Frame();
        Assert.Equal(9, fixture.Writes.Count);
        h.Events.Raise(e => e.LeaveWorld += null!);
        Assert.Equal(committed, h.WorkArea.State.Start);
        persistence.Dispose();
        Assert.False(persistence.IsDirty);
        Assert.Equal(10, fixture.Writes.Count);
        Assert.Equal(committed!.Value.X, fixture.Read()["worlds"]!["world-a"]!["areas"]![0]!["start"]!["x"]!.Value<int>());
        var restored = new WorkAreaController();
        using var reopened = fixture.Open(restored);
        Assert.Equal(committed, restored.State.Start);
        Assert.Equal(committedEnd, restored.State.End);
        foreach (var (name, cell) in new[] { ("start", committed!.Value), ("end", committedEnd!.Value) })
        {
            var saved = fixture.Read()["worlds"]!["world-a"]!["areas"]![0]![name]!;
            Assert.Equal(cell.X, saved["x"]!.Value<int>());
            Assert.Equal(cell.Y, saved["y"]!.Value<int>());
            Assert.Equal(cell.Z, saved["z"]!.Value<int>());
            Assert.Equal(cell.Dimension, saved["dimension"]!.Value<int>());
        }
        h.AssertNoTargeting();
        Assert.False(restored.State.ShowWorkArea);
        Assert.False(restored.State.CanPick);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProductionSession_LargeCoordinatesAndBoundaryEndAnchorsPersistExactCellsAfterReload(bool grabbed)
    {
        using var fixture = new Fixture();
        fixture.Write(Config(("world-a", Pair(
            Endpoint(-2000000000, 32767, 2000000000, 65535),
            Endpoint(-1999999980, 32767, 2000000020, 65535)))));
        using var persistence = fixture.Open();
        using var h = new WorkAreaGizmoSessionTests.Harness(fixture.Api, fixture.World,
            fixture.Accessor, fixture.Controller, grabbed);
        h.Player.Entity.Pos.Dimension = 65535;
        h.Reference = Gizmos.PositionGizmoTests.V(-2000000000, 32767, 2000000200);
        h.Enable();
        for (int drag = 0; drag < 2; drag++)
        {
            bool start = drag == 0;
            h.Frame();
            var instance = start ? h.Session.Start! : h.Session.End!;
            var original = (start ? h.WorkArea.State.Start : h.WorkArea.State.End)!.Value;
            h.Aim(instance.Value.Coordinates);
            Assert.True(h.Down().Handled);
            Assert.Same(instance, h.Session.Manager.CapturedGizmo);
            h.MoveCenterTo(original with { X = original.X + 2 + drag });
            h.Frame();
            h.AssertLiveBox();
            Assert.Equal(drag, fixture.Writes.Count);
            Assert.Equal(32768d, h.Session.End!.Value.Coordinates[1]);
            Assert.Equal(65535, instance.Value.Dimension);
            h.Up();
            Assert.Equal(drag + 1, fixture.Writes.Count);
        }
        var committed = h.WorkArea.CommittedEndpoints;
        h.AssertNoTargeting();
        var restored = new WorkAreaController();
        using var reopened = fixture.Open(restored);
        Assert.Equal(committed, restored.CommittedEndpoints);
        Assert.Equal(new WorkAreaEndpoint(-1999999998, 32767, 2000000000, 65535), restored.State.Start);
        Assert.Equal(new WorkAreaEndpoint(-1999999977, 32767, 2000000020, 65535), restored.State.End);
        Assert.False(restored.State.ShowWorkArea);
        Assert.False(restored.IsEditing);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GizmoValuePreviews_WriteNothingUntilOneCompletionAndNeverSaveCancellation(bool start)
    {
        using var fixture = new Fixture();
        fixture.Write(Config(("world-a", Pair(Endpoint(10, 65, 20), Endpoint(14, 67, 23)))));
        using var persistence = fixture.Open();
        var controller = fixture.Controller;
        controller.SetContext(true, true);
        var role = start ? WorkAreaEndpointRole.Start : WorkAreaEndpointRole.End;
        var initial = (start ? controller.State.Start : controller.State.End)!.Value;
        var target = initial;
        var initialAnchor = WorkAreaGizmoCoordinates.ToAnchor(initial, role);
        using var gizmo = new PositionGizmo(initialAnchor, _ => { },
            edit => WorkAreaGizmoCoordinates.Constrain(edit, role, fixture.Accessor.Object));
        gizmo.OnInteractionStarted += _ => Assert.True(controller.BeginEndpointEdit(role));
        gizmo.OnValueChanged += anchor =>
        {
            Assert.True(WorkAreaGizmoCoordinates.TryDecode(anchor, role, fixture.Accessor.Object, out var cell));
            Assert.True(controller.PreviewEndpoint(cell, fixture.Accessor.Object));
        };
        gizmo.OnInteractionCompleted += _ => Assert.True(controller.CompleteEndpointEdit());
        gizmo.OnInteractionCanceled += _ => controller.CancelEndpointEdit();
        var origin = initialAnchor.Coordinates + Gizmos.PositionGizmoTests.V(0.25, -0.25, 10);
        var context = Gizmos.PositionGizmoTests.Camera(reference: origin);
        var pointer = new GizmoPointer(context, new(origin, Gizmos.PositionGizmoTests.V(0, 0, -1)));
        Assert.True(gizmo.BeginInteraction(PositionGizmo.Center, pointer));
        for (int i = 1; i <= 100; i++)
        {
            target = new(initial.X + i, initial.Y, initial.Z, initial.Dimension);
            gizmo.UpdateInteraction(new(context, new(origin + Gizmos.PositionGizmoTests.V(i, 0, 0),
                Gizmos.PositionGizmoTests.V(0, 0, -1))));
            Assert.Empty(fixture.Writes);
            Assert.False(persistence.IsDirty);
            Assert.True(persistence.TrySave());
        }
        gizmo.CompleteInteraction();
        Assert.Single(fixture.Writes);
        var expected = new WorkAreaBounds(target, start ? new(14, 67, 23, 0) : new(10, 65, 20, 0));
        Assert.Equal(expected.MinCell, controller.State.Start);
        Assert.Equal(expected.MaxCell, controller.State.End);
        Assert.Equal(start ? expected.MinCell.X : expected.MaxCell.X,
            fixture.Read()["worlds"]!["world-a"]!["areas"]![0]![start ? "start" : "end"]!["x"]!.Value<int>());
        var committed = controller.State;
        var committedPointer = new GizmoPointer(context, new(origin + Gizmos.PositionGizmoTests.V(100, 0, 0),
            Gizmos.PositionGizmoTests.V(0, 0, -1)));
        Assert.True(gizmo.BeginInteraction(PositionGizmo.Center, committedPointer));
        target = target with { X = target.X + 50 };
        gizmo.UpdateInteraction(new(context, new(origin + Gizmos.PositionGizmoTests.V(150, 0, 0),
            Gizmos.PositionGizmoTests.V(0, 0, -1))));
        gizmo.CancelInteraction();
        Assert.Equal(committed, controller.State);
        Assert.Single(fixture.Writes);
        Assert.False(persistence.IsDirty);
    }

    [Fact]
    public void ExplicitSaveDuringDirtyPreview_WritesCommittedPairWithoutCancelingTheEdit()
    {
        using var fixture = new Fixture();
        using var persistence = fixture.Open();
        var controller = fixture.Controller;
        controller.SetContext(true, true);
        fixture.FailWrites = true;
        Assert.True(controller.TrySetEndpoints(new(10, 65, 20, 0), new(14, 67, 23, 0), fixture.Accessor.Object));
        Assert.True(persistence.IsDirty);
        Assert.True(controller.BeginEndpointEdit(WorkAreaEndpointRole.End));
        Assert.True(controller.PreviewEndpoint(new(99, 80, 99, 0), fixture.Accessor.Object));
        fixture.FailWrites = false;
        Assert.True(persistence.TrySave());
        Assert.True(controller.IsEditing);
        Assert.Equal(99, controller.State.End!.Value.X);
        Assert.Equal(14, fixture.Read()["worlds"]!["world-a"]!["areas"]![0]!["end"]!["x"]!.Value<int>());
        Assert.False(persistence.IsDirty);
        controller.CancelEndpointEdit();
        Assert.Equal(14, controller.State.End!.Value.X);
        Assert.Equal(2, fixture.Writes.Count);
    }

    [Fact]
    public void ExternalReplacementDuringDirtyPreview_SavesIncomingAuthorityNotStalePreviewOrRollback()
    {
        using var fixture = new Fixture();
        using var persistence = fixture.Open();
        var controller = fixture.Controller;
        controller.SetContext(true, true);
        fixture.FailWrites = true;
        controller.TrySetEndpoint(WorkAreaEndpointRole.Start, new(10, 65, 20, 0), fixture.Accessor.Object);
        controller.BeginEndpointEdit(WorkAreaEndpointRole.Start);
        controller.PreviewEndpoint(new(99, 80, 99, 0), fixture.Accessor.Object);
        var replacement = new WorkAreaEndpoint(-40, 70, -50, 2);
        controller.RestoreEndpoints(null, replacement);
        controller.CancelEndpointEdit();
        Assert.False(controller.IsEditing);
        Assert.Equal(1, fixture.Writes.Count);
        fixture.FailWrites = false;
        Assert.True(persistence.TrySave());
        var restored = new WorkAreaController();
        using var fresh = fixture.Open(restored);
        Assert.Null(restored.State.Start);
        Assert.Equal(replacement, restored.State.End);
    }

    [Fact]
    public void DirtyCancel_DoesNotRetryAndRevertingToSavedPairDoesNotWriteRedundantly()
    {
        using var fixture = new Fixture();
        using var persistence = fixture.Open();
        var controller = fixture.Controller;
        controller.SetContext(true, true);
        var saved = new WorkAreaEndpoint(10, 65, 20, 0);
        controller.TrySetEndpoint(WorkAreaEndpointRole.Start, saved, fixture.Accessor.Object);
        fixture.FailWrites = true;
        controller.TrySetEndpoint(WorkAreaEndpointRole.Start, new(30, 70, 40, 0), fixture.Accessor.Object);
        controller.BeginEndpointEdit(WorkAreaEndpointRole.Start);
        controller.PreviewEndpoint(new(99, 80, 99, 0), fixture.Accessor.Object);
        fixture.FailWrites = false;
        controller.CancelEndpointEdit();
        Assert.True(persistence.IsDirty);
        Assert.Equal(2, fixture.Writes.Count);
        Assert.Equal(30, controller.State.Start!.Value.X);
        controller.TrySetEndpoint(WorkAreaEndpointRole.Start, saved, fixture.Accessor.Object);
        Assert.False(persistence.IsDirty);
        Assert.Equal(2, fixture.Writes.Count);
        Assert.Equal(saved.X, fixture.Read()["worlds"]!["world-a"]!["areas"]![0]!["start"]!["x"]!.Value<int>());
    }

    [Fact]
    public void ChangedCompletionFailure_RetainsCommitAndNoOpCompletionRetriesOnlyWhileDirty()
    {
        using var fixture = new Fixture();
        using var persistence = fixture.Open();
        var controller = fixture.Controller;
        controller.SetContext(true, true);
        controller.TrySetEndpoint(WorkAreaEndpointRole.Start, new(10, 65, 20, 0), fixture.Accessor.Object);
        fixture.FailWrites = true;
        controller.BeginEndpointEdit(WorkAreaEndpointRole.Start);
        controller.PreviewEndpoint(new(30, 70, 40, 0), fixture.Accessor.Object);
        controller.CompleteEndpointEdit();
        Assert.True(persistence.IsDirty);
        Assert.Equal(30, controller.State.Start!.Value.X);
        Assert.Contains("vanillabuildingexpanded:workarea-persistence-save-failed", fixture.Errors);
        fixture.FailWrites = false;
        controller.BeginEndpointEdit(WorkAreaEndpointRole.Start);
        controller.CompleteEndpointEdit();
        Assert.False(persistence.IsDirty);
        Assert.Equal(30, fixture.Read()["worlds"]!["world-a"]!["areas"]![0]!["start"]!["x"]!.Value<int>());
        int writes = fixture.Writes.Count;
        controller.BeginEndpointEdit(WorkAreaEndpointRole.Start);
        controller.CompleteEndpointEdit();
        Assert.Equal(writes, fixture.Writes.Count);
    }

    [Fact]
    public void Dispose_CancelsPreviewBeforeDirtyRetryAndRestartRestoresOnlyTheCommittedPair()
    {
        using var fixture = new Fixture();
        var persistence = fixture.Open();
        var controller = fixture.Controller;
        controller.SetContext(true, true);
        fixture.FailWrites = true;
        controller.TrySetEndpoint(WorkAreaEndpointRole.End, new(14, 67, 23, 2), fixture.Accessor.Object);
        Assert.True(persistence.IsDirty);
        controller.BeginEndpointEdit(WorkAreaEndpointRole.End);
        controller.PreviewEndpoint(new(99, 80, 99, 2), fixture.Accessor.Object);
        fixture.FailWrites = false;
        persistence.Dispose();
        Assert.False(controller.IsEditing);
        Assert.Equal(new WorkAreaEndpoint(14, 67, 23, 2), controller.State.End);
        var restored = new WorkAreaController();
        using var fresh = fixture.Open(restored);
        Assert.Null(restored.State.Start);
        Assert.Equal(controller.State.End, restored.State.End);
        Assert.False(fresh.IsDirty);
        Assert.Equal(2, fixture.Writes.Count);
    }

    [Fact]
    public void DirectEdits_SaveExtremeCellsNotRoleOffsetAnchorsAndPreserveSchema()
    {
        using var fixture = new Fixture();
        using var persistence = fixture.Open();
        fixture.Controller.SetContext(true, true);
        var end = new WorkAreaEndpoint(int.MaxValue, 32767, int.MinValue, 65535);
        var anchor = WorkAreaGizmoCoordinates.ToAnchor(end, WorkAreaEndpointRole.End);
        Assert.True(WorkAreaGizmoCoordinates.TryDecode(anchor, WorkAreaEndpointRole.End, fixture.Accessor.Object, out var decoded));
        Assert.True(fixture.Controller.TrySetEndpoint(WorkAreaEndpointRole.End, decoded, fixture.Accessor.Object));
        var restored = new WorkAreaController();
        using var fresh = fixture.Open(restored);
        Assert.Equal(end, restored.State.End);
        Assert.Null(restored.State.Start);
        Assert.Equal(2, fixture.Read()["schemaVersion"]!.Value<int>());
        Assert.Equal(new[] { "areas" }, ((JObject)fixture.Read()["worlds"]!["world-a"]!).Properties().Select(p => p.Name));
    }

    [Fact]
    public void MissingFile_IsNormalFirstUseAndDoesNotWriteUntilAnEndpointCommit()
    {
        using var fixture = new Fixture();
        using var persistence = fixture.Open();

        Assert.False(File.Exists(fixture.ConfigPath));
        Assert.False(persistence.IsDirty);
        Assert.Empty(fixture.Errors);
        Assert.Empty(fixture.Writes);
        Assert.Null(fixture.Controller.State.Bounds);
        Commit(fixture.Controller, true, new BlockPos(-10, 65, -20, 0));

        Assert.False(persistence.IsDirty);
        Assert.Single(fixture.Writes);
        Assert.Equal(2, fixture.Read()["schemaVersion"]!.Value<int>());
        Assert.Equal(-10, fixture.Read()["worlds"]!["world-a"]!["areas"]![0]!["start"]!["x"]!.Value<int>());
        Assert.Empty(Directory.GetFiles(GamePaths.ModConfig, "*.tmp"));
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 2)]
    [InlineData(false, true, 0)]
    [InlineData(true, true, 2)]
    public void Endpoints_RoundTripEmptyPartialCompleteNegativeAndDimensionRecords(
        bool hasStart, bool hasEnd, int dimension)
    {
        using var fixture = new Fixture();
        fixture.Write(Config(("world-a", Pair(null, null))));
        using (var persistence = fixture.Open())
        {
            fixture.Controller.SetVisibility(true);
            if (hasStart) Commit(fixture.Controller, true, new BlockPos(-10, 65, -20, dimension));
            if (hasEnd) Commit(fixture.Controller, false, new BlockPos(-5, 70, -15, dimension));
        }
        var saved = fixture.Controller.State;
        var fresh = new WorkAreaController();
        using var reopened = fixture.Open(fresh);

        Assert.Equal(saved.Start, fresh.State.Start);
        Assert.Equal(saved.End, fresh.State.End);
        Assert.Equal(saved.Bounds, fresh.State.Bounds);
        Assert.False(fresh.State.ShowWorkArea);
        Assert.False(fresh.State.IsBuildModeEnabled);
        Assert.False(fresh.State.IsPlayerAlive);
        Assert.False(fresh.IsEditing);
        Assert.False(reopened.IsDirty);
        Assert.Empty(fixture.Errors);
        var record = Assert.IsType<JObject>(fixture.Read()["worlds"]!["world-a"]!["areas"]![0]);
        Assert.Equal(new[] { "id", "name", "start", "end" }, record.Properties().Select(p => p.Name));
        foreach (var endpoint in record.Properties().Select(p => p.Value).OfType<JObject>())
            Assert.Equal(new[] { "x", "y", "z", "dimension" }, endpoint.Properties().Select(p => p.Name));
    }

    [Fact]
    public void TransientChangesAndUnchangedCommits_DoNotWriteOrSerializeSessionState()
    {
        using var fixture = new Fixture();
        using var persistence = fixture.Open();
        fixture.Controller.SetContext(true, true);
        fixture.Controller.SetVisibility(true);
        fixture.Controller.CancelEndpointEdit();
        fixture.Controller.SetContext(false, false);
        Assert.Empty(fixture.Writes);

        Commit(fixture.Controller, true, new BlockPos(10, 65, 20, 0));
        Assert.Single(fixture.Writes);
        Commit(fixture.Controller, true, new BlockPos(10, 65, 20, 0));
        Assert.Single(fixture.Writes);
        Assert.Equal(new[] { "schemaVersion", "worlds" }, fixture.Read().Properties().Select(p => p.Name));
        Assert.False(persistence.IsDirty);
    }

    [Fact]
    public void WorldIdentities_IsolateLocalMultiplayerReplacementAndCaseSensitiveEntries()
    {
        using var fixture = new Fixture();
        string[] ids = ["local-save-a", "local-save-b", "server-a-original-save", "server-b-save",
            "server-a-replacement-save", "LOCAL-SAVE-A"];
        for (int i = 0; i < ids.Length; i++)
        {
            fixture.WorldId = ids[i];
            var controller = new WorkAreaController();
            using var persistence = fixture.Open(controller);
            Assert.Null(controller.State.Start);
            Commit(controller, true, new BlockPos(i + 10, 65, 20, 0));
        }
        Assert.Equal(ids.Length, ((JObject)fixture.Read()["worlds"]!).Count);
        for (int i = 0; i < ids.Length; i++)
        {
            fixture.WorldId = ids[i];
            var controller = new WorkAreaController();
            using var persistence = fixture.Open(controller);
            Assert.Equal(new WorkAreaEndpoint(i + 10, 65, 20, 0), controller.State.Start);
        }
        Assert.Empty(fixture.Errors);
    }

    [Fact]
    public void Save_RereadsOtherWorldsAndPreservesUnknownOrInvalidUnrelatedJson()
    {
        using var fixture = new Fixture();
        var root = Config(("world-a", Pair(null, null)), ("other-world", new JValue("keep invalid data")));
        root["metadata"] = new JObject { ["future"] = true };
        ((JObject)root["worlds"]!["world-a"]!)["note"] = "keep this too";
        fixture.Write(root);
        using var persistence = fixture.Open();
        root["worlds"]!["added-while-playing"] = new JObject { ["raw"] = 123 };
        fixture.Write(root);
        Commit(fixture.Controller, true, new BlockPos(10, 65, 20, 0));
        var saved = fixture.Read();

        Assert.True(JToken.DeepEquals(root["metadata"], saved["metadata"]));
        Assert.True(JToken.DeepEquals(root["worlds"]!["other-world"], saved["worlds"]!["other-world"]));
        Assert.True(JToken.DeepEquals(root["worlds"]!["added-while-playing"], saved["worlds"]!["added-while-playing"]));
        Assert.Equal("keep this too", saved["worlds"]!["world-a"]!["note"]!.Value<string>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CrossDimensionCommit_WritesOneCoherentPartialReset(bool setStart)
    {
        using var fixture = new Fixture();
        using var persistence = fixture.Open();
        Commit(fixture.Controller, true, new BlockPos(10, 65, 20, 0));
        Commit(fixture.Controller, false, new BlockPos(14, 67, 23, 0));
        int before = fixture.Writes.Count;

        Commit(fixture.Controller, setStart, new BlockPos(30, 80, 40, 2));
        Assert.Equal(before + 1, fixture.Writes.Count);
        var entry = fixture.Read()["worlds"]!["world-a"]!["areas"]![0]!;
        Assert.Equal(JTokenType.Null, entry[setStart ? "end" : "start"]!.Type);
        Assert.Equal(2, entry[setStart ? "start" : "end"]!["dimension"]!.Value<int>());
        Assert.Null(fixture.Controller.State.Bounds);
        Assert.False(persistence.IsDirty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingIdentity_IsReportedMemoryOnlyAndNeverLoadsOrUsesAGlobalFallback(string? id)
    {
        using var fixture = new Fixture { WorldId = id };
        using var persistence = fixture.Open();
        Assert.Contains("vanillabuildingexpanded:workarea-persistence-no-world", fixture.Errors);
        Assert.Equal(0, fixture.ReadCount);
        Commit(fixture.Controller, true, new BlockPos(10, 65, 20, 0));
        Assert.True(persistence.IsDirty);
        Assert.False(persistence.TrySave());
        fixture.WorldId = "different-session";
        Assert.False(persistence.TrySave());
        Assert.Empty(fixture.Writes);
        Assert.Equal(0, fixture.ReadCount);
        Assert.NotNull(fixture.Controller.State.Start);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":\"1\",\"worlds\":{}}")]
    [InlineData("{\"schemaVersion\":2.0,\"worlds\":{}}")]
    [InlineData("{\"schemaVersion\":2147483648,\"worlds\":{}}")]
    [InlineData("{\"schemaVersion\":2,\"worlds\":null}")]
    [InlineData("{\"schemaVersion\":2,\"worlds\":[]}")]
    public void UnreadableOrMalformedFiles_AreReportedAndNeverReplacedWithDefaults(string json)
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.ConfigPath, json);
        using var persistence = fixture.Open();
        Assert.Contains("vanillabuildingexpanded:workarea-persistence-load-failed", fixture.Errors);
        Commit(fixture.Controller, true, new BlockPos(10, 65, 20, 0));
        Assert.True(persistence.IsDirty);
        Assert.False(persistence.TrySave());
        Assert.Equal(json, File.ReadAllText(fixture.ConfigPath));
        Assert.Empty(fixture.Writes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void UnsupportedSchema_IsDistinctAndProtectedUntilFileIsRepaired(int version)
    {
        using var fixture = new Fixture();
        var root = Config(("other-world", Pair(Endpoint(5, 60, 6), null)));
        root["schemaVersion"] = version;
        string original = fixture.Write(root);
        using var persistence = fixture.Open();
        Commit(fixture.Controller, true, new BlockPos(10, 65, 20, 0));

        Assert.Contains("vanillabuildingexpanded:workarea-persistence-unsupported", fixture.Errors);
        Assert.Equal(original, File.ReadAllText(fixture.ConfigPath));
        Assert.True(persistence.IsDirty);
        Assert.Empty(fixture.Writes);
        root["schemaVersion"] = 2;
        fixture.Write(root);
        Assert.True(persistence.TrySave());
        Assert.False(persistence.IsDirty);
        Assert.True(JToken.DeepEquals(root["worlds"]!["other-world"], fixture.Read()["worlds"]!["other-world"]));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"start\":null}")]
    [InlineData("{\"start\":{},\"end\":null}")]
    [InlineData("{\"start\":{\"x\":1,\"y\":65,\"z\":2},\"end\":null}")]
    [InlineData("{\"start\":{\"x\":\"1\",\"y\":65,\"z\":2,\"dimension\":0},\"end\":null}")]
    [InlineData("{\"start\":{\"x\":1.5,\"y\":65,\"z\":2,\"dimension\":0},\"end\":null}")]
    [InlineData("{\"start\":{\"x\":2147483648,\"y\":65,\"z\":2,\"dimension\":0},\"end\":null}")]
    [InlineData("{\"start\":{\"x\":1,\"y\":-1,\"z\":2,\"dimension\":0},\"end\":null}")]
    [InlineData("{\"start\":{\"x\":1,\"y\":32768,\"z\":2,\"dimension\":0},\"end\":null}")]
    [InlineData("{\"start\":{\"x\":1,\"y\":65,\"z\":2,\"dimension\":-1},\"end\":null}")]
    [InlineData("{\"start\":{\"x\":1,\"y\":65,\"z\":2,\"dimension\":2147483647},\"end\":null}")]
    [InlineData("{\"start\":{\"x\":1,\"y\":65,\"z\":2,\"dimension\":0},\"end\":{\"x\":3,\"y\":66,\"z\":4,\"dimension\":2}}")]
    public void InvalidActiveEntries_AreReportedAndRetainedUntilAnExplicitValidCommit(string entry)
    {
        using var fixture = new Fixture();
        var invalid = JToken.Parse(entry);
        if (invalid is JObject area && area.ContainsKey("start"))
        {
            area["id"] = "11111111111111111111111111111111";
            area["name"] = "First";
            invalid = new JObject { ["areas"] = new JArray(area) };
        }
        var root = Config(("world-a", invalid), ("other-world", new JValue("preserve")));
        string original = fixture.Write(root);
        using var persistence = fixture.Open();

        Assert.Null(fixture.Controller.State.Start);
        Assert.Null(fixture.Controller.State.End);
        Assert.Contains("vanillabuildingexpanded:workarea-persistence-invalid-entry", fixture.Errors);
        Assert.Equal(original, File.ReadAllText(fixture.ConfigPath));
        Assert.Empty(fixture.Writes);
        Assert.False(persistence.IsDirty);
        Commit(fixture.Controller, false, new BlockPos(10, 65, 20, 0));
        Assert.False(persistence.IsDirty);
        Assert.Single(fixture.Writes);
        Assert.Equal("preserve", fixture.Read()["worlds"]!["other-world"]!.Value<string>());
        Assert.Equal(JTokenType.Null, fixture.Read()["worlds"]!["world-a"]!["areas"]![0]!["start"]!.Type);
    }

    [Fact]
    public void Validation_UsesWorldBoundaryAuthorityWithoutReadingBlocksOrLoadedDimensions()
    {
        using var fixture = new Fixture();
        fixture.ValidPosition = pos => pos.X >= 0 && pos.X < 100 && pos.Y < 256 && pos.dimension != 3;
        fixture.Write(Config(("world-a", Pair(Endpoint(-1, 65, 10), null))));
        using (var invalid = fixture.Open())
        {
            Assert.Null(fixture.Controller.State.Start);
            Assert.Contains("vanillabuildingexpanded:workarea-persistence-invalid-entry", fixture.Errors);
        }
        fixture.Write(Config(("world-a", Pair(Endpoint(10, 65, 20, 2), null))));
        var fresh = new WorkAreaController();
        using var valid = fixture.Open(fresh);
        Assert.Equal(new WorkAreaEndpoint(10, 65, 20, 2), fresh.State.Start);
        fixture.Accessor.Verify(a => a.IsValidPos(It.Is<BlockPos>(p => p.Y == 65 && p.dimension == 2)), Times.Once);
        // Strict mocks have no block/chunk reads or dimension-loading setup.
        Assert.False(valid.IsDirty);
    }

    [Fact]
    public void InvalidCommit_IsRetainedUnsavedAndReportedRatherThanPersistingInvalidCoordinates()
    {
        using var fixture = new Fixture();
        fixture.ValidPosition = p => p.dimension != 3;
        using var persistence = fixture.Open();
        Commit(fixture.Controller, true, new BlockPos(10, 65, 20, 3));

        Assert.True(persistence.IsDirty);
        Assert.NotNull(fixture.Controller.State.Start);
        Assert.Empty(fixture.Writes);
        Assert.Contains("vanillabuildingexpanded:workarea-persistence-save-failed", fixture.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteFailure_PreservesFileAndMemoryThenRetriesEvenAnIdenticalEndpoint(bool partialWrite)
    {
        using var fixture = new Fixture();
        string original = fixture.Write(Config(("other-world", Pair(Endpoint(4, 65, 5), null))));
        fixture.FailWrites = true;
        fixture.PartialWrite = partialWrite;
        using var persistence = fixture.Open();
        Commit(fixture.Controller, true, new BlockPos(10, 65, 20, 0));

        Assert.Equal(original, File.ReadAllText(fixture.ConfigPath));
        Assert.Equal(new WorkAreaEndpoint(10, 65, 20, 0), fixture.Controller.State.Start);
        Assert.True(persistence.IsDirty);
        Assert.Contains("vanillabuildingexpanded:workarea-persistence-save-failed", fixture.Errors);
        Assert.Empty(Directory.GetFiles(GamePaths.ModConfig, "*.tmp"));
        fixture.FailWrites = false;
        Commit(fixture.Controller, true, new BlockPos(10, 65, 20, 0));
        Assert.False(persistence.IsDirty);
        Assert.Equal(2, fixture.Writes.Count);
        Assert.Equal(4, fixture.Read()["worlds"]!["other-world"]!["areas"]![0]!["start"]!["x"]!.Value<int>());
    }

    [Fact]
    public void PromotionFailure_PreservesOriginalAndCleansStagingFile()
    {
        using var fixture = new Fixture();
        string original = fixture.Write(Config(("other-world", Pair(null, null))));
        using var persistence = fixture.Open();
        using (var locked = new FileStream(fixture.ConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Commit(fixture.Controller, true, new BlockPos(10, 65, 20, 0));
            Assert.True(persistence.IsDirty);
            Assert.Equal(original, File.ReadAllText(fixture.ConfigPath));
            Assert.Empty(Directory.GetFiles(GamePaths.ModConfig, "*.tmp"));
        }
        Assert.True(persistence.TrySave());
        Assert.False(persistence.IsDirty);
    }

    [Fact]
    public void ReadPermissionFailure_IsReportedAndDoesNotDestroyOtherEntries()
    {
        using var fixture = new Fixture();
        string original = fixture.Write(Config(("other-world", Pair(null, null))));
        fixture.ReadFailure = new UnauthorizedAccessException("test permission failure");
        using var persistence = fixture.Open();
        Commit(fixture.Controller, true, new BlockPos(10, 65, 20, 0));
        Assert.True(persistence.IsDirty);
        Assert.Equal(original, File.ReadAllText(fixture.ConfigPath));
        Assert.Empty(fixture.Writes);
        Assert.Contains("vanillabuildingexpanded:workarea-persistence-load-failed", fixture.Errors);
        fixture.ReadFailure = null;
        Assert.True(persistence.TrySave());
    }

    [Fact]
    public void Dispose_RetriesDirtyEditsOnceAndDetachesCommitSubscription()
    {
        using var fixture = new Fixture();
        fixture.FailWrites = true;
        var persistence = fixture.Open();
        Commit(fixture.Controller, true, new BlockPos(10, 65, 20, 0));
        Assert.True(persistence.IsDirty);
        fixture.FailWrites = false;
        persistence.Dispose();
        int count = fixture.Writes.Count;
        persistence.Dispose();
        Commit(fixture.Controller, false, new BlockPos(14, 67, 23, 0));

        Assert.False(persistence.IsDirty);
        Assert.Equal(2, count);
        Assert.Equal(count, fixture.Writes.Count);
        Assert.Equal(JTokenType.Null, fixture.Read()["worlds"]!["world-a"]!["areas"]![0]!["end"]!.Type);
        Assert.Throws<ObjectDisposedException>(() => persistence.TrySave());
    }

    [Fact]
    public void RestoreEndpoints_DoesNotCommitOrRestoreTransientStateAndRejectsMismatchedPairs()
    {
        var controller = new WorkAreaController();
        int commits = 0;
        controller.EndpointsCommitted += () => commits++;
        controller.RestoreEndpoints(new WorkAreaEndpoint(10, 65, 20, 2), null);

        Assert.Equal(0, commits);
        Assert.False(controller.State.IsBuildModeEnabled);
        Assert.False(controller.State.ShowWorkArea);
        var before = controller.State;
        Assert.Throws<ArgumentException>(() => controller.RestoreEndpoints(
            before.Start, new WorkAreaEndpoint(14, 67, 23, 0)));
        Assert.Equal(before, controller.State);
    }

    [Fact]
    public void ClientOwner_LoadsOnlyForLocalJoinAndDisposesBeforeWorldDeparture()
    {
        using var fixture = new Fixture();
        // The native interface's inherited range overloads cannot be proxied by Moq.
        var player = WorkAreaRenderingTests.CreatePlayer();
        var other = Assert.IsType<ClientPlayer>(RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer)));
        fixture.Api.Setup(a => a.Render).Returns(new Mock<IRenderAPI>().Object);
        fixture.Api.Setup(a => a.Event).Returns(new Mock<IClientEventAPI>().Object);
        fixture.World.Setup(w => w.Player).Returns(player);
        var brushWorld = TestHelpers.CreateMockWorld();
        var brushController = new BuildBrushControllerClient(fixture.Api.Object,
            () => TestHelpers.CreateTestInstance(brushWorld));
        var system = new BuildBrushSystem_Client();
        SetField(system, "api", fixture.Api.Object);
        SetField(system, "controller", brushController);
        try
        {
            Invoke(system, "Event_PlayerJoin", other);
            Assert.Equal(0, fixture.ReadCount);
            Invoke(system, "Event_PlayerJoin", player);
            var workArea = Assert.IsType<WorkAreaController>(GetField(system, "workArea"));
            var rendering = Assert.IsType<WorkAreaRenderBinding>(GetField(system, "workAreaRendering"));
            Assert.False(workArea.State.ShowWorkArea);
            Assert.False(workArea.State.IsBuildModeEnabled);
            int reads = fixture.ReadCount;
            Invoke(system, "Event_PlayerJoin", player);
            Assert.Same(workArea, GetField(system, "workArea"));
            Assert.Same(rendering, GetField(system, "workAreaRendering"));
            Assert.Equal(reads, fixture.ReadCount);
            fixture.FailWrites = true;
            Commit(workArea, true, new BlockPos(10, 65, 20, 0));
            Assert.True(workArea.BeginEndpointEdit(WorkAreaEndpointRole.Start));
            Assert.True(workArea.PreviewEndpoint(new(99, 80, 99, 0), fixture.Accessor.Object));
            fixture.FailWrites = false;
            Invoke(system, "Event_LeaveWorld");
            Assert.False(workArea.IsEditing);
            Assert.Equal(10, workArea.State.Start!.Value.X);
            Assert.Null(GetField(system, "workArea"));
            Assert.Null(GetField(system, "workAreaPersistence"));
            Assert.Null(GetField(system, "workAreaRendering"));
            Assert.Equal(10, fixture.Read()["worlds"]!["world-a"]!["areas"]![0]!["start"]!["x"]!.Value<int>());
            int writes = fixture.Writes.Count;
            Commit(workArea, false, new BlockPos(14, 67, 23, 0));
            Assert.Equal(writes, fixture.Writes.Count);
            Invoke(system, "Event_PlayerJoin", player);
            var fresh = Assert.IsType<WorkAreaController>(GetField(system, "workArea"));
            Assert.NotSame(workArea, fresh);
            Assert.NotSame(rendering, GetField(system, "workAreaRendering"));
            Assert.Equal(new WorkAreaEndpoint(10, 65, 20, 0), fresh.State.Start);
            Assert.Null(fresh.State.End);
            Assert.False(fresh.State.ShowWorkArea);
            Assert.False(fresh.State.IsBuildModeEnabled);
        }
        finally { Invoke(system, "Event_LeaveWorld"); }
    }

    private static void Commit(WorkAreaController controller, bool start, BlockPos position)
    {
        controller.SetContext(true, true);
        var accessor = new Mock<IBlockAccessor>();
        accessor.Setup(a => a.IsValidPos(It.IsAny<BlockPos>())).Returns(true);
        var cell = new WorkAreaEndpoint(position.X, position.Y, position.Z, position.dimension);
        var opposite = start ? controller.State.End : controller.State.Start;
        if (opposite?.Dimension != cell.Dimension) opposite = null;
        Assert.True(controller.TrySetEndpoints(start ? cell : opposite, start ? opposite : cell, accessor.Object));
    }

    private static JObject Endpoint(int x, int y, int z, int dimension = 0) =>
        new() { ["x"] = x, ["y"] = y, ["z"] = z, ["dimension"] = dimension };

    private static JObject Pair(JToken? start, JToken? end) =>
        new() { ["areas"] = new JArray(new JObject { ["id"] = "11111111111111111111111111111111",
            ["name"] = "First", ["start"] = start ?? JValue.CreateNull(), ["end"] = end ?? JValue.CreateNull() }) };

    private static JObject Config(params (string Id, JToken Value)[] entries)
    {
        var worlds = new JObject();
        foreach (var (id, value) in entries) worlds[id] = value;
        return new JObject { ["schemaVersion"] = 2, ["worlds"] = worlds };
    }

    private static object? GetField(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

    private static void Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);

    private sealed class Fixture : IDisposable
    {
        private readonly string originalDataPath = GamePaths.DataPath;
        private readonly string originalLocale = Lang.CurrentLocale;
        private const string Locale = "vbe-workarea-persistence-tests";
        private readonly string root = Path.Combine(Path.GetTempPath(), $"vbe-workarea-tests-{Guid.NewGuid():N}");
        public readonly Mock<ICoreClientAPI> Api = new(MockBehavior.Strict);
        public readonly Mock<IClientWorldAccessor> World = new(MockBehavior.Strict);
        public readonly Mock<IBlockAccessor> Accessor = new(MockBehavior.Strict);
        public WorkAreaController Controller { get; } = new();
        public string? WorldId { get; set; } = "world-a";
        public System.Func<BlockPos, bool> ValidPosition { get; set; } = _ => true;
        public bool FailWrites { get; set; }
        public bool PartialWrite { get; set; }
        public Exception? ReadFailure { get; set; }
        public int ReadCount { get; private set; }
        public List<string> Writes { get; } = [];
        public List<string> Errors { get; } = [];
        public List<string> LogErrors { get; } = [];
        public string ConfigPath => Path.Combine(GamePaths.ModConfig, WorkAreaPersistence.ConfigFileName);

        public Fixture()
        {
            GamePaths.DataPath = root;
            Directory.CreateDirectory(GamePaths.ModConfig);
            var translations = new Mock<ITranslationService>();
            translations.Setup(t => t.HasTranslation(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(true);
            translations.Setup(t => t.Get(It.IsAny<string>(), It.IsAny<object[]>()))
                .Returns((string key, object[] args) => key.EndsWith("workarea-default-name", StringComparison.Ordinal)
                    ? $"Work area {args[0]}" : $"translated {key}");
            Lang.AvailableLanguages.Add(Locale, translations.Object);
            Lang.ChangeLanguage(Locale);
            Api.Setup(a => a.World).Returns(World.Object);
            var logger = new Mock<ILogger>();
            logger.Setup(l => l.Error(It.IsAny<string>(), It.IsAny<object[]>()))
                .Callback<string, object[]>((format, args) => LogErrors.Add(string.Format(format, args)));
            Api.Setup(a => a.Logger).Returns(logger.Object);
            Api.Setup(a => a.TriggerIngameError(It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback<object, string, string>((_, code, text) =>
                {
                    Assert.Equal($"translated {code}", text);
                    if (code.Contains(":workarea-persistence-", StringComparison.Ordinal))
                        Assert.NotEmpty(LogErrors);
                    Errors.Add(code);
                });
            World.Setup(w => w.SavegameIdentifier).Returns(() => WorldId!);
            World.Setup(w => w.BlockAccessor).Returns(Accessor.Object);
            Accessor.Setup(a => a.IsValidPos(It.IsAny<BlockPos>())).Returns((BlockPos p) => ValidPosition(p));
            Api.Setup(a => a.LoadModConfig(WorkAreaPersistence.ConfigFileName)).Returns(() =>
            {
                ReadCount++;
                if (ReadFailure is { } failure) throw failure;
                return File.Exists(ConfigPath) ? new JsonObject(JObject.Parse(File.ReadAllText(ConfigPath))) : null!;
            });
            Api.Setup(a => a.StoreModConfig(It.IsAny<JsonObject>(), It.IsAny<string>()))
                .Callback<JsonObject, string>((document, filename) =>
                {
                    Writes.Add(filename);
                    string path = Path.Combine(GamePaths.ModConfig, filename);
                    if (FailWrites)
                    {
                        if (PartialWrite) File.WriteAllText(path, "partial");
                        throw new IOException("test write failure");
                    }
                    File.WriteAllText(path, document.ToString());
                });
        }

        public WorkAreaPersistence Open(WorkAreaController? controller = null) => new(Api.Object, controller ?? Controller);
        public JObject Read() => JObject.Parse(File.ReadAllText(ConfigPath));
        public string Write(JObject value)
        {
            string json = value.ToString();
            File.WriteAllText(ConfigPath, json);
            return json;
        }

        public void Dispose()
        {
            GamePaths.DataPath = originalDataPath;
            Lang.ChangeLanguage(originalLocale);
            Lang.AvailableLanguages.Remove(Locale);
            Directory.Delete(root, recursive: true);
        }
    }
}
