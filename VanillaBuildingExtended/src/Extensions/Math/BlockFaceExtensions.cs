using System.Numerics;
using System.Runtime.CompilerServices;

using Vintagestory.API.MathTools;

namespace VanillaBuildingExtended.src.Extensions.Math;
public static class BlockFaceExtensions
{
    /// <summary>
    /// Projects a 3D position onto a 2D plane defined by the given BlockFacing.
    /// </summary>
    /// <param name="facing"></param>
    /// <param name="pos"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 ToAB(this BlockFacing facing, in Vector3 pos)
    {
        return facing.Axis switch
        {
            EnumAxis.X => new Vector2(pos.Z, pos.Y),
            EnumAxis.Y => new Vector2(pos.X, pos.Z),
            EnumAxis.Z => new Vector2(pos.X, pos.Y),
            _ => Vector2.Zero,
        };
    }
}
