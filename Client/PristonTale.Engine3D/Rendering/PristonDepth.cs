using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

// The scene's depth convention: reversed, 1 at the near plane and 0 at the far plane, in a 32-bit float buffer. Float
// precision is densest near 0, which reversed depth spends on the distance, so it is nearly even from near to far. A
// 24-bit buffer with the near plane at 1 separated surfaces only some hundredths of a unit apart at 200 units: layered
// faces (a helmet's plates, decals on the town floor) fought in the depth test and the jitter flipped them each frame.
// Every camera and portrait projection, scene depth clear and scene depth test goes through here; shadow maps keep the
// ordinary range (their own targets, projections and states).
public static class PristonDepth
{
    public const DepthFormat Format = DepthFormat.Depth32Float;
    public const float Clear = 0f;

    // Writes and tests: nearer is greater.
    public static readonly DepthStencilState Write = new()
    {
        Name = "PristonDepthWrite",
        DepthBufferEnable = true,
        DepthBufferWriteEnable = true,
        DepthBufferFunction = CompareFunction.GreaterEqual
    };

    // Tests without writing: blended surfaces and effects.
    public static readonly DepthStencilState Read = new()
    {
        Name = "PristonDepthRead",
        DepthBufferEnable = true,
        DepthBufferWriteEnable = false,
        DepthBufferFunction = CompareFunction.GreaterEqual
    };

    // The lit pass over surfaces the pre-pass already wrote, with the same vertex stage so the depth is bit-identical: only
    // the surface that won the pre-pass is shaded, and a cut-out's discarded texels fail before their pixel shader runs.
    public static readonly DepthStencilState Equal = new()
    {
        Name = "PristonDepthEqual",
        DepthBufferEnable = true,
        DepthBufferWriteEnable = false,
        DepthBufferFunction = CompareFunction.Equal
    };

    // z_ndc = n (f + z_view) / ((f - n) · -z_view): 1 at z_view = -n, 0 at z_view = -f. x, y and w are those of the
    // ordinary projection.
    public static Matrix Perspective(float verticalFieldOfView, float aspect, float near, float far)
    {
        var projection = Matrix.CreatePerspectiveFieldOfView(verticalFieldOfView, aspect, near, far);
        projection.M33 = near / (far - near);
        projection.M43 = near * far / (far - near);
        return projection;
    }

    public static float NearPlane(Matrix projection) => projection.M43 / (projection.M33 + 1f);

    public static float FarPlane(Matrix projection) => projection.M43 / projection.M33;

    // The same projection with the far plane at infinity: z_ndc = n / -z_view, so nothing beyond the near plane clips.
    public static Matrix InfiniteFar(Matrix projection)
    {
        var near = NearPlane(projection);
        projection.M33 = 0f;
        projection.M43 = near;
        return projection;
    }

    // The window-space depth Viewport.Unproject takes for a point on the near or the far plane.
    public const float NearDepth = 1f;
    public const float FarDepth = 0f;
}
