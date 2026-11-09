using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

// The pose an actor's palette shows, copied out of its animator (whose buffer bone queries and portraits evaluate into)
// with the frame and motion it was taken at. The crowd level of detail keeps it between samples.
public sealed class ActorPose
{
    private Matrix[] _transforms = [];

    public float Frame { get; private set; } = float.NaN;

    public PristonMotionInfo Motion { get; private set; }

    public bool Taken => !float.IsNaN(Frame);

    public Matrix[] Transforms => _transforms;

    // A sample is due when none was taken, the motion changed, or a refresh finds the frame moved on.
    public bool NeedsSample(bool refresh, float frame, PristonMotionInfo motion) =>
        !Taken || !ReferenceEquals(Motion, motion) || refresh && Frame != frame;

    public Matrix[] Take(Matrix[] evaluated, float frame, PristonMotionInfo motion)
    {
        if (_transforms.Length != evaluated.Length)
        {
            _transforms = new Matrix[evaluated.Length];
        }

        evaluated.CopyTo(_transforms, 0);
        Frame = frame;
        Motion = motion;
        return _transforms;
    }
}
