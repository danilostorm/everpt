using System.Buffers.Binary;
using System.Text;
using Microsoft.Xna.Framework;
using PristonTale.Contracts.Units;

namespace PristonTale.Engine3D.Formats;

internal sealed class BinaryCursor
{
    private readonly byte[] _data;

    public BinaryCursor(byte[] data) => _data = data;

    public int Length => _data.Length;
    public int Position { get; set; }

    public void Require(int byteCount)
    {
        if ((uint)Position > (uint)_data.Length || byteCount < 0 || byteCount > _data.Length - Position)
        {
            throw new InvalidDataException("Unexpected end of Priston binary asset.");
        }
    }

    public byte[] Slice(int byteCount)
    {
        Require(byteCount);
        var bytes = new byte[byteCount];
        Buffer.BlockCopy(_data, Position, bytes, 0, byteCount);
        Position += byteCount;
        return bytes;
    }

    public string FixedString(int byteCount)
    {
        var bytes = ReadSpan(byteCount);
        var end = bytes.IndexOf((byte)0);
        if (end < 0)
        {
            end = bytes.Length;
        }

        return Encoding.ASCII.GetString(bytes[..end]);
    }

    public ReadOnlySpan<byte> ReadSpan(int byteCount)
    {
        Require(byteCount);
        var bytes = _data.AsSpan(Position, byteCount);
        Position += byteCount;
        return bytes;
    }

    public byte Byte()
    {
        Require(1);
        return _data[Position++];
    }

    public short Int16()
    {
        Require(2);
        var value = BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(Position, 2));
        Position += 2;
        return value;
    }

    public ushort UInt16()
    {
        Require(2);
        var value = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(Position, 2));
        Position += 2;
        return value;
    }

    public int Int32()
    {
        Require(4);
        var value = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(Position, 4));
        Position += 4;
        return value;
    }

    public uint UInt32()
    {
        Require(4);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(Position, 4));
        Position += 4;
        return value;
    }

    public float Single()
    {
        Require(4);
        var value = BinaryPrimitives.ReadSingleLittleEndian(_data.AsSpan(Position, 4));
        Position += 4;
        return value;
    }

    public double Double()
    {
        Require(8);
        var value = BinaryPrimitives.ReadDoubleLittleEndian(_data.AsSpan(Position, 8));
        Position += 8;
        return value;
    }

}

internal static class PristonBinaryUtil
{
    private static readonly Matrix ModelAxis = new(
        1f, 0f, 0f, 0f,
        0f, 0f, -1f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 0f, 1f);
    private static readonly Matrix ModelAxisInverse = Matrix.Invert(ModelAxis);

    public static string BaseName(string path)
    {
        var normalized = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFileName(normalized);
    }

    public static Matrix ReadFixedMatrix(BinaryCursor cursor)
    {
        var bytes = cursor.ReadSpan(64);
        return ConvertMatrix(new Matrix(
            ReadFixed(bytes, 0), ReadFixed(bytes, 4), ReadFixed(bytes, 8), ReadFixed(bytes, 12),
            ReadFixed(bytes, 16), ReadFixed(bytes, 20), ReadFixed(bytes, 24), ReadFixed(bytes, 28),
            ReadFixed(bytes, 32), ReadFixed(bytes, 36), ReadFixed(bytes, 40), ReadFixed(bytes, 44),
            ReadFixed(bytes, 48), ReadFixed(bytes, 52), ReadFixed(bytes, 56), ReadFixed(bytes, 60)));
    }

    public static Matrix ReadFloatMatrix(BinaryCursor cursor)
    {
        var bytes = cursor.ReadSpan(64);
        return ConvertMatrix(new Matrix(
            ReadFloat(bytes, 0), ReadFloat(bytes, 4), ReadFloat(bytes, 8), ReadFloat(bytes, 12),
            ReadFloat(bytes, 16), ReadFloat(bytes, 20), ReadFloat(bytes, 24), ReadFloat(bytes, 28),
            ReadFloat(bytes, 32), ReadFloat(bytes, 36), ReadFloat(bytes, 40), ReadFloat(bytes, 44),
            ReadFloat(bytes, 48), ReadFloat(bytes, 52), ReadFloat(bytes, 56), ReadFloat(bytes, 60)));
    }

    private static float ReadFixed(ReadOnlySpan<byte> bytes, int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4)) / GameUnits.FixedScale;
    }

    private static float ReadFloat(ReadOnlySpan<byte> bytes, int offset)
    {
        return BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(offset, 4));
    }

    private static Matrix ConvertMatrix(Matrix matrix)
    {
        return ModelAxisInverse * matrix * ModelAxis;
    }

    public static Color StageColor(short b, short g, short r, short alpha = 255)
    {
        static byte Clamp(short value) => (byte)Math.Clamp((int)value, 0, 255);
        return new Color(Clamp(r), Clamp(g), Clamp(b), Clamp(alpha));
    }

    public static BoundingBox BoundsFrom(IEnumerable<Vector3> points)
    {
        var any = false;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var point in points)
        {
            any = true;
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }

        return any ? new BoundingBox(min, max) : new BoundingBox(Vector3.Zero, Vector3.Zero);
    }
}
