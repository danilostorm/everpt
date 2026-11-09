using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Tests;

internal static class AnimationFallbackTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("position-only animation preserves authored rotation and child orientation", RotationFallbackAsync);
        yield return ("animation segment caches preserve forward playback, loops and clip jumps", SegmentCacheAsync);
        yield return ("a merged skeleton samples only the motion range holding the frame and keeps the bind pose without one", MotionRangeAsync);
        yield return ("a rotation key past the playing motion's end holds the key before it", RotationEndAsync);
    }

    private static Task MotionRangeAsync()
    {
        var bind = Matrix.CreateRotationY(0.3f);
        var baseRotation = Matrix.CreateRotationZ(0.7f);
        var turned = Quaternion.CreateFromAxisAngle(Vector3.Right, 1f);
        PristonModelObject Skirt(IReadOnlyList<PristonPositionKey> positions, IReadOnlyList<PristonMotionSegment> positionRanges) =>
            new("skirt", "", false, bind, Vector3.Zero, [], [], [],
                [new(1000, Quaternion.Identity), new(2000, turned)],
                [Matrix.Identity, Matrix.CreateFromQuaternion(turned)],
                positions, [], [],
                [new(0, 1000, 0, 0), new(1000, 2000, 0, 2)], positionRanges, [new(0, 1000, 0, 0), new(1000, 2000, 0, 0)])
            {
                BaseRotation = baseRotation
            };

        // No key of any channel in the first motion: the bind pose, not the second motion's first key.
        var idle = new PristonModelAnimator(new PristonModel("idle", [], [Skirt([], [new(0, 1000, 0, 0), new(1000, 2000, 0, 0)])], [], default));
        AssertMatrixNear(bind, idle.Evaluate(500f)[0]);
        AssertMatrixNear(Matrix.CreateFromQuaternion(Quaternion.Slerp(Quaternion.Identity, turned, 0.5f)), idle.Evaluate(1500f)[0]);

        // Position keys alone in the first motion: those positions with the base rotation.
        var moving = new PristonModelAnimator(new PristonModel("moving", [],
            [Skirt([new(0, Vector3.Zero), new(1000, new Vector3(0, 10, 0))], [new(0, 1000, 0, 2), new(1000, 2000, 0, 0)])], [], default));
        var expected = baseRotation;
        expected.Translation = new Vector3(0, 5, 0);
        AssertMatrixNear(expected, moving.Evaluate(500f)[0]);
        return Task.CompletedTask;
    }

    private static Task RotationEndAsync()
    {
        var turned = Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.PiOver2);
        var obj = new PristonModelObject("arm", "", false, Matrix.Identity, Vector3.Zero, [], [], [],
            [new(0, Quaternion.Identity), new(200, turned)],
            [Matrix.Identity, Matrix.CreateFromQuaternion(turned)],
            [new(0, Vector3.Zero), new(200, new Vector3(20, 0, 0))], [], [], [], [], []);
        var animator = new PristonModelAnimator(new PristonModel("arm", [], [obj], [], default));

        // The motion ends at 150, before the next rotation key: rotation holds, position keeps interpolating.
        var held = animator.Evaluate(100f, 150f)[0];
        var expected = Matrix.Identity;
        expected.Translation = new Vector3(10, 0, 0);
        AssertMatrixNear(expected, held);
        AssertMatrixNear(Matrix.CreateFromQuaternion(Quaternion.Slerp(Quaternion.Identity, turned, 0.5f)) * Matrix.CreateTranslation(10, 0, 0),
            animator.Evaluate(100f)[0]);
        return Task.CompletedTask;
    }

    private static Task RotationFallbackAsync()
    {
        var pelvis = new PristonModelObject("pelvis", "", false, Matrix.Identity, Vector3.Zero,
            [], [], [], [], [], [new(0, Vector3.Zero), new(1600, new Vector3(0, 10, 0))], [], [], [], [], [])
        {
            BaseRotation = Matrix.CreateRotationZ(MathHelper.PiOver2)
        };
        var child = new PristonModelObject("head", "pelvis", false, Matrix.Identity, new Vector3(20, 0, 0),
            [], [], [], [new(0, Quaternion.Identity), new(1600, Quaternion.Identity)], [Matrix.Identity, Matrix.Identity], [], [], [], [], [], []);
        var model = new PristonModel("fixture", [], [pelvis, child], [], default);
        var animator = new PristonModelAnimator(model);
        foreach (var hz in new[] { 30, 60, 120, 144 })
        {
            for (var step = 0; step <= hz; step++)
            {
                var frame = 1600f * step / hz;
                var transforms = animator.Evaluate(frame);
                Assert.Near(0, transforms[1].Translation.X);
                Assert.Near(20 + 10f * step / hz, transforms[1].Translation.Y);
                Assert.Near(0, transforms[1].Translation.Z);
            }
        }

        return Task.CompletedTask;
    }

    private static Task SegmentCacheAsync()
    {
        var rotations = new[]
        {
            Quaternion.Identity,
            Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.PiOver2),
            Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.Pi)
        };
        var rotationMatrices = rotations.Select(Matrix.CreateFromQuaternion).ToArray();
        var obj = new PristonModelObject(
            "animated",
            "",
            false,
            Matrix.Identity,
            Vector3.Zero,
            [],
            [],
            [],
            [new(0, rotations[0]), new(100, rotations[1]), new(200, rotations[2])],
            rotationMatrices,
            [new(0, Vector3.Zero), new(100, new Vector3(10, 20, 30)), new(200, new Vector3(20, 40, 60))],
            [new(0, Vector3.One), new(100, new Vector3(2, 3, 4)), new(200, new Vector3(3, 5, 7))],
            [],
            [],
            [],
            []);
        var model = new PristonModel("segment-cache", [], [obj], [], default);
        var animator = new PristonModelAnimator(model);

        foreach (var frame in new[] { 0f, 25f, 99f, 100f, 175f, 200f, 75f, 150f, 10f })
        {
            var expected = ExpectedTransform(frame, rotations);
            var actual = animator.Evaluate(frame)[0];
            AssertMatrixNear(expected, actual);
        }

        return Task.CompletedTask;
    }

    private static Matrix ExpectedTransform(float frame, IReadOnlyList<Quaternion> rotations)
    {
        var clamped = MathHelper.Clamp(frame, 0f, 200f);
        var segment = clamped < 100f ? 0 : 1;
        var amount = (clamped - segment * 100f) / 100f;
        var rotation = Quaternion.Slerp(rotations[segment], rotations[segment + 1], amount);
        var position = Vector3.Lerp(
            segment == 0 ? Vector3.Zero : new Vector3(10, 20, 30),
            segment == 0 ? new Vector3(10, 20, 30) : new Vector3(20, 40, 60),
            amount);
        var scale = Vector3.Lerp(
            segment == 0 ? Vector3.One : new Vector3(2, 3, 4),
            segment == 0 ? new Vector3(2, 3, 4) : new Vector3(3, 5, 7),
            amount);
        var expected = Matrix.CreateFromQuaternion(rotation) * Matrix.CreateScale(scale);
        expected.Translation = position;
        return expected;
    }

    private static void AssertMatrixNear(Matrix expected, Matrix actual)
    {
        Assert.Near(expected.M11, actual.M11);
        Assert.Near(expected.M12, actual.M12);
        Assert.Near(expected.M13, actual.M13);
        Assert.Near(expected.M21, actual.M21);
        Assert.Near(expected.M22, actual.M22);
        Assert.Near(expected.M23, actual.M23);
        Assert.Near(expected.M31, actual.M31);
        Assert.Near(expected.M32, actual.M32);
        Assert.Near(expected.M33, actual.M33);
        Assert.Near(expected.M41, actual.M41);
        Assert.Near(expected.M42, actual.M42);
        Assert.Near(expected.M43, actual.M43);
    }
}
