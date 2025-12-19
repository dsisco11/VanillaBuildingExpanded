using ProtoBuf;

using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.Networking;

/// <summary>
/// Packet sent from server to client to control dimension preview rendering.
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class Packet_BrushDimensionPreview
{
    /// <summary>
    /// The dimension ID to render, or -1 to disable rendering.
    /// </summary>
    public int DimensionId;

    /// <summary>
    /// The origin position of the dimension preview.
    /// </summary>
    public BlockPos? Position;
}
