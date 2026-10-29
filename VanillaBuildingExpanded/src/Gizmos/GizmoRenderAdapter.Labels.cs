using System;
using System.Collections.Generic;

using Cairo;
using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Projects handle labels into the same overlay viewport without changing hit geometry.</summary>
internal sealed partial class GizmoRenderAdapter
{
    private sealed record LabelTexture(string Text, double Scale, ResolvedGizmoLabelStyle Style, LoadedTexture Texture);
    private readonly Dictionary<(Gizmo Owner, GizmoHandle Handle), LabelTexture> labelTextures = [];
    private IShaderProgram? labelShader;
    private MeshRef? labelQuad;

    private void DrawLabels()
    {
        if (labelDraws.Count == 0) return;
        var render = api.Render;
        var previous = render.CurrentActiveShader;
        labelState.Capture();
        try
        {
            previous?.Stop();
            render.GLDisableDepthTest();
            render.GLDepthMask(false);
            for (int i = 0; i < labelDraws.Count; i++)
                if (CanDraw(labelDraws[i].Geometry)) DrawLabel(labelDraws[i]);
        }
        finally
        {
            try { labelState.Restore(); }
            finally { previous?.Use(); }
        }
    }

    /// <summary>Draws eligible labels in AfterBlit, separate from the OIT line draws.</summary>
    private void DrawLabel(GizmoHandleDraw draw)
    {
        if (!TryProjectLabel(draw, out var label, out double x, out double y)) return;
        var geometry = draw.Geometry;
        if (!CanDraw(geometry)) return;
        var camera = geometry.Context.Camera!;
        var style = draw.Appearance.LabelStyle
            ?? throw new InvalidOperationException("A labeled handle requires resolved label styling.");
        var key = (geometry.Owner, geometry.Handle);
        if (labelTextures.TryGetValue(key, out var cached)
            && (cached.Text != label.Text || cached.Scale != camera.GuiScale || cached.Style != style))
        {
            labelTextures.Remove(key);
            cached.Texture.Dispose();
            cached = null;
        }
        if (cached is null)
        {
            cached = new(label.Text, camera.GuiScale, style, CreateLabelTexture(label.Text, camera.GuiScale, style));
            labelTextures.Add(key, cached);
        }
        var texture = cached.Texture;
        if (x + texture.Width <= 0 || y + texture.Height <= 0 || x >= camera.Width || y >= camera.Height)
            return;
        IShaderProgram? program = null;
        try
        {
            program = GetLabelShader();
            labelQuad ??= api.Render.UploadMesh(new MeshData
            {
                xyz = [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0],
                Indices = [0, 2, 1, 0, 3, 2], VerticesCount = 4, IndicesCount = 6
            }) ?? throw new InvalidOperationException("Gizmo label quad upload failed.");
            if (labelQuad.Disposed || !labelQuad.Initialized)
                throw new InvalidOperationException("The gizmo label quad is unavailable.");
            program.Use();
            program.Uniform("screenRect", (float)(2 * x / camera.Width - 1),
                (float)(1 - 2 * y / camera.Height), 2f * texture.Width / camera.Width,
                -2f * texture.Height / camera.Height);
            program.BindTexture2D("labelTexture", texture.TextureId, 0);
            if (CanDraw(geometry)) api.Render.RenderMesh(labelQuad);
        }
        finally { program?.Stop(); }
    }

    /// <summary>Resolves the label's top-left pixel position from the rendered center or arrow tip.</summary>
    internal static bool TryProjectLabel(GizmoHandleDraw draw,
        out GizmoLabel label, out double x, out double y)
    {
        x = y = 0;
        label = null!;
        var geometry = draw.Geometry;
        var handle = geometry.Handle;
        if (!ReferenceEquals(handle.Owner, geometry.Owner) || handle.Label is not { } candidate
            || !candidate.IsVisible(handle, geometry.Context)) return false;
        if (!handle.IsEligible(geometry.Context.Dimension) || !ReferenceEquals(handle.Owner, geometry.Owner)
            || !ReferenceEquals(handle.Label, candidate)) return false;
        if (geometry.Context.Camera is not { } camera
            || !camera.TryProject(geometry.LabelAnchor, out var screen)) return false;
        x = screen[0] + candidate.OffsetX;
        y = screen[1] + candidate.OffsetY;
        if (!double.IsFinite(x) || !double.IsFinite(y)) return false;
        label = candidate;
        return true;
    }

    /// <summary>Rasterizes literal single-line text only when content, scale or resolved style changes.</summary>
    private LoadedTexture CreateLabelTexture(string text, double scale, ResolvedGizmoLabelStyle style)
    {
        double fontSize = style.FontSize * scale;
        double padding = Math.Ceiling(style.Padding * scale);
        if (!double.IsFinite(fontSize) || !double.IsFinite(padding)
            || fontSize > int.MaxValue || padding > int.MaxValue / 2)
            throw new InvalidOperationException("The resolved label style exceeds supported raster dimensions.");
        using var fontOptions = new FontOptions { Antialias = Antialias.Gray };
        using var measureSurface = new ImageSurface(Format.Argb32, 1, 1);
        using var measure = new Context(measureSurface);
        Configure(measure);
        var extents = measure.TextExtents(text);
        int width = Math.Max(1, checked((int)Math.Ceiling(extents.Width + 2 * padding)));
        int height = Math.Max(1, checked((int)Math.Ceiling(measure.FontExtents.Height + 2 * padding)));
        using var surface = new ImageSurface(Format.Argb32, width, height);
        using var ctx = new Context(surface);
        Configure(ctx);
        var background = style.BackgroundColor;
        ctx.SetSourceRGBA(background.X, background.Y, background.Z, background.W);
        ctx.Paint();
        var color = style.TextColor;
        ctx.SetSourceRGBA(color.X, color.Y, color.Z, color.W);
        ctx.MoveTo(padding - extents.XBearing, padding + ctx.FontExtents.Ascent);
        ctx.ShowText(text);
        surface.Flush();
        int id = api.Gui.LoadCairoTexture(surface, true);
        if (id <= 0) throw new InvalidOperationException("Gizmo label texture upload failed.");
        return new LoadedTexture(api) { TextureId = id, Width = width, Height = height };

        void Configure(Context context)
        {
            context.FontOptions = fontOptions;
            context.SelectFontFace(style.FontFamily, FontSlant.Normal, style.Bold ? FontWeight.Bold : FontWeight.Normal);
            context.SetFontSize(fontSize);
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
        List<(Gizmo Owner, GizmoHandle Handle)>? expired = null;
        foreach (var (key, cached) in labelTextures)
            if (key.Owner.IsDisposed || key.Owner.Owner != manager || key.Handle.IsDisposed
                || !ReferenceEquals(key.Handle.Owner, key.Owner)
                || key.Handle.Label is not { } label || label.Text != cached.Text
                || key.Handle.Appearance.LabelStyle != cached.Style)
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
