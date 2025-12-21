using ProtoBuf;

using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.Networking;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class Packet_SetBuildBrush
{
    public required bool isActive;
    public required int rotationIndex;
    public required BlockPos position;
    public required EBuildBrushSnapping snapping;
}
