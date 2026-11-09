using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Formats;
using PristonTale.Contracts.Units;

namespace PristonTale.Engine3D.Rendering;

public sealed class PristonMeshEffect : IDisposable
{
    // Frames of a baked effect model (30 per second).
    public const int MaxFrames = 256;

    private readonly GraphicsDevice _device;
    private readonly PristonUnlitEffect _effect;
    private readonly List<Batch>[] _frames;
    public int FrameCount => _frames.Length;

    public PristonMeshEffect(
        GraphicsDevice device,
        ContentManager content,
        PristonTextureLoader textures,
        PristonModel mesh,
        PristonModel skeleton,
        int frameCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frameCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frameCount, MaxFrames);
        _device = device;
        _effect = new PristonUnlitEffect(content) { Sampler = SamplerState.LinearWrap };
        _frames = new List<Batch>[frameCount];
        try
        {
            var directory = Path.GetDirectoryName(mesh.Path) ?? ".";
            for (int frame = 0; frame < frameCount; frame++)
            {
                var batches = new List<Batch>();
                _frames[frame] = batches;
                var groups = PristonModelGeometry.BuildGroups(mesh, Matrix.Identity, Color.White, skeleton, frame * GameUnits.FrameTicks);
                foreach (var group in groups)
                {
                    if (group.Key < 0 || group.Key >= mesh.Materials.Count)
                    {
                        throw new InvalidDataException("Effect mesh references an invalid material.");
                    }

                    var material = mesh.Materials[group.Key];
                    if (material.IsInvisible || material.HasNoRenderableTexture || group.Value.Count == 0)
                    {
                        continue;
                    }

                    var texture = textures.LoadTexture(directory, material.TextureName, material.OpacityName);
                    var vertices = new VertexPositionColorTexture[group.Value.Count];
                    for (var index = 0; index < vertices.Length; index++)
                    {
                        var corner = group.Value[index];
                        vertices[index] = new VertexPositionColorTexture(corner.Position, corner.Color, corner.Uv0);
                    }

                    var buffer = new VertexBuffer(device, VertexPositionColorTexture.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
                    batches.Add(new Batch(buffer, texture, vertices.Length / 3));
                    buffer.SetData(vertices);
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Draw(int frame, Matrix world, Matrix view, Matrix projection, float opacity)
    {
        var alpha = MathHelper.Clamp(opacity, 0, 1);
        // Additive: the opacity fades the added light itself.
        Draw(frame, world, view, projection, new Vector4(alpha, alpha, alpha, alpha), BlendState.Additive);
    }

    // Draws one baked frame with a linear tint (rgb) and opacity (w) under the given blend.
    public void Draw(int frame, Matrix world, Matrix view, Matrix projection, Vector4 tint, BlendState blend)
    {
        _effect.World = world;
        _effect.View = view;
        _effect.Projection = projection;
        _effect.Alpha = MathHelper.Clamp(tint.W, 0, 1);
        _effect.DiffuseColor = new Vector3(Math.Max(0, tint.X), Math.Max(0, tint.Y), Math.Max(0, tint.Z));
        _device.BlendState = blend;
        _device.DepthStencilState = PristonDepth.Read;
        _device.RasterizerState = RasterizerState.CullNone;
        foreach (var batch in _frames[Math.Clamp(frame, 0, FrameCount - 1)])
        {
            _effect.Texture = batch.Texture;
            _device.SetVertexBuffer(batch.Vertices);
            _effect.Apply();
            _device.DrawPrimitives(PrimitiveType.TriangleList, 0, batch.Primitives);
        }
    }

    public void Dispose()
    {
        foreach (var frame in _frames)
        {
            if (frame is null)
            {
                continue;
            }

            foreach (var batch in frame)
            {
                batch.Vertices.Dispose();
            }
        }

        _effect?.Dispose();
    }

    private readonly record struct Batch(VertexBuffer Vertices, Texture2D Texture, int Primitives);
}
