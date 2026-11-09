using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

// Verification of issue 011 of Etervale's MonoGame fork (monogame/issues/011-texture-load-binding.md): a pixel shader
// copies a 7 × 5 RGBA32F texture into a render target, which is read back and compared exactly.
//  1. Load, with a sampler_state naming the texture that this shader does not use (the effect processor dropped the
//     texture's binding: the shader read zeros).
//  2. Load, no sampler_state.
//  3. SampleLevel (the path that always worked), as the control.
// Exit code: the number of failed checks (0 = pass).
using var game = new TextureLoadRepro.ReproGame();
game.Run();
return game.Failures;

namespace TextureLoadRepro
{
    internal sealed class ReproGame : Game
    {
        private const int Width = 7, Height = 5;
        private Effect? _effect;
        private VertexBuffer? _triangle;

        public int Failures { get; private set; }

        public ReproGame()
        {
            _ = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 64, PreferredBackBufferHeight = 64 };
            Content.RootDirectory = "Content";
        }

        protected override void LoadContent()
        {
            _effect = Content.Load<Effect>("Load");
            _triangle = new VertexBuffer(GraphicsDevice, VertexPositionTexture.VertexDeclaration, 3, BufferUsage.WriteOnly);
            _triangle.SetData(new VertexPositionTexture[]
            {
                new(new Vector3(-1, 1, 0), new Vector2(0, 0)),
                new(new Vector3(3, 1, 0), new Vector2(2, 0)),
                new(new Vector3(-1, -3, 0), new Vector2(0, 2)),
            });
        }

        private static Vector4 Expected(int x, int y, float salt) => new(x + 1, y + 1, x * 10 + y + salt, 0.5f + salt);

        private void Check(string technique, string parameter, float salt)
        {
            var data = new Vector4[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                    data[y * Width + x] = Expected(x, y, salt);
            }
            using var source = new Texture2D(GraphicsDevice, Width, Height, false, SurfaceFormat.Vector4);
            source.SetData(data);
            using var target = new RenderTarget2D(GraphicsDevice, Width, Height, false, SurfaceFormat.Vector4, DepthFormat.None);
            var p = _effect!.Parameters[parameter];
            if (p is null)
            {
                Report(technique, false, $"the effect has no parameter '{parameter}'");
                return;
            }
            p.SetValue(source);
            _effect.Parameters["Size"]?.SetValue(new Vector4(Width, Height, 0, 0));
            GraphicsDevice.SetRenderTarget(target);
            GraphicsDevice.Clear(new Color(0, 0, 0, 0));
            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.DepthStencilState = DepthStencilState.None;
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            GraphicsDevice.SetVertexBuffer(_triangle);
            _effect.CurrentTechnique = _effect.Techniques[technique];
            _effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 1);
            GraphicsDevice.SetRenderTarget(null);
            var read = new Vector4[Width * Height];
            target.GetData(read);
            int wrong = 0;
            for (int i = 0; i < read.Length; i++)
            {
                if (read[i] != data[i])
                    wrong++;
            }
            Report(technique, wrong == 0, $"{wrong} of {read.Length} texels wrong (first read {read[0]}, expected {data[0]})");
        }

        private void Report(string name, bool passed, string detail)
        {
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}: {detail}");
            if (!passed)
                Failures++;
        }

        protected override void Draw(GameTime gameTime)
        {
            Check("LoadNamed", "Named", 1);
            Check("LoadBare", "Bare", 2);
            Check("Sample", "Sampled", 3);
            Console.WriteLine($"{Failures} failed checks");
            Exit();
            base.Draw(gameTime);
        }
    }
}
