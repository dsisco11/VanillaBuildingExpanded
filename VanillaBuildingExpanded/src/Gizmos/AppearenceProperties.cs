using System;
using System.Numerics;

namespace VanillaBuildingExpanded.Gizmos;

/// <summary>Visual states; Selected means currently captured, not persistent application selection.</summary>
public enum EAppearenceState
{
    /// <summary>The required baseline for every override.</summary>
    Default,
    /// <summary>The handle under the pointer.</summary>
    Hover,
    /// <summary>The handle captured for interaction; takes precedence over Hover.</summary>
    Selected
}

/// <summary>Nullable visual configuration; each unspecified property inherits directly from Default.</summary>
public readonly record struct AppearenceProperties
{
    /// <summary>Gets the supported line shape override.</summary>
    public GizmoShape? Shape { get; init; }
    /// <summary>Gets the positive nominal logical-pixel extent: arrow length or center radius.</summary>
    public double? Size { get; init; }
    /// <summary>Gets the handle's normalized RGBA color, overridden or inherited as a whole.</summary>
    public Vector4? Color { get; init; }
    /// <summary>Gets the positive native framebuffer line width.</summary>
    public float? LineWidth { get; init; }
    /// <summary>Gets the normalized label-text RGBA color.</summary>
    public Vector4? LabelTextColor { get; init; }
    /// <summary>Gets the normalized label-background RGBA color.</summary>
    public Vector4? LabelBackgroundColor { get; init; }
    /// <summary>Gets the nonblank native label font family.</summary>
    public string? LabelFontFamily { get; init; }
    /// <summary>Gets the positive label font size in logical pixels.</summary>
    public double? LabelFontSize { get; init; }
    /// <summary>Gets whether label text uses bold weight.</summary>
    public bool? LabelBold { get; init; }
    /// <summary>Gets the nonnegative label padding in logical pixels.</summary>
    public double? LabelPadding { get; init; }

    /// <summary>Validates supplied values without requiring optional overrides to be complete.</summary>
    /// <exception cref="ArgumentException">A supplied font family is blank or contains control characters.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A supplied numeric value or shape is invalid.</exception>
    internal void Validate()
    {
        if (Shape is { } shape && !Enum.IsDefined(shape)) throw new ArgumentOutOfRangeException(nameof(Shape));
        Positive(Size, nameof(Size));
        Positive(LineWidth, nameof(LineWidth));
        ValidateColor(Color, nameof(Color));
        ValidateColor(LabelTextColor, nameof(LabelTextColor));
        ValidateColor(LabelBackgroundColor, nameof(LabelBackgroundColor));
        Positive(LabelFontSize, nameof(LabelFontSize));
        if (LabelPadding is { } padding && (!double.IsFinite(padding) || padding < 0))
            throw new ArgumentOutOfRangeException(nameof(LabelPadding));
        if (LabelFontFamily is { } family)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(family, nameof(LabelFontFamily));
            foreach (char character in family)
                if (char.IsControl(character))
                    throw new ArgumentException("Font families cannot contain control characters.", nameof(LabelFontFamily));
        }
    }

    /// <summary>Resolves every field directly against one baseline and requires usable output.</summary>
    /// <param name="baseline">The Default properties, never another non-default state.</param>
    /// <param name="hasLabel">Whether complete label styling is required.</param>
    /// <returns>A fully specified handle snapshot and optional complete label style.</returns>
    /// <exception cref="ArgumentException">A required baseline value is absent or invalid.</exception>
    internal ResolvedAppearenceProperties ResolveOver(AppearenceProperties baseline, bool hasLabel)
    {
        Validate();
        baseline.Validate();
        var resolved = new AppearenceProperties
        {
            Shape = Shape ?? baseline.Shape, Size = Size ?? baseline.Size,
            Color = Color ?? baseline.Color, LineWidth = LineWidth ?? baseline.LineWidth,
            LabelTextColor = LabelTextColor ?? baseline.LabelTextColor,
            LabelBackgroundColor = LabelBackgroundColor ?? baseline.LabelBackgroundColor,
            LabelFontFamily = LabelFontFamily ?? baseline.LabelFontFamily,
            LabelFontSize = LabelFontSize ?? baseline.LabelFontSize,
            LabelBold = LabelBold ?? baseline.LabelBold,
            LabelPadding = LabelPadding ?? baseline.LabelPadding
        };
        return new(resolved, hasLabel);
    }

    /// <summary>Checks an optional positive finite dimension.</summary>
    /// <param name="value">The dimension.</param>
    /// <param name="name">The property name used in errors.</param>
    /// <exception cref="ArgumentOutOfRangeException">The supplied dimension is nonfinite or nonpositive.</exception>
    private static void Positive(double? value, string name)
    {
        if (value is { } number && (!double.IsFinite(number) || number <= 0))
            throw new ArgumentOutOfRangeException(name);
    }

    /// <summary>Checks an optional RGBA value without splitting its inheritance into channels.</summary>
    /// <param name="value">The color.</param>
    /// <param name="name">The property name used in errors.</param>
    /// <exception cref="ArgumentOutOfRangeException">A supplied component is outside finite [0, 1].</exception>
    private static void ValidateColor(Vector4? value, string name)
    {
        if (value is { } color && (!float.IsFinite(color.X) || !float.IsFinite(color.Y)
            || !float.IsFinite(color.Z) || !float.IsFinite(color.W)
            || Vector4.Min(color, Vector4.Zero) != Vector4.Zero
            || Vector4.Max(color, Vector4.One) != Vector4.One))
            throw new ArgumentOutOfRangeException(name);
    }

    /// <summary>Reads a validated required property without inventing a renderer fallback.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The resolved property.</param>
    /// <param name="name">The missing property's name.</param>
    /// <returns>The specified value.</returns>
    /// <exception cref="ArgumentException">The required property is absent.</exception>
    internal static T Required<T>(T? value, string name) where T : struct =>
        value ?? throw new ArgumentException($"Default appearance requires {name}.", name);
}

/// <summary>Nonnullable handle appearance produced by validated state resolution.</summary>
public readonly record struct ResolvedAppearenceProperties
{
    /// <summary>Gets the supported shape.</summary>
    public GizmoShape Shape { get; }
    /// <summary>Gets the positive logical-pixel extent.</summary>
    public double Size { get; }
    /// <summary>Gets the complete normalized RGBA color.</summary>
    public Vector4 Color { get; }
    /// <summary>Gets the positive framebuffer line width.</summary>
    public float LineWidth { get; }
    /// <summary>Gets complete label styling, or null when the handle has no label.</summary>
    public ResolvedGizmoLabelStyle? LabelStyle { get; }

    /// <summary>Builds a complete snapshot after supplied values have been validated.</summary>
    /// <param name="value">The merged configuration.</param>
    /// <param name="hasLabel">Whether the label fields are required.</param>
    /// <exception cref="ArgumentException">A required property is missing.</exception>
    internal ResolvedAppearenceProperties(AppearenceProperties value, bool hasLabel)
    {
        Shape = AppearenceProperties.Required(value.Shape, nameof(value.Shape));
        Size = AppearenceProperties.Required(value.Size, nameof(value.Size));
        Color = AppearenceProperties.Required(value.Color, nameof(value.Color));
        LineWidth = AppearenceProperties.Required(value.LineWidth, nameof(value.LineWidth));
        LabelStyle = hasLabel ? new ResolvedGizmoLabelStyle(value) : null;
    }
}

/// <summary>Complete raster styling, present only for a handle with an attached label.</summary>
public readonly record struct ResolvedGizmoLabelStyle
{
    /// <summary>Gets the complete text RGBA color.</summary>
    public Vector4 TextColor { get; }
    /// <summary>Gets the complete background RGBA color.</summary>
    public Vector4 BackgroundColor { get; }
    /// <summary>Gets the native font family.</summary>
    public string FontFamily { get; }
    /// <summary>Gets the positive logical-pixel font size.</summary>
    public double FontSize { get; }
    /// <summary>Gets the bold weight setting.</summary>
    public bool Bold { get; }
    /// <summary>Gets the nonnegative logical-pixel padding.</summary>
    public double Padding { get; }

    /// <summary>Requires every label field instead of using silent rendering defaults.</summary>
    /// <param name="value">Validated merged configuration.</param>
    /// <exception cref="ArgumentException">A label field is absent.</exception>
    internal ResolvedGizmoLabelStyle(AppearenceProperties value)
    {
        TextColor = AppearenceProperties.Required(value.LabelTextColor, nameof(value.LabelTextColor));
        BackgroundColor = AppearenceProperties.Required(value.LabelBackgroundColor, nameof(value.LabelBackgroundColor));
        FontFamily = value.LabelFontFamily ?? throw new ArgumentException("Default appearance requires LabelFontFamily.");
        FontSize = AppearenceProperties.Required(value.LabelFontSize, nameof(value.LabelFontSize));
        Bold = AppearenceProperties.Required(value.LabelBold, nameof(value.LabelBold));
        Padding = AppearenceProperties.Required(value.LabelPadding, nameof(value.LabelPadding));
    }
}
