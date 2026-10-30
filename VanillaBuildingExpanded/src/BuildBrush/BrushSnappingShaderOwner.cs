using System;

using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class BrushSnappingShaderOwner<TShader>(
    ICoreClientAPI api, string passName, Func<TShader> createShader) : IDisposable
    where TShader : class, IShaderProgram
{
    private TShader? program;
    private bool disposed;

    public TShader GetProgram()
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
        TShader? candidate = null;
        try
        {
            candidate = createShader()
                ?? throw new InvalidOperationException($"Snapping shader {passName} creation returned no program.");
            candidate.AssetDomain = "vanillabuildingexpanded";
            candidate.Oit = true;
            api.Shader.RegisterFileShaderProgram(passName, candidate);
            if (candidate.LoadError || !candidate.Compile())
                throw new InvalidOperationException($"The snapping indicator shader {passName} failed to compile.");
            program = candidate;
            candidate = null;
            api.Logger.Notification("[BuildBrush] Snapping indicator shader {0} compiled.", passName);
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
