using System;
using System.Collections.Generic;
using System.Threading;

using HarmonyLib;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class BrushTargetWorldMeshSource : IDisposable
{
    private static BrushTargetWorldMeshSource? active;
    private readonly ICoreClientAPI api;
    private Request? request;
    private MeshData? mesh;
    private Exception? failure;
    private bool disposed, requestingMesh;

    internal sealed record Request(BrushTargetWorldMeshSource Owner, Block Block, BlockPos Position)
    {
        public int ChunkX => Position.X >> 5;
        public int ChunkY => Position.InternalY >> 5;
        public int ChunkZ => Position.Z >> 5;
        public int LocalIndex => ((Position.Y & 31) * 32 + (Position.Z & 31)) * 32 + (Position.X & 31);
        public bool IsOnChunkEdge => (Position.X & 31) is 0 or 31 || (Position.Y & 31) is 0 or 31
            || (Position.Z & 31) is 0 or 31;
    }

    public BrushTargetWorldMeshSource(ICoreClientAPI api)
    {
        this.api = api;
        if (Interlocked.CompareExchange(ref active, this, null) is not null)
            throw new InvalidOperationException("A native snapping mesh capture is already active.");
    }

    public MeshData? GetMesh(Block block, BlockPos position)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Request? current = Volatile.Read(ref request);
        if (current is null || !ReferenceEquals(current.Block, block) || current.Position != position)
        {
            Invalidate();
            current = new Request(this, block, position.Copy());
            Volatile.Write(ref request, current);
            api.Logger.Debug("[BuildBrush] Requesting native snapping mesh at {0}.", position);
            requestingMesh = true;
            try { api.World.BlockAccessor.MarkBlockDirty(position.Copy(), (IPlayer?)null); }
            finally { requestingMesh = false; }
        }
        if (failure is not null)
            throw new InvalidOperationException("Native snapping mesh capture failed.", failure);
        return mesh;
    }

    public bool IsRequestingMeshAt(BlockPos position) =>
        requestingMesh && Volatile.Read(ref request)?.Position == position;

    internal static Request? FindRequest(int chunkX, int chunkY, int chunkZ, bool skipChunkCenter = false)
    {
        BrushTargetWorldMeshSource? source = Volatile.Read(ref active);
        Request? current = source is null ? null : Volatile.Read(ref source.request);
        return current is not null && current.ChunkX == chunkX && current.ChunkY == chunkY
            && current.ChunkZ == chunkZ && (!skipChunkCenter || current.IsOnChunkEdge) ? current : null;
    }

    internal void Publish(Request captured, MeshData? result, Exception? error)
    {
        if (Volatile.Read(ref disposed) || !ReferenceEquals(captured, Volatile.Read(ref request)))
        {
            result?.Dispose();
            return;
        }
        api.Event.EnqueueMainThreadTask(() =>
        {
            if (disposed || !ReferenceEquals(captured, Volatile.Read(ref request)))
            {
                result?.Dispose();
                return;
            }
            if (error is not null)
            {
                result?.Dispose();
                failure = error;
                api.Logger.Error("[BuildBrush] Native snapping mesh capture failed: {0}", error);
                return;
            }
            if (mesh is not null && result is not null
                && mesh.xyz.AsSpan(0, mesh.VerticesCount * 3).SequenceEqual(result.xyz.AsSpan(0, result.VerticesCount * 3))
                && mesh.Indices.AsSpan(0, mesh.IndicesCount).SequenceEqual(result.Indices.AsSpan(0, result.IndicesCount))
                && mesh.Flags.AsSpan().SequenceEqual(result.Flags.AsSpan()))
            {
                result.Dispose();
                return;
            }
            mesh?.Dispose();
            mesh = result;
            if (result is null)
                api.Logger.Debug("[BuildBrush] Native terrain tessellation emitted no snapping target triangles at {0}.",
                    captured.Position);
        }, "vbe-snapping-native-mesh");
    }

    public void Invalidate()
    {
        Volatile.Write(ref request, null);
        failure = null;
        mesh?.Dispose();
        mesh = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Interlocked.CompareExchange(ref active, null, this);
        Invalidate();
    }
}

internal sealed class BrushTargetMeshCapture(BrushTargetWorldMeshSource.Request request)
{
    private readonly List<float>[] positions = [[], [], []];
    private readonly List<int>[] indices = [[], [], []];
    private readonly List<int>[] flags = [[], [], []];
    private Exception? failure;

    internal readonly record struct Pool(MeshData Data, int FirstIndex, int Lod, EnumChunkRenderPass Pass);
    public bool IsDecorPass { get; set; }

    public bool Matches(TCTCache vars, Block block) =>
        vars.index3d == request.LocalIndex && (IsDecorPass || block.Id == request.Block.Id);

    public static List<Pool> Snapshot(MeshData[][][] pools)
    {
        List<Pool> result = [];
        for (int lod = 0; lod < Math.Min(3, pools.Length); lod++)
            for (int pass = 0; pass < pools[lod].Length; pass++)
                if (pools[lod][pass] is { } atlases)
                    foreach (MeshData? data in atlases)
                        if (data is not null) result.Add(new Pool(data, data.IndicesCount, lod, (EnumChunkRenderPass)pass));
        return result;
    }

    public void Append(List<Pool> pools)
    {
        if (failure is not null) return;
        try
        {
            foreach (Pool pool in pools)
            {
                MeshData data = pool.Data;
                if (data.IndicesCount == pool.FirstIndex) continue;
                Dictionary<int, int> vertices = [];
                for (int i = pool.FirstIndex; i < data.IndicesCount; i++)
                {
                    int source = data.Indices[i];
                    if (source < 0 || source >= data.VerticesCount)
                        throw new InvalidOperationException("Native target mesh contains an invalid vertex index.");
                    if (!vertices.TryGetValue(source, out int destination))
                    {
                        destination = positions[pool.Lod].Count / 3;
                        vertices.Add(source, destination);
                        positions[pool.Lod].Add(data.xyz[source * 3] - (request.Position.X & 31));
                        positions[pool.Lod].Add(data.xyz[source * 3 + 1] - (request.Position.Y & 31));
                        positions[pool.Lod].Add(data.xyz[source * 3 + 2] - (request.Position.Z & 31));
                        int nativeFlags = data.Flags is null ? 0 : data.Flags[source];
                        // The procedural shader uses the low byte for the native depth path, not material glow.
                        flags[pool.Lod].Add((nativeFlags & (VertexFlags.NormalBitMask | VertexFlags.ZOffsetBitMask))
                            | (int)pool.Pass);
                    }
                    indices[pool.Lod].Add(destination);
                }
            }
        }
        catch (Exception error) when (error is InvalidOperationException or IndexOutOfRangeException
            or ArgumentOutOfRangeException)
        {
            // An optional observer must not interrupt native chunk construction.
            failure = error;
        }
    }

    public MeshData? Finish()
    {
        if (failure is not null) return null;
        List<float> xyz = [];
        List<int> triangles = [];
        List<int> renderFlags = [];
        // Pools 0, 1 and 2 all render nearby; only pool 3 is a distant replacement.
        for (int lod = 0; lod < positions.Length; lod++)
        {
            int offset = xyz.Count / 3;
            xyz.AddRange(positions[lod]);
            renderFlags.AddRange(flags[lod]);
            foreach (int index in indices[lod]) triangles.Add(index + offset);
        }
        if (triangles.Count == 0) return null;
        MeshData result = new(xyz.Count / 3, triangles.Count, withUv: false, withRgba: false);
        xyz.CopyTo(result.xyz);
        triangles.CopyTo(result.Indices);
        renderFlags.CopyTo(result.Flags);
        result.VerticesCount = xyz.Count / 3;
        result.IndicesCount = triangles.Count;
        return result;
    }

    public void Publish() => request.Owner.Publish(request, Finish(), failure);
}

[HarmonyPatch]
internal static class BrushTargetNativeMeshCapture
{
    [ThreadStatic] private static BrushTargetMeshCapture? current;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ChunkTesselator), nameof(ChunkTesselator.NowProcessChunk))]
    private static void Begin(int chunkX, int chunkY, int chunkZ, bool skipChunkCenter,
        out BrushTargetMeshCapture? __state)
    {
        var request = BrushTargetWorldMeshSource.FindRequest(chunkX, chunkY, chunkZ, skipChunkCenter);
        __state = current = request is null ? null : new BrushTargetMeshCapture(request);
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(ChunkTesselator), nameof(ChunkTesselator.NowProcessChunk))]
    private static void End(Exception? __exception, BrushTargetMeshCapture? __state)
    {
        current = null;
        if (__exception is null) __state?.Publish();
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ChunkTesselator), "BuildDecorPolygons")]
    private static void BeforeDecors(out bool __state)
    {
        __state = current?.IsDecorPass ?? false;
        if (current is not null) current.IsDecorPass = true;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(ChunkTesselator), "BuildDecorPolygons")]
    private static void AfterDecors(bool __state)
    {
        if (current is not null) current.IsDecorPass = __state;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ChunkTesselator), "TesselateBlock",
        new[] { typeof(Block), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int) })]
    private static void BeforeBlock(Block block, TCTCache ___vars, MeshData[][][] ___currentModeldataByRenderPassByLodLevel,
        out List<BrushTargetMeshCapture.Pool>? __state)
    {
        __state = current?.Matches(___vars, block) == true
            ? BrushTargetMeshCapture.Snapshot(___currentModeldataByRenderPassByLodLevel) : null;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ChunkTesselator), "TesselateBlock",
        new[] { typeof(Block), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int) })]
    private static void AfterBlock(List<BrushTargetMeshCapture.Pool>? __state)
    {
        if (__state is not null) current?.Append(__state);
    }
}
