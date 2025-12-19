using ProtoBuf;

using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.Networking;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class Packet_SetBuildBrush
{
    public required bool isActive;
    public required int orientationIndex;
    public required float rotationY;
    public required BlockPos position;
    public required EBuildBrushSnapping snapping;
}
