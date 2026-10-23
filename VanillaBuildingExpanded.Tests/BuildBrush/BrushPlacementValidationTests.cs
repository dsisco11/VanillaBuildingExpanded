using VanillaBuildingExpanded.BuildBrush;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

/// <summary>Checks brush placement decisions without constructing players or running native placement rules.</summary>
public sealed class BrushPlacementValidationTests
{
    #region Public API

    #region Snapping decisions

    /// <summary>An accepted snapped candidate ends the search and forwards the actual block and selection data.</summary>
    [Fact]
    public void ResolvePosition_AcceptedSnap_DoesNotCheckFallback()
    {
        Block block = new();
        BlockSelection source = CreateSelection();
        BrushSnapping snapping = new(source);
        RecordingValidator validator = new(true);
        BrushPlacementValidation placement = new(validator);

        BlockPos position = placement.ResolvePosition(block, source, EBuildBrushSnapping.Horizontal, snapping, out bool valid);

        Assert.True(valid);
        var call = Assert.Single(validator.Calls);
        Assert.Same(block, call.Block);
        Assert.Equal(snapping.ResolvePosition(EBuildBrushSnapping.Horizontal), position);
        AssertResolvedSelection(source, position, call.Selection);
        Assert.Equal(string.Empty, call.FailureCode);
    }

    /// <summary>Rejected snapping checks the unsnapped candidate next and uses that candidate's result.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolvePosition_RejectedSnap_UsesFallbackResult(bool fallbackAccepted)
    {
        Block block = new();
        BlockSelection source = CreateSelection();
        BrushSnapping snapping = new(source);
        RecordingValidator validator = new(false, fallbackAccepted);
        BrushPlacementValidation placement = new(validator);

        BlockPos position = placement.ResolvePosition(block, source, EBuildBrushSnapping.Horizontal, snapping, out bool valid);

        Assert.Equal(fallbackAccepted, valid);
        Assert.Equal(2, validator.Calls.Count);
        Assert.Equal(snapping.ResolvePosition(EBuildBrushSnapping.Horizontal), validator.Calls[0].Selection.Position);
        Assert.Equal(snapping.ResolvePosition(EBuildBrushSnapping.None), position);
        foreach (var call in validator.Calls)
        {
            Assert.Same(block, call.Block);
            AssertResolvedSelection(source, call.Selection.Position, call.Selection);
            Assert.Equal(string.Empty, call.FailureCode);
        }
        Assert.NotSame(validator.Calls[0].Selection, validator.Calls[1].Selection);
    }

    /// <summary>Native rule side effects still occur twice when rejected candidates happen to coincide.</summary>
    [Fact]
    public void ResolvePosition_CoincidentCandidates_AreNotDeduplicated()
    {
        BlockSelection source = CreateSelection();
        RecordingValidator validator = new(false, true);
        BrushPlacementValidation placement = new(validator);

        placement.ResolvePosition(new Block(), source, EBuildBrushSnapping.None, new BrushSnapping(source), out bool valid);

        Assert.True(valid);
        Assert.Equal(2, validator.Calls.Count);
        Assert.Equal(validator.Calls[0].Selection.Position, validator.Calls[1].Selection.Position);
    }

    /// <summary>With no block, resolving returns the original position without invoking rules.</summary>
    [Fact]
    public void ResolvePosition_NoBlock_DoesNotValidate()
    {
        BlockSelection source = CreateSelection();
        RecordingValidator validator = new();
        BrushPlacementValidation placement = new(validator);

        BlockPos position = placement.ResolvePosition(null, source, EBuildBrushSnapping.Horizontal, new BrushSnapping(source), out bool valid);

        Assert.True(valid);
        Assert.Same(source.Position, position);
        Assert.Empty(validator.Calls);
    }

    /// <summary>The individual null-block check returns an independent copy of the source position.</summary>
    [Fact]
    public void TryGetValidSnappedPosition_NoBlock_CopiesPositionWithoutValidation()
    {
        BlockSelection source = CreateSelection();
        RecordingValidator validator = new();
        BrushPlacementValidation placement = new(validator);

        bool valid = placement.TryGetValidSnappedPosition(null, source, new BrushSnapping(source), EBuildBrushSnapping.Horizontal, out BlockPos position);

        Assert.True(valid);
        Assert.Equal(source.Position, position);
        Assert.NotSame(source.Position, position);
        Assert.Empty(validator.Calls);
    }

    #endregion

    #region Preview and selection validation

    /// <summary>A preview rejection uses the current placement block at the resolved preview position.</summary>
    [Fact]
    public void CanPlaceAtPosition_RejectedPreview_ForwardsBlockAndResolvedSelection()
    {
        Block placementBlock = new();
        BlockSelection source = CreateSelection();
        BlockPos previewPosition = new(20, 30, 40);
        RecordingValidator validator = new(false);
        BrushPlacementValidation placement = new(validator);

        Assert.False(placement.CanPlaceAtPosition(placementBlock, source, previewPosition));

        var call = Assert.Single(validator.Calls);
        Assert.Same(placementBlock, call.Block);
        AssertResolvedSelection(source, previewPosition, call.Selection);
        Assert.Equal(new BlockPos(3, 4, 5), source.Position);
        Assert.False(source.DidOffset);
    }

    /// <summary>Validation mutations affect the cloned selection, preserving the original raycast data.</summary>
    [Fact]
    public void CanPlaceAtPosition_RuleMutations_DoNotChangeSourceSelection()
    {
        BlockSelection source = CreateSelection();
        RecordingValidator validator = new(true)
        {
            OnValidate = selection =>
            {
                selection.Position.Set(90, 91, 92);
                selection.HitPosition.Set(0, 0, 0);
                selection.Face = BlockFacing.DOWN;
            }
        };

        Assert.True(new BrushPlacementValidation(validator).CanPlaceAtPosition(new Block(), source, new BlockPos(6, 7, 8)));

        Assert.Equal(new BlockPos(3, 4, 5), source.Position);
        Assert.Equal(new Vec3d(0.9, 0.8, 0), source.HitPosition);
        Assert.Same(BlockFacing.NORTH, source.Face);
    }

    /// <summary>A request with no resolved position preserves the original offset marker on its clone.</summary>
    [Fact]
    public void BuildResolvedSelection_NoPosition_PreservesSourceOffset()
    {
        BlockSelection source = CreateSelection();

        BlockSelection clone = BrushPlacementValidation.BuildResolvedSelection(source, null);

        Assert.NotSame(source, clone);
        Assert.Equal(source.Position, clone.Position);
        Assert.False(clone.DidOffset);
    }

    #endregion

    #region Dependency contracts

    /// <summary>The placement component requires an explicit rule boundary.</summary>
    [Fact]
    public void Constructor_NoValidator_RejectsMissingDependency()
    {
        Assert.Throws<ArgumentNullException>(() => new BrushPlacementValidation(null!));
    }

    /// <summary>The native adapter cannot silently bypass claims by retaining a null player.</summary>
    [Fact]
    public void NativeAdapter_NoPlayer_RejectsMissingOwner()
    {
        Assert.Throws<ArgumentNullException>(() => new PlayerBrushPlacementValidator(TestHelpers.CreateMockWorld().Object, null!));
    }

    #endregion

    #endregion

    #region Private

    /// <summary>Creates a raycast selection with non-default face, hit and selection-box metadata.</summary>
    private static BlockSelection CreateSelection()
    {
        return new BlockSelection
        {
            Position = new BlockPos(3, 4, 5),
            Face = BlockFacing.NORTH,
            HitPosition = new Vec3d(0.9, 0.8, 0),
            SelectionBoxIndex = 2,
            SelectionBoxId = "test-box",
            DidOffset = false
        };
    }

    /// <summary>Checks that validation changes position/offset while preserving the original raycast metadata.</summary>
    private static void AssertResolvedSelection(BlockSelection source, BlockPos position, BlockSelection resolved)
    {
        Assert.NotSame(source, resolved);
        Assert.NotSame(source.Position, resolved.Position);
        Assert.Equal(position, resolved.Position);
        Assert.True(resolved.DidOffset);
        Assert.Same(source.Face, resolved.Face);
        Assert.Equal(source.HitPosition, resolved.HitPosition);
        Assert.NotSame(source.HitPosition, resolved.HitPosition);
        Assert.Equal(source.SelectionBoxIndex, resolved.SelectionBoxIndex);
        Assert.Equal(source.SelectionBoxId, resolved.SelectionBoxId);
    }

    /// <summary>Records validation inputs and returns controlled results without using any game player.</summary>
    private sealed class RecordingValidator : IBrushPlacementValidator
    {
        private readonly Queue<bool> outcomes;

        #region Public API

        public List<(Block Block, BlockSelection Selection, string FailureCode)> Calls { get; } = [];
        public Action<BlockSelection>? OnValidate { get; init; }

        /// <summary>Configures the ordered results expected from candidate checks.</summary>
        public RecordingValidator(params bool[] outcomes)
        {
            this.outcomes = new Queue<bool>(outcomes);
        }

        /// <summary>Records inputs before applying an optional mutation and a failure-code result.</summary>
        public bool CanPlace(Block block, BlockSelection selection, ref string failureCode)
        {
            Calls.Add((block, selection, failureCode));
            OnValidate?.Invoke(selection);
            bool accepted = outcomes.Dequeue();
            failureCode = accepted ? string.Empty : "test-rejected";
            return accepted;
        }

        #endregion
    }

    #endregion
}
