using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics;

using Cairo;
using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Projects handle labels into the same overlay viewport without changing hit geometry.</summary>
internal sealed partial class GizmoRenderAdapter
{
    private sealed record LabelTexture(string Text, double Scale, LoadedTexture Texture);
    private readonly Dictionary<(Gizmo Gizmo, GizmoHandleId Handle), LabelTexture> labelTextures = [];
    private IShaderProgram? labelShader;
    private MeshRef? labelQuad;

    /// <summary>Draws eligible labels after handle lines, sharing the enclosing render-state guard.</summary>
    private void DrawLabels(PositionGizmoDraw draw)
    {
        if (draw.Gizmo.Labels.Count == 0) return;
        IShaderProgram? program = null;
        try
        {
            foreach (var part in draw.Geometry.Parts)
            {
                if (disposed || failed || !desired || !draw.Gizmo.IsEligible(draw.Context.Dimension)) break;
                if (!TryProjectLabel(draw, part, out var label, out double x, out double y)) continue;
                if (disposed || failed || !desired || !draw.Gizmo.IsEligible(draw.Context.Dimension)) break;
                var camera = draw.Context.Camera!;
                var key = ((Gizmo)draw.Gizmo, part.Handle);
                if (labelTextures.TryGetValue(key, out var cached)
                    && (cached.Text != label.Text || cached.Scale != camera.GuiScale))
                {
                    labelTextures.Remove(key);
                    cached.Texture.Dispose();
                    cached = null;
                }
                if (cached is null)
                {
                    cached = new(label.Text, camera.GuiScale, CreateLabelTexture(label.Text, camera.GuiScale));
                    labelTextures.Add(key, cached);
                }
                var texture = cached.Texture;
                if (x + texture.Width <= 0 || y + texture.Height <= 0 || x >= camera.Width || y >= camera.Height)
                    continue;
                if (program is null)
                {
                    shader?.Stop();
                    program = GetLabelShader();
                    labelQuad ??= api.Render.UploadMesh(new MeshData
                    {
                        xyz = [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0],
                        Indices = [0, 2, 1, 0, 3, 2], VerticesCount = 4, IndicesCount = 6
                    }) ?? throw new InvalidOperationException("Gizmo label quad upload failed.");
                    if (labelQuad.Disposed || !labelQuad.Initialized)
                        throw new InvalidOperationException("The gizmo label quad is unavailable.");
                    program.Use();
                }
                program.Uniform("screenRect", (float)(2 * x / camera.Width - 1),
                    (float)(1 - 2 * y / camera.Height), 2f * texture.Width / camera.Width,
                    -2f * texture.Height / camera.Height);
                program.BindTexture2D("labelTexture", texture.TextureId, 0);
                api.Render.RenderMesh(labelQuad!);
            }
        }
        finally { program?.Stop(); }
    }

    /// <summary>Resolves the label's top-left pixel position from the rendered center or arrow tip.</summary>
    internal static bool TryProjectLabel(PositionGizmoDraw draw, PositionGizmoPart part,
        out GizmoLabel label, out double x, out double y)
    {
        x = y = 0;
        label = null!;
        if (!draw.Gizmo.Labels.TryGetValue(part.Handle, out var candidate)
            || !candidate.IsVisible(draw.Gizmo, part.Handle, draw.Context)) return false;
        var anchor = part.Shape == GizmoShape.Arrow
            ? part.Transform(Vector256.Create(1d, 0d, 0d, 0d)) : part.Origin;
        if (draw.Context.Camera is not { } camera || !camera.TryProject(anchor, out var screen)) return false;
        x = screen[0] + candidate.OffsetX;
        y = screen[1] + candidate.OffsetY;
        if (!double.IsFinite(x) || !double.IsFinite(y)) return false;
        label = candidate;
        return true;
    }

    /// <summary>Rasterizes literal single-line text only when content or GUI scale changes.</summary>
    private LoadedTexture CreateLabelTexture(string text, double scale)
    {
        using var measureSurface = new ImageSurface(Format.Argb32, 1, 1);
        using var measure = new Context(measureSurface);
        Configure(measure);
        var extents = measure.TextExtents(text);
        double padding = Math.Ceiling(3 * scale);
        int width = checked((int)Math.Ceiling(extents.Width + 2 * padding));
        int height = checked((int)Math.Ceiling(measure.FontExtents.Height + 2 * padding));
        using var surface = new ImageSurface(Format.Argb32, width, height);
        using var ctx = new Context(surface);
        Configure(ctx);
        ctx.SetSourceRGBA(0, 0, 0, 0.65);
        ctx.Paint();
        ctx.SetSourceRGBA(1, 1, 1, 1);
        ctx.MoveTo(padding - extents.XBearing, padding + ctx.FontExtents.Ascent);
        ctx.ShowText(text);
        surface.DemulAlpha();
        int id = api.Gui.LoadCairoTexture(surface, true);
        if (id <= 0) throw new InvalidOperationException("Gizmo label texture upload failed.");
        return new LoadedTexture(api) { TextureId = id, Width = width, Height = height };

        void Configure(Context context)
        {
            context.SelectFontFace(GuiStyle.StandardFontName, FontSlant.Normal, FontWeight.Bold);
            context.SetFontSize(14 * scale);
        }
    }

    /// <summary>Builds a textured screen-space shader independent of ambient perspective or GUI matrices.</summary>
    private IShaderProgram GetLabelShader()
    {
        if (labelShader is null || labelShader.Disposed)
        {
            labelShader = api.Shader.NewShaderProgram()
                ?? throw new InvalidOperationException("Gizmo label shader creation failed.");
            var vertex = api.Shader.NewShader(EnumShaderType.VertexShader)
                ?? throw new InvalidOperationException("Gizmo label vertex shader creation failed.");
            vertex.Code = """
                #version 330 core
                layout(location = 0) in vec3 vertexPositionIn;
                uniform vec4 screenRect;
                out vec2 uv;
                void main() {
                    uv = vertexPositionIn.xy;
                    gl_Position = vec4(screenRect.xy + vertexPositionIn.xy * screenRect.zw, 0.0, 1.0);
                }
                """;
            labelShader.VertexShader = vertex;
            var fragment = api.Shader.NewShader(EnumShaderType.FragmentShader)
                ?? throw new InvalidOperationException("Gizmo label fragment shader creation failed.");
            fragment.Code = """
                #version 330 core
                uniform sampler2D labelTexture;
                in vec2 uv;
                layout(location = 0) out vec4 outColor;
                void main() {
                    outColor = texture(labelTexture, uv);
                }
                """;
            labelShader.FragmentShader = fragment;
            api.Shader.RegisterMemoryShaderProgram("vbe-gizmo-labels", labelShader);
            if (!labelShader.Compile()) throw new InvalidOperationException("The gizmo label shader failed to compile.");
        }
        if (labelShader.LoadError) throw new InvalidOperationException("The gizmo label shader is unavailable.");
        return labelShader;
    }

    /// <summary>Releases cached textures when their handle label or manager membership no longer exists.</summary>
    private void PruneLabelTextures()
    {
        List<(Gizmo Gizmo, GizmoHandleId Handle)>? expired = null;
        foreach (var (key, cached) in labelTextures)
            if (key.Gizmo.IsDisposed || key.Gizmo.Owner != manager
                || !key.Gizmo.Labels.TryGetValue(key.Handle, out var label) || label.Text != cached.Text)
                (expired ??= []).Add(key);
        if (expired is null) return;
        foreach (var key in expired)
        {
            var texture = labelTextures[key].Texture;
            labelTextures.Remove(key);
            texture.Dispose();
        }
    }
}
