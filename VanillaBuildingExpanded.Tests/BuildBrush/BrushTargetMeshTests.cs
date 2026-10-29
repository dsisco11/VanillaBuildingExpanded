using System.Numerics;

using Moq;

using VanillaBuildingExpanded.BuildBrush;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

public sealed class BrushTargetMeshTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaticCubeAndJsonSlab_UseTheBorrowedWorldMeshWithoutRetessellation(bool slab)
    {
        using Fixture f = new();
        f.Block.DrawType = slab ? EnumDrawType.JSON : EnumDrawType.Cube;
        if (slab)
        {
            f.Block.Shape = new CompositeShape { Base = new AssetLocation("game:block/basic/slab/slab-down") };
            f.Borrowed = CreateCuboid(Vector3.Zero, new Vector3(1, 0.5f, 1));
        }
        BrushSnappingPresentation presentation = f.Presentation(BlockFacing.UP, 0.9, 0.1, slab ? 0.5 : 1);
        BrushTargetGeometry target = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(presentation));

        Assert.Same(presentation, target.Presentation);
        Assert.NotSame(f.Borrowed, target.Mesh.Data);
        Assert.Equal(f.Borrowed.xyz, target.Mesh.Data.xyz);
        Assert.Equal(f.Borrowed.Indices, target.Mesh.Data.Indices);
        Assert.Equal(slab ? 0.5f : 1, target.Mesh.Bounds.Max.Y);
        f.Api.Verify(x => x.TesselatorManager.GetDefaultBlockMesh(f.Block), Times.Once);
        f.Api.Verify(x => x.TesselatorManager.GetDefaultBlockMeshRef(It.IsAny<Block>()), Times.Never);
        f.Api.Verify(x => x.Tesselator.TesselateBlock(It.IsAny<Block>(), out It.Ref<MeshData>.IsAny), Times.Never);
        Assert.Equal(0, f.Uploads);
    }

    [Theory]
    [InlineData("north")]
    [InlineData("east")]
    [InlineData("south")]
    [InlineData("west")]
    [InlineData("up")]
    [InlineData("down")]
    public void NativeSlabs_WithStaticRotationBehaviors_KeepTheActualMesh(string facing)
    {
        using Fixture f = new();
        Block slab = f.UseNativeSlab(facing);
        BlockPos position = f.Position.Copy();
        var target = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(f.Presentation(u: facing == "west" ? 0.1 : 0.9)));
        Assert.Equal(f.Borrowed.xyz, target.Mesh.Data.xyz);
        Assert.Equal(f.Borrowed.Indices, target.Mesh.Data.Indices);
        Assert.NotSame(f.Borrowed, target.Mesh.Data);
        Assert.Equal(position, f.Position);
        f.Api.Verify(x => x.TesselatorManager.GetDefaultBlockMesh(slab), Times.Once);
        f.Api.Verify(x => x.Tesselator.TesselateBlock(It.IsAny<Block>(), out It.Ref<MeshData>.IsAny), Times.Never);
        f.Api.VerifyGet(x => x.BlockTextureAtlas, Times.Never);
    }

    [Fact]
    public void NativeOrdinarySlab_UsesItsStaticMesh()
    {
        using Fixture f = new();
        f.UseNativeSlab(snowAware: false);
        var target = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(f.Presentation()));
        Assert.Equal(f.Borrowed.xyz, target.Mesh.Data.xyz);
        Assert.Equal(f.Borrowed.Indices, target.Mesh.Data.Indices);
    }

    [Theory]
    [InlineData("snow")]
    [InlineData("snow-neighbor")]
    [InlineData("unknown-neighbor")]
    [InlineData("unknown-rotation")]
    [InlineData("custom-rotation")]
    [InlineData("custom-wrench")]
    public void NativeSlab_MetadataDoesNotRejectCapturedGeometry(string category)
    {
        using Fixture f = new();
        Block slab = f.UseNativeSlab();
        switch (category)
        {
            case "snow": slab.VariantStrict["cover"] = "snow"; break;
            case "snow-neighbor":
                f.Blocks.Setup(x => x.GetBlock(f.Position.AddCopy(BlockFacing.SOUTH)))
                    .Returns(new Block { BlockMaterial = EnumBlockMaterial.Snow });
                break;
            case "unknown-neighbor":
                f.Blocks.Setup(x => x.GetBlock(f.Position.AddCopy(BlockFacing.SOUTH))).Returns((Block)null!);
                break;
            case "unknown-rotation": slab.VariantStrict["rot"] = "unknown"; break;
            case "custom-rotation": slab.BlockBehaviors = [new CustomRotatable(slab)]; break;
            case "custom-wrench": slab.CollectibleBehaviors = [new CustomWrenchOrientable(slab)]; break;
            default: throw new InvalidOperationException();
        }
        Assert.NotNull(f.Cache.GetTarget(f.Presentation()));
        Assert.Equal(0, f.Uploads);
        f.Api.Verify(x => x.TesselatorManager.GetDefaultBlockMesh(slab), Times.Once);
    }

    [Theory]
    [InlineData("north")]
    [InlineData("east")]
    [InlineData("south")]
    [InlineData("west")]
    public void NativeVerticalSlab_NewCapturedSnowGeometryReplacesTheOldGeometry(string facing)
    {
        using Fixture f = new();
        f.UseNativeSlab(facing);
        BlockPos neighborPosition = f.Position.AddCopy(BlockFacing.FromCode(facing).Opposite);
        Block neighbor = new();
        f.Blocks.Setup(x => x.GetBlock(neighborPosition)).Returns(() => neighbor);
        BrushSnappingPresentation presentation = f.Presentation(u: facing == "west" ? 0.1 : 0.9);
        var first = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(presentation));
        MeshRef upload = first.Mesh.GetOrUpload();
        neighbor = new Block { BlockMaterial = EnumBlockMaterial.Snow };
        f.Borrowed = CreateCuboid(Vector3.Zero, Vector3.One);
        var snow = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(presentation));
        Assert.NotSame(first.Mesh, snow.Mesh);
        Assert.True(upload.Disposed);
        neighbor = new Block();
        var resumed = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(presentation));
        Assert.Same(snow.Mesh, resumed.Mesh);
        resumed.Mesh.GetOrUpload();
        Assert.Equal(2, f.Uploads);
        f.Inputs.Validator.Verify(x => x.CanPlace(It.IsAny<Block>(), It.IsAny<BlockSelection>(), ref It.Ref<string>.IsAny),
            Times.Once);
    }

    [Fact]
    public void FaceHitModeAndEquivalentInstanceChanges_ReuseOwnedGeometryAndUpload()
    {
        using Fixture f = new();
        BrushTargetGeometry first = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(f.Presentation()));
        MeshRef upload = first.Mesh.GetOrUpload();
        BrushTargetGeometry? last = null;
        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            foreach (double hit in new[] { 0.1, 0.8, 0.9 })
            {
                var mode = face.Axis == EnumAxis.Y ? EBuildBrushSnapping.Horizontal
                    : EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical;
                f.Position = new BlockPos(100 + face.Index, 50, 200);
                var next = f.Presentation(face, hit, 0.9, mode: mode | EBuildBrushSnapping.ApplyFaceNormalOffset);
                last = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(next));
                Assert.Same(first.Mesh, last.Mesh);
                Assert.Same(upload, last.Mesh.GetOrUpload());
                Assert.Same(next, last.Presentation);
                Assert.Equal(f.Position, last.Presentation.TargetPosition);
                Assert.NotEqual(first.Presentation.Revision, last.Presentation.Revision);
            }
        }
        Assert.NotNull(last);
        Assert.Equal(1, f.Uploads);
        f.Api.VerifyGet(x => x.BlockTextureAtlas, Times.Never);
        f.Api.Verify(x => x.Tesselator.TesselateBlock(It.IsAny<Block>(), out It.Ref<MeshData>.IsAny), Times.Never);
        f.Inputs.Validator.Verify(x => x.CanPlace(It.IsAny<Block>(), It.IsAny<BlockSelection>(), ref It.Ref<string>.IsAny),
            Times.Exactly(19));
    }

    [Theory]
    [InlineData("air")]
    [InlineData("missing")]
    [InlineData("custom")]
    [InlineData("entity-class")]
    [InlineData("block-entity")]
    [InlineData("decor")]
    [InlineData("behavior")]
    [InlineData("collectible-behavior")]
    [InlineData("random-offset")]
    [InlineData("random-rotation")]
    [InlineData("random-size")]
    [InlineData("shape-alternates")]
    [InlineData("baked-alternates")]
    [InlineData("overlay")]
    [InlineData("lod0")]
    [InlineData("lod2")]
    [InlineData("lod-suppression")]
    [InlineData("wind")]
    [InlineData("depth-offset")]
    [InlineData("liquid-pass")]
    [InlineData("custom-draw")]
    [InlineData("format")]
    [InlineData("voxelized")]
    [InlineData("stale-block")]
    public void NativeGeometryIsNotRejectedByBlockMetadata(string category)
    {
        using Fixture f = new();
        BrushSnappingPresentation original = f.Presentation();
        switch (category)
        {
            case "air": f.WorldBlock = new Block(); break;
            case "missing": f.Block.IsMissing = true; break;
            case "custom": f.WorldBlock = new CustomBlock { BlockId = 10 }; break;
            case "entity-class": f.Block.EntityClass = "Custom"; break;
            case "block-entity": f.Blocks.Setup(x => x.GetBlockEntity(It.IsAny<BlockPos>())).Returns(Mock.Of<BlockEntity>()); break;
            case "decor": f.Blocks.Setup(x => x.GetSubDecors(It.IsAny<BlockPos>()))
                    .Returns(new Dictionary<int, Block> { [17] = new Block { BlockId = 20 } }); break;
            case "behavior": f.Block.BlockBehaviors = [new Mock<BlockBehavior>(f.Block).Object]; break;
            case "collectible-behavior": f.Block.CollectibleBehaviors = [new Mock<CollectibleBehavior>(f.Block).Object]; break;
            case "random-offset": f.Block.RandomDrawOffset = 1; break;
            case "random-rotation": f.Block.RandomizeRotations = true; break;
            case "random-size": f.Block.RandomSizeAdjust = 0.1f; break;
            case "shape-alternates": f.Block.Shape.Alternates = [new CompositeShape()]; break;
            case "baked-alternates": f.Block.Shape.BakedAlternates = [new CompositeShape()]; break;
            case "overlay": f.Block.Shape.Overlays = [new CompositeShape()]; break;
            case "lod0": f.Block.Lod0Shape = new CompositeShape(); break;
            case "lod2": f.Block.Lod2Shape = new CompositeShape(); break;
            case "lod-suppression": f.Block.DoNotRenderAtLod2 = true; break;
            case "wind": f.Block.VertexFlags.WindMode = EnumWindBitMode.NormalWind; break;
            case "depth-offset": f.Block.VertexFlags.ZOffset = 1; break;
            case "liquid-pass": f.Block.RenderPass = EnumChunkRenderPass.Liquid; break;
            case "custom-draw": f.Block.DrawType = EnumDrawType.Cross; break;
            case "format": f.Block.Shape.Format = EnumShapeFormat.GltfEmbedded; break;
            case "voxelized": f.Block.Shape.VoxelizeTexture = true; break;
            case "stale-block": f.WorldBlock = new Block { BlockId = 11 }; break;
            default: throw new InvalidOperationException();
        }

        if (category is "air" or "stale-block")
        {
            Assert.Null(f.Cache.GetTarget(original));
            f.Api.Verify(x => x.TesselatorManager.GetDefaultBlockMesh(It.IsAny<Block>()), Times.Never);
        }
        else
        {
            var target = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(original));
            Assert.Equal(f.Borrowed.xyz, target.Mesh.Data.xyz);
            Assert.Equal(f.Borrowed.Indices, target.Mesh.Data.Indices);
        }
        Assert.Equal(0, f.Uploads);
    }

    [Theory]
    [InlineData("alpha")]
    [InlineData("partial-alpha")]
    [InlineData("composite-alpha")]
    [InlineData("alternate-texture")]
    [InlineData("tiled-texture")]
    [InlineData("alternate-flag")]
    [InlineData("tiled-flag")]
    [InlineData("unbaked")]
    [InlineData("missing-textures")]
    public void TextureAlphaAndTextureAvailability_DoNotRestrictMeshIndicators(string category)
    {
        using Fixture f = new();
        switch (category)
        {
            case "alpha": f.Bitmap.TexturePixels[0] = 0x00ffffff; break;
            case "partial-alpha": f.Bitmap.TexturePixels[0] = 0x7fffffff; break;
            case "composite-alpha": f.Texture.Alpha = 128; break;
            case "alternate-texture": f.Texture.Baked.BakedVariants = [new BakedCompositeTexture()]; break;
            case "tiled-texture": f.Texture.Baked.BakedTiles = [new BakedCompositeTexture()]; break;
            case "alternate-flag": f.Block.HasAlternates = true; break;
            case "tiled-flag": f.Block.HasTiles = true; break;
            case "unbaked": f.Texture.Baked = null; break;
            case "missing-textures": f.Block.Textures = null; break;
            default: throw new InvalidOperationException();
        }
        var target = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(f.Presentation()));
        var reused = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(f.Presentation(BlockFacing.UP, 0.1, 0.9)));
        Assert.Same(target.Mesh, reused.Mesh);
        Assert.Equal(f.Borrowed.xyz, target.Mesh.Data.xyz);
        Assert.Equal(f.Borrowed.Indices, target.Mesh.Data.Indices);
        Assert.Equal(0, f.Uploads);
        f.Api.VerifyGet(x => x.BlockTextureAtlas, Times.Never);
    }

    [Theory]
    [InlineData("narrow")]
    [InlineData("recessed")]
    public void NonCubicGeometry_ReachesShaderClippingWithoutASecondDirectionalGate(string category)
    {
        using Fixture f = new();
        double plane = 1;
        if (category is "narrow" or "recessed")
        {
            f.Borrowed = CreateCuboid(new Vector3(0.45f, 0, 0.45f), new Vector3(0.55f, 1, 0.55f));
            plane = category == "recessed" ? 0.5 : 1;
        }
        else if (category == "cube-draw-slab-mesh")
        {
            f.Block.DrawType = EnumDrawType.Cube;
            f.Borrowed = CreateCuboid(Vector3.Zero, new Vector3(1, 0.5f, 1));
            plane = 0.5;
        }

        var target = Assert.IsType<BrushTargetGeometry>(
            f.Cache.GetTarget(f.Presentation(BlockFacing.UP, 0.9, 0.1, plane)));
        Assert.Equal(f.Borrowed.xyz, target.Mesh.Data.xyz);
        Assert.Equal(f.Borrowed.Indices, target.Mesh.Data.Indices);
        Assert.Equal(0, f.Uploads);
    }

    [Theory]
    [InlineData("nan")]
    [InlineData("indices")]
    [InlineData("count")]
    [InlineData("degenerate")]
    public void UnsupportedMeshData_IsOmittedRatherThanUploaded(string change)
    {
        using Fixture f = new();
        switch (change)
        {
            case "nan": f.Borrowed.xyz[0] = float.NaN; break;
            case "indices": f.Borrowed.Indices[0] = 999; break;
            case "wind": f.Borrowed.Flags[0] |= EnumWindBitModeMask.WeakWind; break;
            case "offset": f.Borrowed.Flags[0] |= 1 << VertexFlags.ZOffsetBitPos; break;
            case "pass": f.Borrowed.RenderPassesAndExtraBits = [(short)EnumChunkRenderPass.Liquid]; break;
            case "pass-extra-bits": f.Borrowed.RenderPassesAndExtraBits = [1024]; break;
            case "count": f.Borrowed.VerticesCount = 0; break;
            case "degenerate": Array.Fill(f.Borrowed.Indices, 0); break;
            default: throw new InvalidOperationException();
        }
        Assert.Null(f.Cache.GetTarget(f.Presentation()));
        Assert.Equal(0, f.Uploads);
    }

    [Fact]
    public void InheritedOpaqueFacePass_IsSupported_AndCompositeTransformIsNotAppliedTwice()
    {
        using Fixture f = new();
        f.Borrowed = CreateCuboid(new Vector3(0.1f, 0.1f, 0.1f), new Vector3(0.9f, 0.5f, 0.9f));
        f.Borrowed.RenderPassesAndExtraBits = [-1, -1, -1, -1, -1, -1];
        f.Borrowed.RenderPassCount = 6;
        f.Block.Shape.rotateY = 90;
        f.Block.Shape.offsetY = 0.1f;
        f.Block.Shape.Scale = 0.8f;
        var target = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(f.Presentation(BlockFacing.UP, 0.8, 0.2, 0.5)));
        Assert.Equal(f.Borrowed.xyz, target.Mesh.Data.xyz);
        Matrixf model = new();
        target.SetModelMatrix(model, new Matrixf().Values, new Vec3d(3, 4, 5));
        Assert.Equal(new Matrixf().Values, model.Values);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TargetTransform_UsesOriginalCellDimensionAndDoublePrecisionCameraRelativeTranslation(int dimension)
    {
        using Fixture f = new();
        f.Position = new BlockPos(1000000000, 123, -1000000000, dimension);
        BrushTargetGeometry target = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(f.Presentation()));
        Vec3d camera = new(f.Position.X + 0.125,
            f.Position.Y + (double)dimension * BlockPos.DimensionBoundary + 0.25, f.Position.Z + 0.5);
        Matrixf matrix = new();
        target.SetModelMatrix(matrix, new Matrixf().Values, camera);
        Assert.Equal(-0.125f, matrix.Values[12]);
        Assert.Equal(-0.25f, matrix.Values[13]);
        Assert.Equal(-0.5f, matrix.Values[14]);
        Assert.Equal(1, matrix.Values[0]);
        Assert.Equal(1, matrix.Values[5]);
        Assert.Equal(1, matrix.Values[10]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void SlabFaces_KeepIndependentCellBasedDirections(int faceIndex)
    {
        using Fixture f = new();
        f.Borrowed = CreateCuboid(Vector3.Zero, new Vector3(1, 0.5f, 1));
        BlockFacing face = BlockFacing.ALLFACES[faceIndex];
        var target = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(
            f.Presentation(face, 0.9, 0.1, face == BlockFacing.UP ? 0.5 : null)));
        Assert.Equal(1, target.Presentation.Result.AppliedSnapping.FaceU);
        Assert.Equal(-1, target.Presentation.Result.AppliedSnapping.FaceV);
        Assert.Equal(0.5f, target.Mesh.Bounds.Max.Y);
    }

    [Theory]
    [InlineData(EnumChunkRenderPass.Opaque, false)]
    [InlineData(EnumChunkRenderPass.OpaqueNoCull, false)]
    [InlineData(EnumChunkRenderPass.BlendNoCull, false)]
    [InlineData(EnumChunkRenderPass.Transparent, false)]
    [InlineData(EnumChunkRenderPass.Opaque, true)]
    [InlineData(EnumChunkRenderPass.OpaqueNoCull, true)]
    [InlineData(EnumChunkRenderPass.BlendNoCull, true)]
    [InlineData(EnumChunkRenderPass.Transparent, true)]
    public void StaticMaterialPasses_KeepTheFullMeshWithoutAlphaSampling(EnumChunkRenderPass pass, bool inherit)
    {
        using Fixture f = new();
        f.Block.RenderPass = pass;
        f.Texture.Alpha = 0;
        f.Bitmap.TexturePixels[0] = 0;
        f.Borrowed.RenderPassesAndExtraBits = Enumerable.Repeat(inherit ? (short)-1 : (short)pass, 6).ToArray();
        f.Borrowed.RenderPassCount = 6;
        var target = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(f.Presentation()));
        Assert.Equal(f.Borrowed.xyz, target.Mesh.Data.xyz);
        Assert.Equal(f.Borrowed.Indices, target.Mesh.Data.Indices);
        target.Mesh.GetOrUpload();
        f.Render.Verify(x => x.UploadMesh(target.Mesh.Data), Times.Once);
        f.Api.VerifyGet(x => x.BlockTextureAtlas, Times.Never);
    }

    [Fact]
    public void CameraRotation_IsComposedWithOriginalTargetTranslation()
    {
        using Fixture f = new();
        var target = Assert.IsType<BrushTargetGeometry>(f.Cache.GetTarget(f.Presentation()));
        Matrixf camera = new Matrixf().RotateY(0.7f);
        Matrixf expected = new Matrixf().Set(camera.Values).Translate(3, 4, 5);
        Matrixf actual = new();
        target.SetModelMatrix(actual, camera.Values, new Vec3d());
        Assert.Equal(expected.Values, actual.Values);
        Assert.NotEqual(f.Position, target.Presentation.Result.Position);
    }

    internal static MeshData CreateCuboid(Vector3 min, Vector3 max)
    {
        MeshData mesh = CubeMeshUtil.GetCube();
        mesh.Indices = (int[])mesh.Indices.Clone();
        for (int vertex = 0; vertex < mesh.VerticesCount; vertex++)
        {
            for (int axis = 0; axis < 3; axis++)
                mesh.xyz[vertex * 3 + axis] = mesh.xyz[vertex * 3 + axis] < 0 ? min[axis] : max[axis];
        }
        return mesh;
    }

    private sealed class CustomBlock : Block { }
    private sealed class CustomRotatable(Block block) : BlockBehaviorOmniRotatable(block) { }
    private sealed class CustomWrenchOrientable(Block block) : BlockBehaviorWrenchOrientable(block) { }

    internal sealed class Fixture : IDisposable
    {
        public Mock<ICoreClientAPI> Api { get; } = TestHelpers.CreateMockCapi();
        public Mock<IBlockAccessor> Blocks { get; } = new();
        public Mock<IBlockTextureAtlasAPI> Atlas { get; } = new();
        public Mock<IRenderAPI> Render { get; } = new();
        public BrushTestInputs Inputs { get; } = new();
        public Block Block { get; } = new()
        {
            BlockId = 10, Code = new AssetLocation("game:static-test"), VertexFlags = new VertexFlags(),
            Shape = new CompositeShape { Base = new AssetLocation("game:block/basic/cube") }
        };
        public CompositeTexture Texture { get; } = new()
        {
            Base = new AssetLocation("game:block/test"),
            Baked = new BakedCompositeTexture { BakedName = new AssetLocation("game:block/test") }
        };
        public BakedBitmap Bitmap { get; } = new() { Width = 1, Height = 1, TexturePixels = [unchecked((int)0xffffffff)] };
        public Block WorldBlock { get; set; }
        public MeshData Borrowed { get; set; } = CreateCuboid(Vector3.Zero, Vector3.One);
        public BlockPos Position { get; set; } = new(3, 4, 5);
        public BrushTargetMeshCache Cache { get; }
        public int Uploads { get; private set; }
        public List<TestMeshRef> Uploaded { get; } = [];
        private long revision;

        public Fixture()
        {
            WorldBlock = Block;
            Block.Textures = new Dictionary<string, CompositeTexture> { ["all"] = Texture };
            Mock<IClientWorldAccessor> world = new();
            world.Setup(x => x.BlockAccessor).Returns(Blocks.Object);
            Api.Setup(x => x.World).Returns(world.Object);
            Api.Setup(x => x.BlockTextureAtlas).Returns(Atlas.Object);
            Api.Setup(x => x.Render).Returns(Render.Object);
            Api.Setup(x => x.TesselatorManager.GetDefaultBlockMesh(It.IsAny<Block>())).Returns(() => Borrowed);
            Blocks.Setup(x => x.GetBlock(It.IsAny<BlockPos>())).Returns(() => WorldBlock);
            Atlas.Setup(x => x.LoadCompositeBitmap(It.IsAny<AssetLocationAndSource>())).Returns(Bitmap);
            Render.Setup(x => x.UploadMesh(It.IsAny<MeshData>())).Returns(() =>
            {
                Uploads++;
                TestMeshRef mesh = new();
                Uploaded.Add(mesh);
                return mesh;
            });
            Cache = new BrushTargetMeshCache(Api.Object, GetCapturedMesh);
        }

        public MeshData? GetCapturedMesh(Block block, BlockPos position) =>
            Api.Object.TesselatorManager.GetDefaultBlockMesh(block);

        public BrushSnappingPresentation Presentation(BlockFacing? face = null, double u = 0.9, double v = 0.5,
            double? plane = null, EBuildBrushSnapping mode = EBuildBrushSnapping.Horizontal | EBuildBrushSnapping.Vertical)
        {
            face ??= BlockFacing.NORTH;
            double surface = plane ?? (face.Negative ? 0 : 1);
            BlockSelection selection = new()
            {
                Position = Position.Copy(), Block = Block, Face = face,
                HitPosition = face.Axis switch
                {
                    EnumAxis.X => new Vec3d(surface, v, u),
                    EnumAxis.Y => new Vec3d(u, surface, v),
                    _ => new Vec3d(u, v, surface)
                }
            };
            BrushPlacementResult result = new BrushPlacementValidation(Inputs.Validator.Object)
                .Resolve(Block, selection, mode, new BrushSnapping(selection));
            return new BrushSnappingPresentation(selection, result, ++revision);
        }

        public Block UseNativeSlab(string facing = "north", bool snowAware = true)
        {
            Block slab = snowAware ? new BlockSlabSnowRemove() : new BlockSlab();
            slab.BlockId = Block.Id;
            slab.Code = new AssetLocation("game:native-test-slab");
            slab.DrawType = EnumDrawType.JSON;
            slab.Shape = new CompositeShape { Base = new AssetLocation($"game:block/basic/slab/slab-{facing}") };
            slab.VariantStrict["cover"] = "free";
            slab.VariantStrict["rot"] = facing;
            slab.BlockBehaviors = [new BlockBehaviorOmniRotatable(slab), new BlockBehaviorWrenchOrientable(slab)];
            slab.CollectibleBehaviors = slab.BlockBehaviors;
            Vector3 min = Vector3.Zero, max = Vector3.One;
            BlockFacing rotation = BlockFacing.FromCode(facing);
            if (rotation.Negative) max[(int)rotation.Axis] = 0.5f;
            else min[(int)rotation.Axis] = 0.5f;
            Borrowed = CreateCuboid(min, max);
            WorldBlock = slab;
            return slab;
        }

        public void Dispose() => Cache.Dispose();
    }

    internal sealed class TestMeshRef : MeshRef
    {
        public bool Ready { get; set; } = true;
        public override bool Initialized => Ready;
        public int DisposeCount { get; private set; }
        public override void Dispose()
        {
            DisposeCount++;
            base.Dispose();
        }
    }
}
