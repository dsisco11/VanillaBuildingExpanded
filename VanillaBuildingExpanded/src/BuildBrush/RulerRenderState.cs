using VanillaBuildingExpanded.Rendering;

using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.BuildBrush;

internal interface IRulerRenderState : ILineRenderState { }

internal sealed class RulerRenderState(IRenderAPI render) : LineRenderState(render), IRulerRenderState { }
