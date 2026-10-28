using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class WorkAreaPersistence : IDisposable
{
    public const string ConfigFileName = "vanillabuildingexpanded-workareas.json";
    private const int SchemaVersion = 2;
    private readonly ICoreClientAPI api;
    private readonly IClientWorldAccessor world;
    private readonly WorkAreaController controller;
    private readonly string? worldId;
    private JArray savedAreas = [];
    private WorkAreaResizeMode savedResizeMode;
    private bool disposed;

    public bool IsDirty { get; private set; }

    public WorkAreaPersistence(ICoreClientAPI api, WorkAreaController controller)
    {
        this.api = api;
        this.controller = controller;
        world = api.World;
        worldId = world.SavegameIdentifier;
        controller.ResetAreas();
        controller.SetResizeMode(WorkAreaResizeMode.Expansion);
        if (string.IsNullOrWhiteSpace(worldId))
        {
            Report("workarea-persistence-no-world", "SavegameIdentifier is unavailable; this work area is memory-only.");
        }
        else
        {
            Load();
        }
        savedAreas = WriteAreas();
        savedResizeMode = controller.ResizeMode;
        controller.EndpointsCommitted += OnAreasCommitted;
        controller.AreasCommitted += OnAreasCommitted;
        controller.ResizeModeChanged += OnAreasCommitted;
    }

    public bool TrySave()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsDirty) return true;
        if (string.IsNullOrWhiteSpace(worldId)) return false;
        try
        {
            var document = ReadDocument();
            foreach (var area in controller.CommittedAreas) ValidatePair(area.Start, area.End);
            var current = WriteAreas();
            var worlds = (JObject)document["worlds"]!;
            var entry = worlds[worldId] is JObject existing ? (JObject)existing.DeepClone() : new JObject();
            entry["areas"] = current;
            worlds[worldId] = entry;
            document["resizeMode"] = controller.ResizeMode == WorkAreaResizeMode.Centered ? "centered" : "expansion";
            WriteDocument(document);
            savedAreas = current;
            savedResizeMode = controller.ResizeMode;
            IsDirty = false;
            return true;
        }
        catch (NotSupportedException error)
        {
            Report("workarea-persistence-unsupported", error.ToString());
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            Report("workarea-persistence-save-failed", error.ToString());
        }
        return false;
    }

    public void Dispose()
    {
        if (disposed) return;
        controller.EndpointsCommitted -= OnAreasCommitted;
        controller.AreasCommitted -= OnAreasCommitted;
        controller.ResizeModeChanged -= OnAreasCommitted;
        try
        {
            controller.CancelEndpointEdit();
            TrySave();
        }
        finally { disposed = true; }
    }

    private void OnAreasCommitted()
    {
        IsDirty = controller.ResizeMode != savedResizeMode || !JToken.DeepEquals(WriteAreas(), savedAreas);
        if (IsDirty) TrySave();
    }

    private void Load()
    {
        try
        {
            var document = ReadDocument();
            controller.SetResizeMode(ReadResizeMode(document));
            var worlds = (JObject)document["worlds"]!;
            if (!worlds.TryGetValue(worldId!, StringComparison.Ordinal, out var entry)) return;
            try
            {
                if (entry is not JObject record || record["areas"] is not JArray list)
                    throw new InvalidDataException("The saved world must contain a named work areas array.");
                var areas = new List<NamedWorkArea>();
                var ids = new HashSet<Guid>();
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in list)
                {
                    if (item is not JObject area || area["id"]?.Type != JTokenType.String
                        || !Guid.TryParseExact(area["id"]!.Value<string>(), "N", out var id) || id == Guid.Empty
                        || area["name"]?.Type != JTokenType.String
                        || !WorkAreaController.IsValidName(area["name"]!.Value<string>())
                        || !ids.Add(id) || !names.Add(area["name"]!.Value<string>()!)
                        || !area.ContainsKey("start") || !area.ContainsKey("end"))
                        throw new InvalidDataException("Saved work areas require unique IDs, valid unique names and nullable endpoints.");
                    var start = ReadEndpoint(area["start"]!);
                    var end = ReadEndpoint(area["end"]!);
                    ValidatePair(start, end);
                    bool visible = true;
                    if (area.TryGetValue("isVisible", out var visibility))
                    {
                        if (visibility.Type != JTokenType.Boolean)
                            throw new InvalidDataException("A saved work area's isVisible must be a boolean.");
                        visible = visibility.Value<bool>();
                    }
                    areas.Add(new(id, area["name"]!.Value<string>()!, start, end, visible));
                }
                controller.RestoreAreas(areas);
            }
            catch (InvalidDataException error)
            {
                Report("workarea-persistence-invalid-entry", error.ToString());
            }
        }
        catch (NotSupportedException error)
        {
            Report("workarea-persistence-unsupported", error.ToString());
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            Report("workarea-persistence-load-failed", error.ToString());
        }
    }

    private JObject ReadDocument()
    {
        var loaded = api.LoadModConfig(ConfigFileName);
        if (loaded is null)
            return new JObject { ["schemaVersion"] = SchemaVersion, ["worlds"] = new JObject() };
        if (loaded.Token is not JObject document)
            throw new InvalidDataException("The work area configuration must be a JSON object.");
        int version = ReadInteger(document, "schemaVersion");
        if (version is not (1 or SchemaVersion))
            throw new NotSupportedException($"Unsupported work area schema version {version}.");
        if (document["worlds"] is not JObject)
            throw new InvalidDataException("The work area configuration must contain a worlds dictionary.");
        // The single-area format is intentionally retired without migrating its bounds.
        if (version == 1)
            return new JObject { ["schemaVersion"] = SchemaVersion, ["worlds"] = new JObject() };
        return (JObject)document.DeepClone();
    }

    /// <summary>Reads the shared policy, defaulting older collection documents to expansion.</summary>
    /// <param name="document">The work-area configuration document.</param>
    /// <returns>The supported shared numeric resize policy.</returns>
    /// <exception cref="InvalidDataException">An explicit policy value is not supported.</exception>
    private static WorkAreaResizeMode ReadResizeMode(JObject document)
    {
        if (!document.TryGetValue("resizeMode", out var value)) return WorkAreaResizeMode.Expansion;
        if (value.Type == JTokenType.String)
        {
            if (value.Value<string>() == "expansion") return WorkAreaResizeMode.Expansion;
            if (value.Value<string>() == "centered") return WorkAreaResizeMode.Centered;
        }
        throw new InvalidDataException("The shared resizeMode must be 'expansion' or 'centered'.");
    }

    private JArray WriteAreas()
    {
        var result = new JArray();
        foreach (var area in controller.CommittedAreas)
        {
            result.Add(new JObject { ["id"] = area.Id.ToString("N"), ["name"] = area.Name,
                ["start"] = WriteEndpoint(area.Start), ["end"] = WriteEndpoint(area.End),
                ["isVisible"] = area.IsVisible });
        }
        return result;
    }

    private static WorkAreaEndpoint? ReadEndpoint(JToken token)
    {
        if (token.Type == JTokenType.Null) return null;
        if (token is not JObject record)
            throw new InvalidDataException("An endpoint must be null or a coordinate record.");
        return new WorkAreaEndpoint(ReadInteger(record, "x"), ReadInteger(record, "y"),
            ReadInteger(record, "z"), ReadInteger(record, "dimension"));
    }

    private static int ReadInteger(JObject record, string name)
    {
        if (record[name] is not { Type: JTokenType.Integer } value
            || !int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            throw new InvalidDataException($"'{name}' must be an explicit 32-bit integer.");
        return result;
    }

    private void ValidatePair(WorkAreaEndpoint? start, WorkAreaEndpoint? end)
    {
        if ((start is { } first && !IsValidEndpoint(first)) || (end is { } second && !IsValidEndpoint(second)))
            throw new InvalidDataException("A work area endpoint is outside the world's coordinate/dimension bounds.");
        if (start is { } a && end is { } b && a.Dimension != b.Dimension)
            throw new InvalidDataException("Saved work area endpoints belong to different dimensions.");
    }

    private bool IsValidEndpoint(WorkAreaEndpoint endpoint)
    {
        return endpoint.IsValid(world.BlockAccessor);
    }

    private static JToken WriteEndpoint(WorkAreaEndpoint? endpoint) => endpoint is { } value
        ? new JObject { ["x"] = value.X, ["y"] = value.Y, ["z"] = value.Z, ["dimension"] = value.Dimension }
        : JValue.CreateNull();

    private void WriteDocument(JObject document)
    {
        // The engine writer truncates its target; promote only a completely written staging file.
        string stagingName = $"{ConfigFileName}.{Guid.NewGuid():N}.tmp";
        string stagingPath = Path.Combine(GamePaths.ModConfig, stagingName);
        try
        {
            api.StoreModConfig(new JsonObject(document), stagingName);
            File.Move(stagingPath, Path.Combine(GamePaths.ModConfig, ConfigFileName), overwrite: true);
        }
        finally
        {
            try { File.Delete(stagingPath); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Report("workarea-persistence-cleanup-failed", error.ToString());
            }
        }
    }

    private void Report(string key, string details)
    {
        api.Logger.Error("[WorkArea] {0}: {1}", key, details);
        string code = $"vanillabuildingexpanded:{key}";
        api.TriggerIngameError(this, code, Lang.Get(code));
    }
}
