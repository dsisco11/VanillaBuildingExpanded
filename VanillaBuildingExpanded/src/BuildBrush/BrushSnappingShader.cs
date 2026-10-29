using System;

using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class BrushSnappingShader(ICoreClientAPI api) : IDisposable
{
    private IShaderProgram? program;
    private bool disposed;

    public IShaderProgram GetProgram()
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
        IShaderProgram? candidate = null;
        try
        {
            candidate = api.Shader.NewShaderProgram()
                ?? throw new InvalidOperationException("Snapping shader creation returned no program.");
            candidate.AssetDomain = "vanillabuildingexpanded";
            candidate.Oit = true;
            api.Shader.RegisterFileShaderProgram("vbe-snapping", candidate);
            if (candidate.LoadError || !candidate.Compile())
                throw new InvalidOperationException("The snapping indicator shader failed to compile.");
            program = candidate;
            candidate = null;
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
