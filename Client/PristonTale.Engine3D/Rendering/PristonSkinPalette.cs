using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering;

internal sealed class PristonSkinPalette
{
    private readonly Vector4[] _previousRows;
    private uint _packedFrame = uint.MaxValue;

    public PristonSkinPalette(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, PristonShaderLibrary.MaxBones);
        Matrices = new Matrix[count];
        ShaderRows = new Vector4[count * 3];
        _previousRows = new Vector4[count * 3];
    }

    public Matrix[] Matrices { get; }
    public Vector4[] ShaderRows { get; }
    public int Count => Matrices.Length;
    public uint Revision { get; private set; }

    // frame: the renderer's motion frame (PristonRenderer.MotionFrame) the pose belongs to. The first pack of a frame
    // keeps the rows it replaces as the previous pose of the motion vectors.
    public void Pack(uint frame = 0)
    {
        if (frame != _packedFrame)
        {
            Array.Copy(ShaderRows, _previousRows, ShaderRows.Length);
            if (_packedFrame == uint.MaxValue)
            {
                PackRows(_previousRows);
            }

            _packedFrame = frame;
        }

        PackRows(ShaderRows);
        Revision++;
    }

    // The pose of the previous frame for the frame being drawn: a palette not packed in that frame did not move.
    public Vector4[] PreviousRows(uint frame) => frame == _packedFrame ? _previousRows : ShaderRows;

    private void PackRows(Vector4[] rows)
    {
        for (var bone = 0; bone < Matrices.Length; bone++)
        {
            var matrix = Matrices[bone];
            var row = bone * 3;
            rows[row] = new Vector4(matrix.M11, matrix.M21, matrix.M31, matrix.M41);
            rows[row + 1] = new Vector4(matrix.M12, matrix.M22, matrix.M32, matrix.M42);
            rows[row + 2] = new Vector4(matrix.M13, matrix.M23, matrix.M33, matrix.M43);
        }
    }
}
