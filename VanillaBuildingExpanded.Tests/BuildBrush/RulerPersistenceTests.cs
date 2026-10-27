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

[CollectionDefinition("Ruler config files", DisableParallelization = true)]
public sealed class RulerConfigFileCollection;

[Collection("Ruler config files")]
public sealed class RulerPersistenceTests
{
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
        var role = start ? RulerEndpointRole.Start : RulerEndpointRole.End;
        var initial = (start ? controller.State.Start : controller.State.End)!.Value;
        var target = initial;
        using var gizmo = new PositionGizmo(RulerGizmoCoordinates.ToAnchor(initial, role), _ => { },
            _ => RulerGizmoCoordinates.ToAnchor(target, role),
            edit => RulerGizmoCoordinates.Constrain(edit, role, fixture.Accessor.Object));
        gizmo.OnInteractionStarted += _ => Assert.True(controller.BeginEndpointEdit(role));
        gizmo.OnValueChanged += anchor =>
        {
            Assert.True(RulerGizmoCoordinates.TryDecode(anchor, role, fixture.Accessor.Object, out var cell));
            Assert.True(controller.PreviewEndpoint(cell, fixture.Accessor.Object));
        };
        gizmo.OnInteractionCompleted += _ => Assert.True(controller.CompleteEndpointEdit());
        gizmo.OnInteractionCanceled += _ => controller.CancelEndpointEdit();
        var pointer = new GizmoPointer(new(0), new(
            Gizmos.PositionGizmoTests.V(0, 0, 10), Gizmos.PositionGizmoTests.V(0, 0, -1)));
        Assert.True(gizmo.BeginInteraction(PositionGizmo.Center, pointer));
        for (int i = 1; i <= 100; i++)
        {
            target = new(initial.X + i, initial.Y, initial.Z, initial.Dimension);
            gizmo.UpdateInteraction(pointer);
            Assert.Empty(fixture.Writes);
            Assert.False(persistence.IsDirty);
            Assert.True(persistence.TrySave());
        }
        gizmo.CompleteInteraction();
        Assert.Single(fixture.Writes);
        Assert.Equal(target, start ? controller.State.Start : controller.State.End);
        Assert.Equal(target.X, fixture.Read()["worlds"]!["world-a"]![start ? "start" : "end"]!["x"]!.Value<int>());
        var committed = controller.State;
        Assert.True(gizmo.BeginInteraction(PositionGizmo.Center, pointer));
        target = target with { X = target.X + 50 };
        gizmo.UpdateInteraction(pointer);
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
        Assert.True(controller.BeginEndpointEdit(RulerEndpointRole.End));
        Assert.True(controller.PreviewEndpoint(new(99, 80, 99, 0), fixture.Accessor.Object));
        fixture.FailWrites = false;
        Assert.True(persistence.TrySave());
        Assert.True(controller.IsEditing);
        Assert.Equal(99, controller.State.End!.Value.X);
        Assert.Equal(14, fixture.Read()["worlds"]!["world-a"]!["end"]!["x"]!.Value<int>());
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
        controller.TrySetEndpoint(RulerEndpointRole.Start, new(10, 65, 20, 0), fixture.Accessor.Object);
        controller.BeginEndpointEdit(RulerEndpointRole.Start);
        controller.PreviewEndpoint(new(99, 80, 99, 0), fixture.Accessor.Object);
        var replacement = new RulerEndpoint(-40, 70, -50, 2);
        controller.RestoreEndpoints(null, replacement);
        controller.CancelEndpointEdit();
        Assert.False(controller.IsEditing);
        Assert.Equal(1, fixture.Writes.Count);
        fixture.FailWrites = false;
        Assert.True(persistence.TrySave());
        var restored = new RulerController();
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
        var saved = new RulerEndpoint(10, 65, 20, 0);
        controller.TrySetEndpoint(RulerEndpointRole.Start, saved, fixture.Accessor.Object);
        fixture.FailWrites = true;
        controller.TrySetEndpoint(RulerEndpointRole.Start, new(30, 70, 40, 0), fixture.Accessor.Object);
        controller.BeginEndpointEdit(RulerEndpointRole.Start);
        controller.PreviewEndpoint(new(99, 80, 99, 0), fixture.Accessor.Object);
        fixture.FailWrites = false;
        controller.CancelEndpointEdit();
        Assert.True(persistence.IsDirty);
        Assert.Equal(2, fixture.Writes.Count);
        Assert.Equal(30, controller.State.Start!.Value.X);
        controller.TrySetEndpoint(RulerEndpointRole.Start, saved, fixture.Accessor.Object);
        Assert.False(persistence.IsDirty);
        Assert.Equal(2, fixture.Writes.Count);
        Assert.Equal(saved.X, fixture.Read()["worlds"]!["world-a"]!["start"]!["x"]!.Value<int>());
    }

    [Fact]
    public void ChangedCompletionFailure_RetainsCommitAndNoOpCompletionRetriesOnlyWhileDirty()
    {
        using var fixture = new Fixture();
        using var persistence = fixture.Open();
        var controller = fixture.Controller;
        controller.SetContext(true, true);
        controller.TrySetEndpoint(RulerEndpointRole.Start, new(10, 65, 20, 0), fixture.Accessor.Object);
        fixture.FailWrites = true;
        controller.BeginEndpointEdit(RulerEndpointRole.Start);
        controller.PreviewEndpoint(new(30, 70, 40, 0), fixture.Accessor.Object);
        controller.CompleteEndpointEdit();
        Assert.True(persistence.IsDirty);
        Assert.Equal(30, controller.State.Start!.Value.X);
        Assert.Contains("vanillabuildingexpanded:ruler-persistence-save-failed", fixture.Errors);
        fixture.FailWrites = false;
        controller.BeginEndpointEdit(RulerEndpointRole.Start);
        controller.CompleteEndpointEdit();
        Assert.False(persistence.IsDirty);
        Assert.Equal(30, fixture.Read()["worlds"]!["world-a"]!["start"]!["x"]!.Value<int>());
        int writes = fixture.Writes.Count;
        controller.BeginEndpointEdit(RulerEndpointRole.Start);
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
        controller.TrySetEndpoint(RulerEndpointRole.End, new(14, 67, 23, 2), fixture.Accessor.Object);
        Assert.True(persistence.IsDirty);
        controller.BeginEndpointEdit(RulerEndpointRole.End);
        controller.PreviewEndpoint(new(99, 80, 99, 2), fixture.Accessor.Object);
        fixture.FailWrites = false;
        persistence.Dispose();
        Assert.False(controller.IsEditing);
        Assert.Equal(new RulerEndpoint(14, 67, 23, 2), controller.State.End);
        var restored = new RulerController();
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
        var end = new RulerEndpoint(int.MaxValue, 32767, int.MinValue, 65535);
        var anchor = RulerGizmoCoordinates.ToAnchor(end, RulerEndpointRole.End);
        Assert.True(RulerGizmoCoordinates.TryDecode(anchor, RulerEndpointRole.End, fixture.Accessor.Object, out var decoded));
        Assert.True(fixture.Controller.TrySetEndpoint(RulerEndpointRole.End, decoded, fixture.Accessor.Object));
        var restored = new RulerController();
        using var fresh = fixture.Open(restored);
        Assert.Equal(end, restored.State.End);
        Assert.Null(restored.State.Start);
        Assert.Equal(1, fixture.Read()["schemaVersion"]!.Value<int>());
        Assert.Equal(new[] { "start", "end" }, ((JObject)fixture.Read()["worlds"]!["world-a"]!).Properties().Select(p => p.Name));
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
        Assert.Equal(1, fixture.Read()["schemaVersion"]!.Value<int>());
        Assert.Equal(-10, fixture.Read()["worlds"]!["world-a"]!["start"]!["x"]!.Value<int>());
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
        var fresh = new RulerController();
        using var reopened = fixture.Open(fresh);

        Assert.Equal(saved.Start, fresh.State.Start);
        Assert.Equal(saved.End, fresh.State.End);
        Assert.Equal(saved.Bounds, fresh.State.Bounds);
        Assert.False(fresh.State.ShowRuler);
        Assert.False(fresh.State.IsBuildModeEnabled);
        Assert.False(fresh.State.IsPlayerAlive);
        Assert.Equal(RulerPickMode.Idle, fresh.State.PickMode);
        Assert.False(reopened.IsDirty);
        Assert.Empty(fixture.Errors);
        var record = Assert.IsType<JObject>(fixture.Read()["worlds"]!["world-a"]);
        Assert.Equal(new[] { "start", "end" }, record.Properties().Select(p => p.Name));
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
        fixture.Controller.TogglePicking(RulerPickMode.PickingStart);
        fixture.Controller.CancelPicking();
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
            var controller = new RulerController();
            using var persistence = fixture.Open(controller);
            Assert.Null(controller.State.Start);
            Commit(controller, true, new BlockPos(i + 10, 65, 20, 0));
        }
        Assert.Equal(ids.Length, ((JObject)fixture.Read()["worlds"]!).Count);
        for (int i = 0; i < ids.Length; i++)
        {
            fixture.WorldId = ids[i];
            var controller = new RulerController();
            using var persistence = fixture.Open(controller);
            Assert.Equal(new RulerEndpoint(i + 10, 65, 20, 0), controller.State.Start);
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
        var entry = fixture.Read()["worlds"]!["world-a"]!;
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
        Assert.Contains("vanillabuildingexpanded:ruler-persistence-no-world", fixture.Errors);
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
    [InlineData("{\"schemaVersion\":1.0,\"worlds\":{}}")]
    [InlineData("{\"schemaVersion\":2147483648,\"worlds\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"worlds\":null}")]
    [InlineData("{\"schemaVersion\":1,\"worlds\":[]}")]
    public void UnreadableOrMalformedFiles_AreReportedAndNeverReplacedWithDefaults(string json)
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.ConfigPath, json);
        using var persistence = fixture.Open();
        Assert.Contains("vanillabuildingexpanded:ruler-persistence-load-failed", fixture.Errors);
        Commit(fixture.Controller, true, new BlockPos(10, 65, 20, 0));
        Assert.True(persistence.IsDirty);
        Assert.False(persistence.TrySave());
        Assert.Equal(json, File.ReadAllText(fixture.ConfigPath));
        Assert.Empty(fixture.Writes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    public void UnsupportedSchema_IsDistinctAndProtectedUntilFileIsRepaired(int version)
    {
        using var fixture = new Fixture();
        var root = Config(("other-world", Pair(Endpoint(5, 60, 6), null)));
        root["schemaVersion"] = version;
        string original = fixture.Write(root);
        using var persistence = fixture.Open();
        Commit(fixture.Controller, true, new BlockPos(10, 65, 20, 0));

        Assert.Contains("vanillabuildingexpanded:ruler-persistence-unsupported", fixture.Errors);
        Assert.Equal(original, File.ReadAllText(fixture.ConfigPath));
        Assert.True(persistence.IsDirty);
        Assert.Empty(fixture.Writes);
        root["schemaVersion"] = 1;
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
        var root = Config(("world-a", JToken.Parse(entry)), ("other-world", new JValue("preserve")));
        string original = fixture.Write(root);
        using var persistence = fixture.Open();

        Assert.Null(fixture.Controller.State.Start);
        Assert.Null(fixture.Controller.State.End);
        Assert.Contains("vanillabuildingexpanded:ruler-persistence-invalid-entry", fixture.Errors);
        Assert.Equal(original, File.ReadAllText(fixture.ConfigPath));
        Assert.Empty(fixture.Writes);
        Assert.False(persistence.IsDirty);
        Commit(fixture.Controller, false, new BlockPos(10, 65, 20, 0));
        Assert.False(persistence.IsDirty);
        Assert.Single(fixture.Writes);
        Assert.Equal("preserve", fixture.Read()["worlds"]!["other-world"]!.Value<string>());
        Assert.Equal(JTokenType.Null, fixture.Read()["worlds"]!["world-a"]!["start"]!.Type);
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
            Assert.Contains("vanillabuildingexpanded:ruler-persistence-invalid-entry", fixture.Errors);
        }
        fixture.Write(Config(("world-a", Pair(Endpoint(10, 65, 20, 2), null))));
        var fresh = new RulerController();
        using var valid = fixture.Open(fresh);
        Assert.Equal(new RulerEndpoint(10, 65, 20, 2), fresh.State.Start);
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
        Assert.Contains("vanillabuildingexpanded:ruler-persistence-save-failed", fixture.Errors);
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
        Assert.Equal(new RulerEndpoint(10, 65, 20, 0), fixture.Controller.State.Start);
        Assert.True(persistence.IsDirty);
        Assert.Contains("vanillabuildingexpanded:ruler-persistence-save-failed", fixture.Errors);
        Assert.Empty(Directory.GetFiles(GamePaths.ModConfig, "*.tmp"));
        fixture.FailWrites = false;
        Commit(fixture.Controller, true, new BlockPos(10, 65, 20, 0));
        Assert.False(persistence.IsDirty);
        Assert.Equal(2, fixture.Writes.Count);
        Assert.Equal(4, fixture.Read()["worlds"]!["other-world"]!["start"]!["x"]!.Value<int>());
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
        Assert.Contains("vanillabuildingexpanded:ruler-persistence-load-failed", fixture.Errors);
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
        Assert.Equal(JTokenType.Null, fixture.Read()["worlds"]!["world-a"]!["end"]!.Type);
        Assert.Throws<ObjectDisposedException>(() => persistence.TrySave());
    }

    [Fact]
    public void RestoreEndpoints_DoesNotCommitOrRestoreTransientStateAndRejectsMismatchedPairs()
    {
        var controller = new RulerController();
        int commits = 0;
        controller.EndpointsCommitted += () => commits++;
        controller.RestoreEndpoints(new RulerEndpoint(10, 65, 20, 2), null);

        Assert.Equal(0, commits);
        Assert.False(controller.State.IsBuildModeEnabled);
        Assert.False(controller.State.ShowRuler);
        var before = controller.State;
        Assert.Throws<ArgumentException>(() => controller.RestoreEndpoints(
            before.Start, new RulerEndpoint(14, 67, 23, 0)));
        Assert.Equal(before, controller.State);
    }

    [Fact]
    public void ClientOwner_LoadsOnlyForLocalJoinAndDisposesBeforeWorldDeparture()
    {
        using var fixture = new Fixture();
        // The native interface's inherited range overloads cannot be proxied by Moq.
        var player = RulerRenderingTests.CreatePlayer();
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
            var ruler = Assert.IsType<RulerController>(GetField(system, "ruler"));
            var rendering = Assert.IsType<RulerRenderBinding>(GetField(system, "rulerRendering"));
            Assert.False(ruler.State.ShowRuler);
            Assert.False(ruler.State.IsBuildModeEnabled);
            int reads = fixture.ReadCount;
            Invoke(system, "Event_PlayerJoin", player);
            Assert.Same(ruler, GetField(system, "ruler"));
            Assert.Same(rendering, GetField(system, "rulerRendering"));
            Assert.Equal(reads, fixture.ReadCount);
            fixture.FailWrites = true;
            Commit(ruler, true, new BlockPos(10, 65, 20, 0));
            Assert.True(ruler.BeginEndpointEdit(RulerEndpointRole.Start));
            Assert.True(ruler.PreviewEndpoint(new(99, 80, 99, 0), fixture.Accessor.Object));
            fixture.FailWrites = false;
            Invoke(system, "Event_LeaveWorld");
            Assert.False(ruler.IsEditing);
            Assert.Equal(10, ruler.State.Start!.Value.X);
            Assert.Null(GetField(system, "ruler"));
            Assert.Null(GetField(system, "rulerPersistence"));
            Assert.Null(GetField(system, "rulerRendering"));
            Assert.Equal(10, fixture.Read()["worlds"]!["world-a"]!["start"]!["x"]!.Value<int>());
            int writes = fixture.Writes.Count;
            Commit(ruler, false, new BlockPos(14, 67, 23, 0));
            Assert.Equal(writes, fixture.Writes.Count);
            Invoke(system, "Event_PlayerJoin", player);
            var fresh = Assert.IsType<RulerController>(GetField(system, "ruler"));
            Assert.NotSame(ruler, fresh);
            Assert.NotSame(rendering, GetField(system, "rulerRendering"));
            Assert.Equal(new RulerEndpoint(10, 65, 20, 0), fresh.State.Start);
            Assert.Null(fresh.State.End);
            Assert.False(fresh.State.ShowRuler);
            Assert.False(fresh.State.IsBuildModeEnabled);
        }
        finally { Invoke(system, "Event_LeaveWorld"); }
    }

    private static void Commit(RulerController controller, bool start, BlockPos position)
    {
        controller.SetContext(true, true);
        Assert.True(controller.TogglePicking(start ? RulerPickMode.PickingStart : RulerPickMode.PickingEnd));
        Assert.True(controller.TrySelect(position, true));
    }

    private static JObject Endpoint(int x, int y, int z, int dimension = 0) =>
        new() { ["x"] = x, ["y"] = y, ["z"] = z, ["dimension"] = dimension };

    private static JObject Pair(JToken? start, JToken? end) =>
        new() { ["start"] = start ?? JValue.CreateNull(), ["end"] = end ?? JValue.CreateNull() };

    private static JObject Config(params (string Id, JToken Value)[] entries)
    {
        var worlds = new JObject();
        foreach (var (id, value) in entries) worlds[id] = value;
        return new JObject { ["schemaVersion"] = 1, ["worlds"] = worlds };
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
        private const string Locale = "vbe-ruler-persistence-tests";
        private readonly string root = Path.Combine(Path.GetTempPath(), $"vbe-ruler-tests-{Guid.NewGuid():N}");
        public readonly Mock<ICoreClientAPI> Api = new(MockBehavior.Strict);
        public readonly Mock<IClientWorldAccessor> World = new(MockBehavior.Strict);
        public readonly Mock<IBlockAccessor> Accessor = new(MockBehavior.Strict);
        public RulerController Controller { get; } = new();
        public string? WorldId { get; set; } = "world-a";
        public System.Func<BlockPos, bool> ValidPosition { get; set; } = _ => true;
        public bool FailWrites { get; set; }
        public bool PartialWrite { get; set; }
        public Exception? ReadFailure { get; set; }
        public int ReadCount { get; private set; }
        public List<string> Writes { get; } = [];
        public List<string> Errors { get; } = [];
        public List<string> LogErrors { get; } = [];
        public string ConfigPath => Path.Combine(GamePaths.ModConfig, RulerPersistence.ConfigFileName);

        public Fixture()
        {
            GamePaths.DataPath = root;
            Directory.CreateDirectory(GamePaths.ModConfig);
            var translations = new Mock<ITranslationService>();
            translations.Setup(t => t.HasTranslation(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(true);
            translations.Setup(t => t.Get(It.IsAny<string>(), It.IsAny<object[]>()))
                .Returns((string key, object[] _) => $"translated {key}");
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
                    if (code.Contains(":ruler-persistence-", StringComparison.Ordinal))
                        Assert.NotEmpty(LogErrors);
                    Errors.Add(code);
                });
            World.Setup(w => w.SavegameIdentifier).Returns(() => WorldId!);
            World.Setup(w => w.BlockAccessor).Returns(Accessor.Object);
            Accessor.Setup(a => a.IsValidPos(It.IsAny<BlockPos>())).Returns((BlockPos p) => ValidPosition(p));
            Api.Setup(a => a.LoadModConfig(RulerPersistence.ConfigFileName)).Returns(() =>
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

        public RulerPersistence Open(RulerController? controller = null) => new(Api.Object, controller ?? Controller);
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
