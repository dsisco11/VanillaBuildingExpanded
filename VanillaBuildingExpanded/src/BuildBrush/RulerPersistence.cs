using System;
using System.Globalization;
using System.IO;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class RulerPersistence : IDisposable
{
    public const string ConfigFileName = "vanillabuildingexpanded-rulers.json";
    private const int SchemaVersion = 1;
    private readonly ICoreClientAPI api;
    private readonly IClientWorldAccessor world;
    private readonly RulerController controller;
    private readonly string? worldId;
    private RulerEndpoint? savedStart;
    private RulerEndpoint? savedEnd;
    private bool disposed;

    public bool IsDirty { get; private set; }

    public RulerPersistence(ICoreClientAPI api, RulerController controller)
    {
        this.api = api;
        this.controller = controller;
        world = api.World;
        worldId = world.SavegameIdentifier;
        controller.RestoreEndpoints(null, null);
        if (string.IsNullOrWhiteSpace(worldId))
        {
            Report("ruler-persistence-no-world", "SavegameIdentifier is unavailable; this ruler is memory-only.");
        }
        else
        {
            Load();
        }
        savedStart = controller.State.Start;
        savedEnd = controller.State.End;
        controller.EndpointsCommitted += OnEndpointsCommitted;
    }

    public bool TrySave()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsDirty) return true;
        if (string.IsNullOrWhiteSpace(worldId)) return false;
        try
        {
            var document = ReadDocument();
            var state = controller.State;
            ValidatePair(state.Start, state.End);
            var worlds = (JObject)document["worlds"]!;
            var entry = worlds[worldId] is JObject existing ? (JObject)existing.DeepClone() : new JObject();
            entry["start"] = WriteEndpoint(state.Start);
            entry["end"] = WriteEndpoint(state.End);
            worlds[worldId] = entry;
            WriteDocument(document);
            savedStart = state.Start;
            savedEnd = state.End;
            IsDirty = false;
            return true;
        }
        catch (NotSupportedException error)
        {
            Report("ruler-persistence-unsupported", error.ToString());
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            Report("ruler-persistence-save-failed", error.ToString());
        }
        return false;
    }

    public void Dispose()
    {
        if (disposed) return;
        controller.EndpointsCommitted -= OnEndpointsCommitted;
        try { TrySave(); }
        finally { disposed = true; }
    }

    private void OnEndpointsCommitted()
    {
        var state = controller.State;
        if (state.Start != savedStart || state.End != savedEnd) IsDirty = true;
        if (IsDirty) TrySave();
    }

    private void Load()
    {
        try
        {
            var document = ReadDocument();
            var worlds = (JObject)document["worlds"]!;
            if (!worlds.TryGetValue(worldId!, StringComparison.Ordinal, out var entry)) return;
            try
            {
                if (entry is not JObject record || !record.ContainsKey("start") || !record.ContainsKey("end"))
                    throw new InvalidDataException("The saved ruler must contain nullable start and end records.");
                var start = ReadEndpoint(record["start"]!);
                var end = ReadEndpoint(record["end"]!);
                ValidatePair(start, end);
                controller.RestoreEndpoints(start, end);
            }
            catch (InvalidDataException error)
            {
                Report("ruler-persistence-invalid-entry", error.ToString());
            }
        }
        catch (NotSupportedException error)
        {
            Report("ruler-persistence-unsupported", error.ToString());
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            Report("ruler-persistence-load-failed", error.ToString());
        }
    }

    private JObject ReadDocument()
    {
        var loaded = api.LoadModConfig(ConfigFileName);
        if (loaded is null)
            return new JObject { ["schemaVersion"] = SchemaVersion, ["worlds"] = new JObject() };
        if (loaded.Token is not JObject document)
            throw new InvalidDataException("The ruler configuration must be a JSON object.");
        int version = ReadInteger(document, "schemaVersion");
        if (version != SchemaVersion)
            throw new NotSupportedException($"Unsupported ruler schema version {version}.");
        if (document["worlds"] is not JObject)
            throw new InvalidDataException("The ruler configuration must contain a worlds dictionary.");
        return (JObject)document.DeepClone();
    }

    private static RulerEndpoint? ReadEndpoint(JToken token)
    {
        if (token.Type == JTokenType.Null) return null;
        if (token is not JObject record)
            throw new InvalidDataException("An endpoint must be null or a coordinate record.");
        return new RulerEndpoint(ReadInteger(record, "x"), ReadInteger(record, "y"),
            ReadInteger(record, "z"), ReadInteger(record, "dimension"));
    }

    private static int ReadInteger(JObject record, string name)
    {
        if (record[name] is not { Type: JTokenType.Integer } value
            || !int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            throw new InvalidDataException($"'{name}' must be an explicit 32-bit integer.");
        return result;
    }

    private void ValidatePair(RulerEndpoint? start, RulerEndpoint? end)
    {
        if ((start is { } first && !IsValidEndpoint(first)) || (end is { } second && !IsValidEndpoint(second)))
            throw new InvalidDataException("A ruler endpoint is outside the world's coordinate/dimension bounds.");
        if (start is { } a && end is { } b && a.Dimension != b.Dimension)
            throw new InvalidDataException("Saved ruler endpoints belong to different dimensions.");
    }

    private bool IsValidEndpoint(RulerEndpoint endpoint)
    {
        long internalY = (long)endpoint.Dimension * BlockPos.DimensionBoundary + endpoint.Y;
        return endpoint.Dimension >= 0 && endpoint.Y >= 0 && endpoint.Y < BlockPos.DimensionBoundary
            && internalY <= int.MaxValue
            && world.BlockAccessor.IsValidPos(new BlockPos(endpoint.X, endpoint.Y, endpoint.Z, endpoint.Dimension));
    }

    private static JToken WriteEndpoint(RulerEndpoint? endpoint) => endpoint is { } value
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
                Report("ruler-persistence-cleanup-failed", error.ToString());
            }
        }
    }

    private void Report(string key, string details)
    {
        api.Logger.Error("[Ruler] {0}: {1}", key, details);
        string code = $"vanillabuildingexpanded:{key}";
        api.TriggerIngameError(this, code, Lang.Get(code));
    }
}
