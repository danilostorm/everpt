using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

// The original texture-stage operations a material layer can declare; PristonMaterialAdapter maps them onto the material
// model's layer modes.
internal enum PristonStageOperation
{
    Modulate = 0,
    SelectTexture = 1,
    SelectCurrent = 2,
    Modulate2X = 3,
    Modulate4X = 4,
    Add = 5,
    AddSigned = 6,
    AddSigned2X = 7,
    Subtract = 8
}

// A texture layer above the base texture of a multi-texture material: its texture, how it combines with the colour so far, and its UV form state.
internal sealed record PristonMaterialLayer(Texture2D Texture, PristonStageOperation Operation, uint FormState)
{
    // Asset texture stage operation values are Direct3D 7 D3DTOP codes; 0 and 4 modulate.
    public static PristonStageOperation Decode(uint code) => code switch
    {
        2 => PristonStageOperation.SelectTexture,
        3 => PristonStageOperation.SelectCurrent,
        5 => PristonStageOperation.Modulate2X,
        6 => PristonStageOperation.Modulate4X,
        7 => PristonStageOperation.Add,
        8 => PristonStageOperation.AddSigned,
        9 => PristonStageOperation.AddSigned2X,
        10 => PristonStageOperation.Subtract,
        _ => PristonStageOperation.Modulate
    };
}
