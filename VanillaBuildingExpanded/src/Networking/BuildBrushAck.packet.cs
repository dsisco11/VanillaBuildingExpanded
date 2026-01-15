using ProtoBuf;

namespace VanillaBuildingExpanded.Networking;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class Packet_BuildBrushAck
{
    public required long lastAppliedSeq;
}
