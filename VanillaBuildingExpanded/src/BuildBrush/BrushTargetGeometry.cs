using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaBuildingExpanded.BuildBrush;

internal sealed class BrushTargetGeometry
{
    public BrushSnappingPresentation Presentation { get; }
    public BrushTargetMesh Mesh { get; }

    public BrushTargetGeometry(BrushSnappingPresentation presentation, BrushTargetMesh mesh)
    {
        Presentation = presentation;
        Mesh = mesh;
    }

    public void SetModelMatrix(Matrixf model, float[] cameraMatrix, Vec3d camera)
    {
        BlockPos target = Presentation.TargetPosition;
        double dimensionY = (double)target.dimension * BlockPos.DimensionBoundary;
        model.Set(cameraMatrix).Translate(target.X - camera.X,
            target.Y + dimensionY - camera.Y, target.Z - camera.Z);
    }
}
