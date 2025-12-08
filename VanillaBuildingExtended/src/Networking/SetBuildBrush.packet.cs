using ProtoBuf;

using Vintagestory.API.MathTools;

namespace VanillaBuildingExtended.Networking;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class Packet_SetBuildBrush
{
    public required int rotation;
    public required BlockPos position;
}
