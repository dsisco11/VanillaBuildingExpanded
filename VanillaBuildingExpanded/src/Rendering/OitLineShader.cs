using System;
using System.Runtime.CompilerServices;

using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.Rendering;

/// <summary>A render-thread lease on the client's shared OIT line shader.</summary>
internal sealed class OitLineShader : IDisposable
{
    private static readonly ConditionalWeakTable<ICoreClientAPI, SharedProgram> programs = new();
    private readonly ICoreClientAPI api;
    private SharedProgram? shared;

    public OitLineShader(ICoreClientAPI api)
    {
        this.api = api;
        shared = programs.GetValue(api, static _ => new SharedProgram());
        shared.Users++;
    }

    public IShaderProgram GetProgram() =>
        (shared ?? throw new ObjectDisposedException(nameof(OitLineShader))).GetProgram(api);

    public void Dispose()
    {
        var owned = shared;
        shared = null;
        if (owned is null || --owned.Users != 0) return;
        programs.Remove(api);
        if (owned.Program is { Disposed: false } program) program.Dispose();
    }

    private sealed class SharedProgram
    {
        public int Users;
        public IShaderProgram? Program;
        private bool failed;

        public IShaderProgram GetProgram(ICoreClientAPI api)
        {
            if (Program is { Disposed: true })
            {
                Program = null;
                failed = false;
            }
            if (failed) throw new InvalidOperationException("The OIT line shader is unavailable.");
            if (Program is null)
            {
                // Retain failed allocations for cleanup, but never expose them to another renderer.
                failed = true;
                Program = api.Shader.NewShaderProgram()
                    ?? throw new InvalidOperationException("OIT line shader creation returned no program.");
                Program.AssetDomain = "vanillabuildingexpanded";
                Program.Oit = true;
                api.Shader.RegisterFileShaderProgram("vbe-oit-lines", Program);
                if (Program.LoadError || !Program.Compile())
                    throw new InvalidOperationException("The OIT line shader failed to compile.");
                failed = false;
            }
            if (Program.LoadError) throw new InvalidOperationException("The OIT line shader is unavailable.");
            return Program;
        }
    }
}
