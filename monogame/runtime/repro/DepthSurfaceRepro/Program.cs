using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

// Verification of issue 016 of Etervale's MonoGame fork (monogame/issues/016-depth-surface-targets.md): an 8 × 4
// SurfaceFormat.Depth32Float render target, drawn into by passes without colour, read back through a pixel shader into a
// Vector4 target and compared exactly.
//  1. A full triangle at depth 0.75, then one at 0.25 whose left half is cut by clip: Load reads 0.75 left, 0.25 right.
//  2. The same surface through GatherRed with a point sampler.
//  3. Bound again in a later pass without a clear: a triangle at 0.5 tested against the kept depth changes only the
//     left half (the surface keeps its content between passes, and its depth is tested).
//  4. A clear that names the target and the depth buffer sets every texel to the clear depth (no colour to clear).
// Exit code: the number of failed checks (0 = pass).
using var game = new DepthSurfaceRepro.ReproGame();
game.Run();
return game.Failures;

namespace DepthSurfaceRepro
{
    internal sealed class ReproGame : Game
    {
        private const int Width = 8, Height = 4;
        private Effect? _effect;
        private VertexBuffer? _triangle;
        private RenderTarget2D? _surface;
        private RenderTarget2D? _readback;

        public int Failures { get; private set; }

        public ReproGame()
        {
            _ = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 64, PreferredBackBufferHeight = 64 };
            Content.RootDirectory = "Content";
        }

        protected override void LoadContent()
        {
            _effect = Content.Load<Effect>("Depth");
            _triangle = new VertexBuffer(GraphicsDevice, VertexPositionTexture.VertexDeclaration, 3, BufferUsage.WriteOnly);
            _triangle.SetData(new VertexPositionTexture[]
            {
                new(new Vector3(-1, 1, 0), new Vector2(0, 0)),
                new(new Vector3(3, 1, 0), new Vector2(2, 0)),
                new(new Vector3(-1, -3, 0), new Vector2(0, 2)),
            });
            _surface = new RenderTarget2D(GraphicsDevice, Width, Height, false, SurfaceFormat.Depth32Float, DepthFormat.None, 0,
                RenderTargetUsage.PreserveContents);
            _readback = new RenderTarget2D(GraphicsDevice, Width, Height, false, SurfaceFormat.Vector4, DepthFormat.None);
        }

        private void DrawDepth(float depth, bool clipLeft)
        {
            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            _effect!.Parameters["Params"].SetValue(new Vector4(depth, clipLeft ? 1 : 0, 0, 0));
            Draw("Depth");
        }

        private void Draw(string technique)
        {
            GraphicsDevice.SetVertexBuffer(_triangle);
            _effect!.CurrentTechnique = _effect.Techniques[technique];
            _effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 1);
        }

        private void Check(string name, string technique, Func<int, float> expectedAtColumn)
        {
            GraphicsDevice.SetRenderTarget(_readback);
            GraphicsDevice.Clear(new Color(0, 0, 0, 0));
            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.DepthStencilState = DepthStencilState.None;
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            _effect!.Parameters["Surface"].SetValue(_surface);
            _effect.Parameters["Params"].SetValue(Vector4.Zero);
            Draw(technique);
            GraphicsDevice.SetRenderTarget(null);
            var read = new Vector4[Width * Height];
            _readback!.GetData(read);
            int wrong = 0;
            for (int i = 0; i < read.Length; i++)
            {
                if (read[i].X != expectedAtColumn(i % Width))
                    wrong++;
            }
            Report(name, wrong == 0, $"{wrong} of {read.Length} texels wrong (first row {string.Join(' ', read.Take(Width).Select(v => v.X))})");
        }

        private void Report(string name, bool passed, string detail)
        {
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}: {detail}");
            if (!passed)
                Failures++;
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.SetRenderTarget(_surface);
            GraphicsDevice.Clear(ClearOptions.DepthBuffer, Color.Transparent, 1f, 0);
            DrawDepth(0.75f, clipLeft: false);
            DrawDepth(0.25f, clipLeft: true);
            Check("depth written, clip honoured, Load", "Load", x => x < Width / 2 ? 0.75f : 0.25f);
            Check("GatherRed", "Gather", x => x < Width / 2 ? 0.75f : 0.25f);

            GraphicsDevice.SetRenderTarget(_surface);
            DrawDepth(0.5f, clipLeft: false);
            Check("content kept between passes and depth-tested", "Load", x => x < Width / 2 ? 0.5f : 0.25f);

            GraphicsDevice.SetRenderTarget(_surface);
            GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Transparent, 1f, 0);
            Check("clear of target and depth", "Load", _ => 1f);

            Console.WriteLine($"{Failures} failed checks");
            Exit();
            base.Draw(gameTime);
        }
    }
}
