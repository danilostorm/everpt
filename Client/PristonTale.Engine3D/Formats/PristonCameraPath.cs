using Microsoft.Xna.Framework;
using PristonTale.Engine3D.IO;
using PristonTale.Contracts.Units;
using PristonTale.Assets;

namespace PristonTale.Engine3D.Formats;

public readonly record struct PristonCameraPositionKey(int Frame, Vector3 Position);

public readonly record struct PristonDoubleQuaternion(double X, double Y, double Z, double W);

public sealed record PristonCameraRotationKey(
    int AngleFrame,
    Int3 Angles,
    int QuaternionFrame,
    PristonDoubleQuaternion IncrementalQuaternion,
    Matrix CameraToWorld)
{
    public uint QuaternionAlignmentPadding32 { get; init; }
}

public sealed class PristonCameraPath
{
    private const float WfOne = 32768f;
    private const int HeaderSize = 16;
    private const int PositionRecordSize = 16;
    private const int AngleRecordSize = 16;
    private const int QuaternionRecordSize = 40;
    private const int MatrixRecordSize = 64;
    private const int MaximumWarpFrames = 10;


    private readonly List<PristonCameraPositionKey> _positions;
    private readonly List<PristonCameraRotationKey> _rotations;
    private readonly List<(int Start, int End)> _warps = new(MaximumWarpFrames);

    private PristonCameraPath(int positionCount, int rotationCount)
    {
        _positions = new List<PristonCameraPositionKey>(positionCount);
        _rotations = new List<PristonCameraRotationKey>(rotationCount);
    }

    public int InitialFrame { get; private set; }

    public int LastFrame { get; private set; }

    public bool HasPath => _positions.Count > 0;

    public IReadOnlyList<PristonCameraPositionKey> PositionKeys => _positions;

    public IReadOnlyList<PristonCameraRotationKey> RotationKeys => _rotations;

    public void AddWarpFrame(int startFrame, int endFrame)
    {
        if (startFrame > endFrame)
        {
            throw new ArgumentOutOfRangeException(nameof(startFrame), "Camera warp start must not follow its end.");
        }

        if (_warps.Count >= MaximumWarpFrames)
        {
            throw new InvalidOperationException($"The original camera supports at most {MaximumWarpFrames} warp intervals.");
        }

        _warps.Add((checked(startFrame * GameUnits.FrameTicks), checked(endFrame * GameUnits.FrameTicks)));
    }

    public static PristonCameraPath ReadCam(string path)
    {
        var bytes = AssetFiles.ReadAllBytes(path);
        var cursor = new BinaryCursor(bytes);
        cursor.Require(HeaderSize);

        var initialFrame = cursor.Int32();
        var lastFrame = cursor.Int32();
        var positionCount = cursor.Int32();
        var rotationCount = cursor.Int32();
        ValidateCounts(positionCount, rotationCount, bytes.Length);

        var camera = new PristonCameraPath(positionCount, rotationCount)
        {
            InitialFrame = initialFrame,
            LastFrame = lastFrame
        };

        ReadPositions(cursor, camera._positions, positionCount);
        var angles = ReadAngles(cursor, rotationCount);
        var quaternions = ReadQuaternions(cursor, rotationCount);
        ReadRotations(cursor, camera._rotations, angles, quaternions);

        if (cursor.Position != cursor.Length)
        {
            throw new InvalidDataException($"Camera parser ended at {cursor.Position}, but the file has {cursor.Length} bytes.");
        }

        ValidateKeyOrder(camera._positions, camera._rotations);
        if (camera.LastFrame <= 0 && camera._positions.Count > 0)
        {
            camera.LastFrame = camera._positions[^1].Frame;
        }

        return camera;
    }

    public void Sample(float ticks, out Vector3 position, out Vector3 forward, out Vector3 up)
    {
        position = SamplePosition(ticks);
        SampleOrientation(ticks, out forward, out up);

        position = WorldSpace.Mirror(position);
        forward = WorldSpace.Mirror(forward);
        up = WorldSpace.Mirror(up);

        forward = NormalizeOrFallback(forward, Vector3.Forward);
        up = NormalizeOrFallback(up, Vector3.Up);
    }

    private static void ValidateCounts(int positionCount, int rotationCount, int fileLength)
    {
        if (positionCount < 0)
        {
            throw new InvalidDataException($"Negative camera position count {positionCount}.");
        }

        if (rotationCount < 0)
        {
            throw new InvalidDataException($"Negative camera rotation count {rotationCount}.");
        }

        var expectedLength = HeaderSize
            + (long)positionCount * PositionRecordSize
            + (long)rotationCount * (AngleRecordSize + QuaternionRecordSize + MatrixRecordSize);
        if (expectedLength != fileLength)
        {
            throw new InvalidDataException($"Camera layout requires {expectedLength} bytes for {positionCount} positions and {rotationCount} rotations; found {fileLength}.");
        }
    }

    private static void ReadPositions(BinaryCursor cursor, List<PristonCameraPositionKey> destination, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var start = cursor.Position;
            var frame = cursor.Int32();
            var position = new Vector3(cursor.Single(), cursor.Single(), cursor.Single());
            EnsureRecordSize(cursor, start, PositionRecordSize, "position key");
            destination.Add(new PristonCameraPositionKey(frame, position));
        }
    }

    private static PristonCameraAngleRecord[] ReadAngles(BinaryCursor cursor, int count)
    {
        var angles = new PristonCameraAngleRecord[count];
        for (var index = 0; index < count; index++)
        {
            var start = cursor.Position;
            var frame = cursor.Int32();
            var values = new Int3(cursor.Int32(), cursor.Int32(), cursor.Int32());
            EnsureRecordSize(cursor, start, AngleRecordSize, "rotation angle key");
            angles[index] = new PristonCameraAngleRecord(frame, values);
        }

        return angles;
    }

    private static PristonCameraQuaternionRecord[] ReadQuaternions(BinaryCursor cursor, int count)
    {
        var quaternions = new PristonCameraQuaternionRecord[count];
        for (var index = 0; index < count; index++)
        {
            var start = cursor.Position;
            var frame = cursor.Int32();
            var alignmentPadding = cursor.UInt32();
            var value = new PristonDoubleQuaternion(cursor.Double(), cursor.Double(), cursor.Double(), cursor.Double());
            EnsureRecordSize(cursor, start, QuaternionRecordSize, "rotation key");
            quaternions[index] = new PristonCameraQuaternionRecord(frame, alignmentPadding, value);
        }

        return quaternions;
    }

    private static void ReadRotations(
        BinaryCursor cursor,
        List<PristonCameraRotationKey> destination,
        IReadOnlyList<PristonCameraAngleRecord> angles,
        IReadOnlyList<PristonCameraQuaternionRecord> quaternions)
    {
        for (var index = 0; index < angles.Count; index++)
        {
            var start = cursor.Position;
            var matrix = ReadCameraFixedMatrix(cursor);
            EnsureRecordSize(cursor, start, MatrixRecordSize, "matrix key");
            var angle = angles[index];
            var quaternion = quaternions[index];
            destination.Add(new PristonCameraRotationKey(angle.Frame, angle.Angles, quaternion.Frame, quaternion.Value, matrix)
            {
                QuaternionAlignmentPadding32 = quaternion.AlignmentPadding32
            });
        }
    }

    private static Matrix ReadCameraFixedMatrix(BinaryCursor cursor)
    {
        return new Matrix(
            cursor.Int32() / WfOne,
            cursor.Int32() / WfOne,
            cursor.Int32() / WfOne,
            cursor.Int32() / WfOne,
            cursor.Int32() / WfOne,
            cursor.Int32() / WfOne,
            cursor.Int32() / WfOne,
            cursor.Int32() / WfOne,
            cursor.Int32() / WfOne,
            cursor.Int32() / WfOne,
            cursor.Int32() / WfOne,
            cursor.Int32() / WfOne,
            cursor.Int32() / WfOne,
            cursor.Int32() / WfOne,
            cursor.Int32() / WfOne,
            cursor.Int32() / WfOne);
    }

    private static void EnsureRecordSize(BinaryCursor cursor, int start, int expectedSize, string recordName)
    {
        var actualSize = cursor.Position - start;
        if (actualSize != expectedSize)
        {
            throw new InvalidDataException($"{recordName} consumed {actualSize} bytes; expected {expectedSize}.");
        }
    }

    private static void ValidateKeyOrder(
        IReadOnlyList<PristonCameraPositionKey> positions,
        IReadOnlyList<PristonCameraRotationKey> rotations)
    {
        for (var index = 1; index < positions.Count; index++)
        {
            if (positions[index].Frame < positions[index - 1].Frame)
            {
                throw new InvalidDataException("Camera position keys are not ordered by frame.");
            }
        }

        for (var index = 0; index < rotations.Count; index++)
        {
            var rotation = rotations[index];
            if (rotation.AngleFrame != rotation.QuaternionFrame)
            {
                throw new InvalidDataException($"Camera rotation key {index} has mismatched angle/quaternion frames {rotation.AngleFrame}/{rotation.QuaternionFrame}.");
            }

            if (index > 0 && rotation.AngleFrame < rotations[index - 1].AngleFrame)
            {
                throw new InvalidDataException("Camera rotation keys are not ordered by frame.");
            }
        }
    }

    private Vector3 SamplePosition(float ticks)
    {
        if (_positions.Count == 0)
        {
            return Vector3.Zero;
        }

        if (_positions.Count == 1)
        {
            return _positions[0].Position;
        }

        var value = ClampFrame(ticks, _positions[0].Frame, _positions[^1].Frame);
        var index = FindPositionSegment(value);
        var start = _positions[index];
        var end = _positions[index + 1];
        if (IsWarpSegment(start.Frame, end.Frame))
        {
            return end.Position;
        }

        var amount = SegmentAmount(value, start.Frame, end.Frame);
        return Vector3.Lerp(start.Position, end.Position, amount);
    }

    private void SampleOrientation(float ticks, out Vector3 forward, out Vector3 up)
    {
        if (_rotations.Count == 0)
        {
            forward = Vector3.Forward;
            up = Vector3.Up;
            return;
        }

        if (_rotations.Count == 1)
        {
            ExtractCameraBasis(_rotations[0].CameraToWorld, out forward, out up);
            return;
        }

        var value = ClampFrame(ticks, _rotations[0].AngleFrame, _rotations[^1].AngleFrame);
        var index = FindRotationSegment(value);
        var start = _rotations[index];
        var end = _rotations[index + 1];
        if (IsWarpSegment(start.AngleFrame, end.AngleFrame))
        {
            ExtractCameraBasis(end.CameraToWorld, out forward, out up);
            return;
        }

        var amount = SegmentAmount(value, start.AngleFrame, end.AngleFrame);
        var delta = InterpolateIncrementalRotation(end.IncrementalQuaternion, amount);
        var cameraToWorld = start.CameraToWorld * delta;
        ExtractCameraBasis(cameraToWorld, out forward, out up);
    }

    private int FindPositionSegment(float ticks)
    {
        var low = 0;
        var high = _positions.Count - 2;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            if (ticks < _positions[middle].Frame)
            {
                high = middle - 1;
                continue;
            }

            if (ticks > _positions[middle + 1].Frame)
            {
                low = middle + 1;
                continue;
            }

            return middle;
        }

        return Math.Clamp(low, 0, _positions.Count - 2);
    }

    private int FindRotationSegment(float ticks)
    {
        var low = 0;
        var high = _rotations.Count - 2;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            if (ticks < _rotations[middle].AngleFrame)
            {
                high = middle - 1;
                continue;
            }

            if (ticks > _rotations[middle + 1].AngleFrame)
            {
                low = middle + 1;
                continue;
            }

            return middle;
        }

        return Math.Clamp(low, 0, _rotations.Count - 2);
    }

    private bool IsWarpSegment(int segmentStart, int segmentEnd)
    {
        foreach (var (start, end) in _warps)
        {
            if (segmentStart >= start && segmentEnd <= end)
            {
                return true;
            }
        }

        return false;
    }

    private static float SegmentAmount(float ticks, int start, int end)
    {
        var duration = end - start;
        if (duration <= 0)
        {
            return 0f;
        }

        return (ticks - start) / duration;
    }

    private static Matrix InterpolateIncrementalRotation(PristonDoubleQuaternion authored, float amount)
    {
        // The original quaternion slerp is called with (0,0,0,0) as its first value. For a unit authored
        // quaternion the original function reduces to authored * sin(pi/2 * amount).
        var scale = MathF.Sin(MathF.PI * 0.5f * amount);
        var x = (float)authored.X * scale;
        var y = (float)authored.Y * scale;
        var z = (float)authored.Z * scale;
        var w = (float)authored.W * scale;

        var xx = x * x;
        var yy = y * y;
        var zz = z * z;
        var xy = x * y;
        var xz = x * z;
        var yz = y * z;
        var wx = w * x;
        var wy = w * y;
        var wz = w * z;

        var f11 = 1f - 2f * (yy + zz);
        var f12 = 2f * (xy - wz);
        var f13 = 2f * (xz + wy);
        var f21 = 2f * (xy + wz);
        var f22 = 1f - 2f * (xx + zz);
        var f23 = 2f * (yz - wx);
        var f31 = 2f * (xz - wy);
        var f32 = 2f * (yz + wx);
        var f33 = 1f - 2f * (xx + yy);

        // The original camera path remaps the DirectX matrix into Priston's engine basis.
        return new Matrix(
            f11, f13, f12, 0f,
            f31, f33, f32, 0f,
            f21, f23, f22, 0f,
            0f, 0f, 0f, 1f);
    }

    private static void ExtractCameraBasis(Matrix cameraToWorld, out Vector3 forward, out Vector3 up)
    {
        // The original inverts the resulting camera-to-world rotation. For a pure rotation, its world
        // basis is therefore represented by these rows.
        forward = new Vector3(cameraToWorld.M31, cameraToWorld.M32, cameraToWorld.M33);
        up = new Vector3(cameraToWorld.M21, cameraToWorld.M22, cameraToWorld.M23);

        forward = NormalizeOrFallback(forward, Vector3.Forward);
        up -= forward * Vector3.Dot(up, forward);
        up = NormalizeOrFallback(up, Vector3.Up);
    }

    private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
    {
        if (value.LengthSquared() < 1e-6f)
        {
            return fallback;
        }

        return Vector3.Normalize(value);
    }

    private static float ClampFrame(float ticks, int first, int last)
    {
        return MathHelper.Clamp(ticks, first, last);
    }

    private readonly record struct PristonCameraAngleRecord(int Frame, Int3 Angles);

    private readonly record struct PristonCameraQuaternionRecord(int Frame, uint AlignmentPadding32, PristonDoubleQuaternion Value);
}
