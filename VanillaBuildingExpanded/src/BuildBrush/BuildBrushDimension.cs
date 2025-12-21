using System.Collections.Generic;

using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VanillaBuildingExpanded.BuildHammer;

/// <summary>
/// Wrapper class that manages a mini-dimension for the build brush system.
/// Handles block placement and rotation within the preview dimension.
/// </summary>
public class BuildBrushDimension
{
    #region Fields
    private readonly IWorldAccessor world;
    private readonly ICoreServerAPI? sapi;
    private IMiniDimension? dimension;
    private int dimensionId = -1;

    /// <summary>
    /// The current block placed in the dimension (may be rotated variant).
    /// </summary>
    private Block? currentBlock;

    /// <summary>
    /// The original unrotated block.
    /// </summary>
    private Block? originalBlock;

    /// <summary>
    /// Block entity tree attributes for IRotatable blocks.
    /// </summary>
    private ITreeAttribute? blockEntityTree;

    /// <summary>
    /// The position within the mini-dimension where the block is placed.
    /// </summary>
    private BlockPos? internalBlockPos;
    #endregion

    #region Properties
    /// <summary>
    /// The underlying mini-dimension.
    /// </summary>
    public IMiniDimension? Dimension => dimension;

    /// <summary>
    /// The sub-dimension ID assigned to this brush dimension.
    /// </summary>
    public int DimensionId => dimensionId;

    /// <summary>
    /// Whether the dimension has been initialized.
    /// </summary>
    public bool IsInitialized => dimension is not null && dimensionId >= 0;

    /// <summary>
    /// The current rotation angle in degrees (0, 90, 180, 270).
    /// </summary>
    public int RotationAngle { get; private set; } = 0;

    /// <summary>
    /// The detected rotation mode for the current block.
    /// </summary>
    public EBuildBrushRotationMode RotationMode { get; private set; } = EBuildBrushRotationMode.None;

    /// <summary>
    /// The current block in the dimension (may be a rotated variant).
    /// </summary>
    public Block? CurrentBlock => currentBlock;

    /// <summary>
    /// The original unrotated block.
    /// </summary>
    public Block? OriginalBlock => originalBlock;
    #endregion

    #region Constructor
    public BuildBrushDimension(IWorldAccessor world)
    {
        this.world = world;
        this.sapi = world.Api as ICoreServerAPI;
    }
    #endregion

    #region Initialization
    /// <summary>
    /// Initializes the mini-dimension. Must be called on server side.
    /// </summary>
    /// <param name="existingDimensionId">Optional existing dimension ID to reuse.</param>
    /// <returns>True if initialization succeeded.</returns>
    public bool Initialize(int existingDimensionId = -1)
    {
        if (sapi is null)
            return false;

        // Create the mini-dimension
        dimension = world.BlockAccessor.CreateMiniDimension(new Vec3d());

        if (existingDimensionId >= 0)
        {
            // Reuse existing dimension ID
            dimensionId = existingDimensionId;
            sapi.Server.SetMiniDimension(dimension, dimensionId);
        }
        else
        {
            // Allocate new dimension ID
            dimensionId = sapi.Server.LoadMiniDimension(dimension);
        }

        dimension.SetSubDimensionId(dimensionId);
        dimension.BlocksPreviewSubDimension_Server = dimensionId;

        return true;
    }

    /// <summary>
    /// Clears the dimension contents without destroying it.
    /// </summary>
    public void Clear()
    {
        dimension?.ClearChunks();
        currentBlock = null;
        originalBlock = null;
        blockEntityTree = null;
        internalBlockPos = null;
        RotationAngle = 0;
        RotationMode = EBuildBrushRotationMode.None;
    }

    /// <summary>
    /// Destroys the dimension and releases resources.
    /// </summary>
    public void Destroy()
    {
        if (dimension is not null)
        {
            dimension.ClearChunks();
            dimension.UnloadUnusedServerChunks();
        }
        dimension = null;
        dimensionId = -1;
        Clear();
    }
    #endregion

    #region Block Management
    /// <summary>
    /// Sets the block to be displayed in the dimension.
    /// </summary>
    /// <param name="block">The block to display.</param>
    /// <param name="rotationMode">The rotation mode for this block (optional, auto-detects if not provided).</param>
    public void SetBlock(Block block, EBuildBrushRotationMode? rotationMode = null)
    {
        if (dimension is null || !IsInitialized)
            return;

        // Clear existing block
        Clear();

        originalBlock = block;
        currentBlock = block;
        RotationMode = rotationMode ?? EBuildBrushRotationMode.None;

        // Initialize block entity tree if needed
        if (RotationMode is EBuildBrushRotationMode.Rotatable or EBuildBrushRotationMode.Hybrid)
        {
            InitializeBlockEntityTree(block);
        }

        // Place the block in the dimension
        PlaceBlockInDimension();
    }

    /// <summary>
    /// Updates the block variant (for variant-based rotation).
    /// </summary>
    /// <param name="variantBlock">The rotated variant block.</param>
    public void SetVariantBlock(Block variantBlock)
    {
        if (dimension is null || !IsInitialized || originalBlock is null)
            return;

        currentBlock = variantBlock;
        PlaceBlockInDimension();
    }

    /// <summary>
    /// Initializes the block entity tree attributes for IRotatable blocks.
    /// </summary>
    private void InitializeBlockEntityTree(Block block)
    {
        if (string.IsNullOrEmpty(block.EntityClass))
            return;

        try
        {
            // Create a temporary block entity to get default tree attributes
            BlockEntity be = world.ClassRegistry.CreateBlockEntity(block.EntityClass);
            if (be is null)
                return;

            blockEntityTree = new TreeAttribute();

            // Initialize with block code
            blockEntityTree.SetString("blockCode", block.Code.ToShortString());
        }
        catch
        {
            blockEntityTree = null;
        }
    }

    /// <summary>
    /// Places the current block in the mini-dimension at the origin.
    /// </summary>
    private void PlaceBlockInDimension()
    {
        if (dimension is null || currentBlock is null)
            return;

        // Calculate position in mini-dimension space
        // Block is placed at origin (0, 0, 0) within the dimension
        internalBlockPos = new BlockPos(0, 0, 0, Dimensions.MiniDimensions);
        dimension.AdjustPosForSubDimension(internalBlockPos);

        // Set the block
        dimension.SetBlock(currentBlock.BlockId, internalBlockPos);

        // If block has entity data, apply it
        if (blockEntityTree is not null && !string.IsNullOrEmpty(currentBlock.EntityClass))
        {
            // Spawn block entity and apply tree
            world.BlockAccessor.SpawnBlockEntity(currentBlock.EntityClass, internalBlockPos);
            var be = dimension.GetBlockEntity(internalBlockPos);
            be?.FromTreeAttributes(blockEntityTree, world);
        }

        dimension.Dirty = true;
    }
    #endregion

    #region Rotation
    /// <summary>
    /// Applies rotation to the block in the dimension.
    /// </summary>
    /// <param name="angle">The rotation angle in degrees (0, 90, 180, 270).</param>
    /// <param name="variantBlock">For variant-based rotation, the rotated variant. For IRotatable, pass null.</param>
    public void ApplyRotation(int angle, Block? variantBlock = null)
    {
        if (dimension is null || !IsInitialized || originalBlock is null)
            return;

        // Normalize angle
        RotationAngle = ((angle % 360) + 360) % 360;

        switch (RotationMode)
        {
            case EBuildBrushRotationMode.None:
                // No rotation possible
                break;

            case EBuildBrushRotationMode.VariantBased:
                // Use the provided variant block
                if (variantBlock is not null)
                {
                    currentBlock = variantBlock;
                    PlaceBlockInDimension();
                }
                break;

            case EBuildBrushRotationMode.Rotatable:
                // Apply rotation via IRotatable
                ApplyRotatableRotation(RotationAngle);
                break;

            case EBuildBrushRotationMode.Hybrid:
                // Apply both variant and IRotatable
                if (variantBlock is not null)
                {
                    currentBlock = variantBlock;
                }
                ApplyRotatableRotation(RotationAngle);
                break;
        }
    }

    /// <summary>
    /// Applies rotation to IRotatable block entities.
    /// </summary>
    private void ApplyRotatableRotation(int angle)
    {
        if (blockEntityTree is null || currentBlock is null || internalBlockPos is null)
            return;

        if (string.IsNullOrEmpty(currentBlock.EntityClass))
            return;

        try
        {
            // Create block entity and apply rotation
            BlockEntity be = world.ClassRegistry.CreateBlockEntity(currentBlock.EntityClass);
            if (be is null)
                return;

            be.Pos = internalBlockPos;
            be.CreateBehaviors(currentBlock, world);

            // Check both entity and behaviors for IRotatable
            IRotatable? rotatable = be as IRotatable;
            if (rotatable is null)
            {
                foreach (var behavior in be.Behaviors)
                {
                    if (behavior is IRotatable r)
                    {
                        rotatable = r;
                        break;
                    }
                }
            }

            if (rotatable is not null)
            {
                // Apply rotation to tree attributes
                rotatable.OnTransformed(
                    world,
                    blockEntityTree,
                    angle,
                    new Dictionary<int, AssetLocation>(), // oldBlockIdMapping
                    new Dictionary<int, AssetLocation>(), // oldItemIdMapping
                    null // flipAxis
                );
            }

            // Re-place block with updated tree
            PlaceBlockInDimension();
        }
        catch
        {
            // Rotation failed, keep current state
        }
    }
    #endregion

    #region Sync
    /// <summary>
    /// Sends dirty chunks to nearby players.
    /// </summary>
    /// <param name="players">The players to sync to.</param>
    public void SyncToPlayers(IPlayer[] players)
    {
        dimension?.CollectChunksForSending(players);
    }
    #endregion
}
