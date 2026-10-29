using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class BrushTargetMeshCache : IDisposable
{
    private readonly BrushTargetMeshSource source;
    private Block? cachedBlock;
    private MeshData? borrowedMesh;
    private BrushTargetMesh? ownedMesh;
    private bool disposed;

    public BrushTargetMeshCache(ICoreClientAPI api, System.Func<Block, BlockPos, MeshData?>? getMesh = null) =>
        source = new BrushTargetMeshSource(api, getMesh);

    public BrushTargetGeometry? GetTarget(BrushSnappingPresentation presentation)
    {
        EnsureAvailable();
        Block? block = source.GetTargetBlock(presentation);
        if (block is null) return null;
        MeshData? borrowed = source.GetCapturedMesh(block);
        if (borrowed is null)
        {
            Release();
            return null;
        }

        if (!ReferenceEquals(block, cachedBlock) || !ReferenceEquals(borrowed, borrowedMesh))
        {
            Release();
            if (BrushTargetBounds.TryRead(borrowed, out var bounds))
                ownedMesh = new BrushTargetMesh(source, borrowed.Clone(), bounds);
            cachedBlock = block;
            borrowedMesh = borrowed;
        }

        if (ownedMesh is null) return null;
        return new BrushTargetGeometry(presentation, ownedMesh);
    }

    public void InvalidateGeometry(int blockId)
    {
        EnsureAvailable();
        if (cachedBlock?.Id == blockId) Invalidate();
    }

    public bool IsRequestingNativeMeshAt(BlockPos position) => source.IsRequestingNativeMeshAt(position);

    public void Invalidate()
    {
        EnsureAvailable();
        source.Invalidate();
        Release();
    }

    private void EnsureAvailable()
    {
        source.EnsureMainThread();
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private void Release()
    {
        BrushTargetMesh? previous = ownedMesh;
        cachedBlock = null;
        borrowedMesh = null;
        ownedMesh = null;
        previous?.Dispose();
    }

    public void Dispose()
    {
        source.EnsureMainThread();
        if (disposed) return;
        disposed = true;
        try { Release(); }
        finally { source.Dispose(); }
    }
}

internal sealed class BrushTargetMesh : IDisposable
{
    private readonly BrushTargetMeshSource source;
    private MeshRef? mesh;
    private bool disposed;

    public MeshData Data { get; }
    public BrushTargetBounds Bounds { get; }

    public BrushTargetMesh(BrushTargetMeshSource source, MeshData ownedData, BrushTargetBounds bounds)
    {
        this.source = source;
        Data = ownedData;
        Bounds = bounds;
    }

    public MeshRef GetOrUpload()
    {
        source.EnsureMainThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (mesh is null)
        {
            MeshRef uploaded = source.Upload(Data);
            if (!uploaded.Initialized || uploaded.Disposed)
            {
                uploaded.Dispose();
                throw new InvalidOperationException("The uploaded brush target mesh is unavailable.");
            }
            mesh = uploaded;
        }
        return mesh;
    }

    public void Dispose()
    {
        source.EnsureMainThread();
        if (disposed) return;
        disposed = true;
        MeshRef? previous = mesh;
        mesh = null;
        try { previous?.Dispose(); }
        finally { Data.Dispose(); }
    }
}
