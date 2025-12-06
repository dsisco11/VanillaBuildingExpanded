using System;
using System.Runtime.CompilerServices;

using Vintagestory.API.MathTools;

namespace VanillaBuildingExtended.src.Extensions.Math;
public static class VecExtensions
{
    #region To System Numerics Type Extensions

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static System.Numerics.Vector2 ToSNT(this Vec2f vec)
    {
        return new System.Numerics.Vector2([vec.X, vec.Y]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static System.Numerics.Vector3 ToSNT(this Vec3f vec)
    {
        return new System.Numerics.Vector3([vec.X, vec.Y, vec.Z]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static System.Numerics.Vector3 ToSNT(this Vec3d vec)
    {
        return new System.Numerics.Vector3([(float)vec.X, (float)vec.Y, (float)vec.Z]);
    }
    #endregion
}
