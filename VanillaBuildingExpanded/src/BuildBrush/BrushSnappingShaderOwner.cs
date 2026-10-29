using System;

using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class BrushSnappingShaderOwner(ICoreClientAPI api, Func<IBrushSnappingShader>? createShader = null) : IDisposable
{
    private readonly Func<IBrushSnappingShader> createShader = createShader ?? (static () => new BrushSnappingShader());
    private IBrushSnappingShader? program;
    private bool disposed;

    public IBrushSnappingShader GetProgram()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (program is null || program.Disposed) Replace();
        return program!;
    }

    public void Replace()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var previous = program;
        program = null;
        IBrushSnappingShader? candidate = null;
        try
        {
            candidate = createShader()
                ?? throw new InvalidOperationException("Snapping shader creation returned no program.");
            candidate.AssetDomain = "vanillabuildingexpanded";
            candidate.Oit = true;
            api.Shader.RegisterFileShaderProgram("vbe-snapping", candidate);
            if (candidate.LoadError || !candidate.Compile())
                throw new InvalidOperationException("The snapping indicator shader failed to compile.");
            program = candidate;
            candidate = null;
            api.Logger.Notification("[BuildBrush] Snapping indicator shader compiled.");
        }
        finally
        {
            try
            {
                if (candidate is { Disposed: false }) candidate.Dispose();
            }
            finally
            {
                if (previous is { Disposed: false }) previous.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var previous = program;
        program = null;
        if (previous is { Disposed: false }) previous.Dispose();
    }
}
