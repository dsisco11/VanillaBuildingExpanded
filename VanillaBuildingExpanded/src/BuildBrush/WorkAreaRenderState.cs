using VanillaBuildingExpanded.Rendering;

using Vintagestory.API.Client;

namespace VanillaBuildingExpanded.BuildBrush;

internal interface IWorkAreaRenderState : ILineRenderState { }

internal sealed class WorkAreaRenderState(IRenderAPI render) : LineRenderState(render), IWorkAreaRenderState { }
