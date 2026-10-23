using ProtoBuf;

using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.Networking;

/// <summary>Ordered brush state, including mode transitions without a selected target.</summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class Packet_SetBuildBrush
{
    public required long seq;
    public required bool isActive;
    public required int orientationIndex;
    public required BlockPos? position;
    public required EBuildBrushSnapping snapping;
}
