using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

// Repro of the state-cache defects of MonoGame's Vulkan runtime (monogame/upstream/06-state-caches.md).
//  1. Collision: two everyday blend states whose MGG_BlendState_Info bytes have the same 32-bit FNV-1a hash. The
//     runtime's hash-only cache returns the first state for the second, so the second draws with the wrong blending.
//  2. Stress: thousands of frames, each with a fresh random blend state (and depth-stencil and rasterizer states),
//     disposed while still current, so the runtime frees states that the device still points at and allocates new ones
//     at the same addresses. Every frame's pixels are checked against the blend equation on the CPU.
//  3. Viewport (change 8): a target filled one row at a time, each row drawn with a full-screen quad inside a viewport
//     chosen after SetRenderTarget. The runtime reset the viewport to the full target at the lazily begun render pass, so
//     the last row's colour covered the whole target.
//  4. Uniform buffer (change 9): 80 000 draws with fresh constants before one Present, more than the frame's 32 MB
//     uniform buffer holds; the last draw's colour must be in the target.
//  5. Pixel-shader constants (change 11): two draws that differ only in a constant of the pixel shader (Content/
//     PixelConstants.fx, whose vertex shader reads none). The runtime ignored the change and the second draw used the
//     first draw's constants.
// Exit code: the number of failed checks (0 = pass).
using var game = new StateCacheRepro.ReproGame(args.Length > 0 ? int.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture) : 2000);
game.Run();
return game.Failures;

namespace StateCacheRepro
{
    internal sealed class ReproGame : Game
    {
        private const int Size = 4;
        private readonly int _frames;
        private readonly Random _random = new(20261013);
        private RenderTarget2D? _target;
        private RenderTarget2D? _other;
        private BasicEffect? _effect;
        private int _frame;

        public int Failures { get; private set; }

        public ReproGame(int frames)
        {
            _frames = frames;
            _ = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 64, PreferredBackBufferHeight = 64 };
            Content.RootDirectory = "Content";
        }

        protected override void LoadContent()
        {
            // PreserveContents: MonoGame clears a DiscardContents target whenever it is bound (the banded test rebinds it).
            _target = new RenderTarget2D(GraphicsDevice, Size, Size, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);
            _other = new RenderTarget2D(GraphicsDevice, Size, Size, false, SurfaceFormat.Color, DepthFormat.None);
            _effect = new BasicEffect(GraphicsDevice) { VertexColorEnabled = true, World = Matrix.Identity, View = Matrix.Identity, Projection = Matrix.Identity };
        }

        protected override void Draw(GameTime gameTime)
        {
            if (_frame == 0)
            {
                CollisionTest();
                ViewportTest();
                PixelConstantsTest();
            }
            else if (_frame <= _frames)
                StressStep();
            else
            {
                // Last: the 80 000 user-primitive draws leave as many dynamic vertex buffers in the runtime's free list,
                // which slows every later frame down.
                UniformOverflowTest();
                Console.WriteLine($"{_frames} stress frames, {Failures} failed checks");
                Exit();
            }
            _frame++;
            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(Color.Black);
            base.Draw(gameTime);
        }

        // DestinationColor/Zero (alpha One/Zero, RGB) and Zero/InverseSourceAlpha (alpha BlendFactor/SourceAlpha, RGB):
        // MG_ComputeHash 0x880212a5 for both.
        private void CollisionTest()
        {
            var multiply = new BlendState
            {
                ColorSourceBlend = Blend.DestinationColor, ColorDestinationBlend = Blend.Zero,
                AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.Zero,
                ColorWriteChannels = ColorWriteChannels.Red | ColorWriteChannels.Green | ColorWriteChannels.Blue,
            };
            var fade = new BlendState
            {
                ColorSourceBlend = Blend.Zero, ColorDestinationBlend = Blend.InverseSourceAlpha,
                AlphaSourceBlend = Blend.BlendFactor, AlphaDestinationBlend = Blend.SourceAlpha,
                ColorWriteChannels = ColorWriteChannels.Red | ColorWriteChannels.Green | ColorWriteChannels.Blue,
            };
            var dst = new Color(200, 150, 100, 255);
            var src = new Color(128, 64, 32, 64);
            Check("collision: multiply", multiply, src, dst, Color.White);
            Check("collision: fade (same hash)", fade, src, dst, Color.White);
        }

        private void ViewportTest()
        {
            var device = GraphicsDevice;
            var colours = new[] { Color.Red, Color.Lime, Color.Blue, Color.Yellow };
            device.SetRenderTarget(_target);
            device.Clear(Color.Black);
            device.BlendState = BlendState.Opaque;
            device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = RasterizerState.CullNone;
            for (int row = 0; row < Size; row++)
            {
                // As a banded map does, one band per frame: other targets in between, then bind it and choose the band.
                device.SetRenderTarget(_other);
                DrawQuad(Color.Gray, Color.White);
                device.SetRenderTarget(_target);
                device.Viewport = new Viewport(0, row, Size, 1);
                DrawQuad(colours[row], Color.White);
            }
            device.SetRenderTarget(null);
            var pixels = new Color[Size * Size];
            _target!.GetData(pixels);
            for (int row = 0; row < Size; row++)
            {
                if (pixels[row * Size + 1] != colours[row])
                {
                    Failures++;
                    Console.WriteLine($"viewport: row {row} expected {colours[row]}, got {pixels[row * Size + 1]}");
                }
            }
        }

        private void PixelConstantsTest()
        {
            var device = GraphicsDevice;
            var effect = Content.Load<Effect>("PixelConstants");
            var colour = effect.Parameters["Colour"];
            device.SetRenderTarget(_target);
            device.Clear(Color.Black);
            device.BlendState = BlendState.Opaque;
            device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = RasterizerState.CullNone;
            var quad = new[]
            {
                new VertexPositionColor(new Vector3(-1, -1, 0), Color.White), new VertexPositionColor(new Vector3(-1, 1, 0), Color.White),
                new VertexPositionColor(new Vector3(1, -1, 0), Color.White), new VertexPositionColor(new Vector3(1, 1, 0), Color.White),
            };
            // Same vertices, textures and states; only the pixel shader's constant changes between the two draws.
            foreach (var c in new[] { Color.Red, Color.Lime })
            {
                colour.SetValue(c.ToVector4());
                effect.CurrentTechnique.Passes[0].Apply();
                device.DrawUserPrimitives(PrimitiveType.TriangleStrip, quad, 0, 2);
            }
            device.SetRenderTarget(null);
            var pixels = new Color[Size * Size];
            _target!.GetData(pixels);
            if (pixels[Size + 1] != Color.Lime)
            {
                Failures++;
                Console.WriteLine($"pixel constants: expected the second draw's {Color.Lime}, got {pixels[Size + 1]}");
            }
        }

        private void UniformOverflowTest()
        {
            var device = GraphicsDevice;
            device.SetRenderTarget(_target);
            device.BlendState = BlendState.Opaque;
            device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = RasterizerState.CullNone;
            const int draws = 80_000;
            int last = 0;
            for (int i = 0; i < draws; i++)
            {
                last = i % 251;
                DrawQuad(Color.White, new Color(last, 255 - last, 128, 255));
            }
            device.SetRenderTarget(null);
            var pixels = new Color[Size * Size];
            _target!.GetData(pixels);
            var expected = new Color(last, 255 - last, 128, 255);
            var got = pixels[Size + 1];
            if (Math.Abs(got.R - expected.R) > 1 || Math.Abs(got.G - expected.G) > 1 || Math.Abs(got.B - expected.B) > 1)
            {
                Failures++;
                Console.WriteLine($"uniform buffer: {draws} draws in one frame, expected {expected}, got {got}");
            }
            _effect!.DiffuseColor = Vector3.One;
        }

        // A full-target quad in the vertex colour times the effect's diffuse colour (fresh constants when it changes).
        private void DrawQuad(Color vertex, Color diffuse)
        {
            _effect!.DiffuseColor = diffuse.ToVector3();
            var quad = new[]
            {
                new VertexPositionColor(new Vector3(-1, -1, 0), vertex), new VertexPositionColor(new Vector3(-1, 1, 0), vertex),
                new VertexPositionColor(new Vector3(1, -1, 0), vertex), new VertexPositionColor(new Vector3(1, 1, 0), vertex),
            };
            _effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleStrip, quad, 0, 2);
        }

        private void StressStep()
        {
            var functions = new[] { BlendFunction.Add, BlendFunction.Subtract, BlendFunction.ReverseSubtract, BlendFunction.Min, BlendFunction.Max };
            var state = new BlendState
            {
                ColorSourceBlend = (Blend)_random.Next(13), ColorDestinationBlend = (Blend)_random.Next(13),
                ColorBlendFunction = functions[_random.Next(5)],
                AlphaSourceBlend = (Blend)_random.Next(13), AlphaDestinationBlend = (Blend)_random.Next(13),
                AlphaBlendFunction = functions[_random.Next(5)],
                ColorWriteChannels = (ColorWriteChannels)(1 + _random.Next(15)),
            };
            // Fresh depth-stencil and rasterizer states too (the same caches and destroy queues).
            using var depth = new DepthStencilState { DepthBufferEnable = false, StencilEnable = _random.Next(2) == 0, ReferenceStencil = _random.Next(256) };
            using var raster = new RasterizerState { CullMode = CullMode.None, DepthBias = _random.Next(100) * 1e-6f };
            GraphicsDevice.DepthStencilState = depth;
            GraphicsDevice.RasterizerState = raster;
            var src = new Color(_random.Next(256), _random.Next(256), _random.Next(256), _random.Next(256));
            var dst = new Color(_random.Next(256), _random.Next(256), _random.Next(256), _random.Next(256));
            var factor = new Color(_random.Next(256), _random.Next(256), _random.Next(256), _random.Next(256));
            Check($"stress {_frame}", state, src, dst, factor);
            // Disposed while still the device's current state (released natively, destroyed a few frames later, while
            // the device may still point at it).
            state.Dispose();
            GraphicsDevice.DepthStencilState = DepthStencilState.None;
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        }

        private void Check(string name, BlendState state, Color src, Color dst, Color factor)
        {
            var device = GraphicsDevice;
            device.SetRenderTarget(_target);
            device.Clear(dst);
            device.BlendState = state;
            device.BlendFactor = factor;
            if (device.DepthStencilState.DepthBufferEnable)
                device.DepthStencilState = DepthStencilState.None;
            var quad = new[]
            {
                new VertexPositionColor(new Vector3(-1, -1, 0), src), new VertexPositionColor(new Vector3(-1, 1, 0), src),
                new VertexPositionColor(new Vector3(1, -1, 0), src), new VertexPositionColor(new Vector3(1, 1, 0), src),
            };
            _effect!.CurrentTechnique.Passes[0].Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, quad, 0, 2);
            device.SetRenderTarget(null);

            var pixels = new Color[Size * Size];
            _target!.GetData(pixels);
            var expected = BlendEquation(state, src.ToVector4(), dst.ToVector4(), factor.ToVector4());
            var got = pixels[Size + 1].ToVector4();
            float error = Vector4.Distance(expected, got) * 255f;
            if (error > 3f)
            {
                Failures++;
                if (Failures <= 20)
                    Console.WriteLine($"{name}: expected {Format(expected)}, got {Format(got)} ({Describe(state)})");
            }
        }

        private static string Format(Vector4 c) => $"({c.X * 255:F0}, {c.Y * 255:F0}, {c.Z * 255:F0}, {c.W * 255:F0})";

        private static string Describe(BlendState s) =>
            $"{s.ColorSourceBlend}/{s.ColorDestinationBlend} {s.ColorBlendFunction}, a {s.AlphaSourceBlend}/{s.AlphaDestinationBlend} {s.AlphaBlendFunction}, write {s.ColorWriteChannels}";

        // The blend equation (D3D/Vulkan semantics) on 8-bit UNORM values.
        private static Vector4 BlendEquation(BlendState s, Vector4 src, Vector4 dst, Vector4 f)
        {
            Vector4 Factor(Blend b, bool alpha)
            {
                float sat = Math.Min(src.W, 1f - dst.W);
                return b switch
                {
                    Blend.One => Vector4.One,
                    Blend.Zero => Vector4.Zero,
                    Blend.SourceColor => src,
                    Blend.InverseSourceColor => Vector4.One - src,
                    Blend.SourceAlpha => new Vector4(src.W),
                    Blend.InverseSourceAlpha => new Vector4(1f - src.W),
                    Blend.DestinationColor => dst,
                    Blend.InverseDestinationColor => Vector4.One - dst,
                    Blend.DestinationAlpha => new Vector4(dst.W),
                    Blend.InverseDestinationAlpha => new Vector4(1f - dst.W),
                    Blend.BlendFactor => f,
                    Blend.InverseBlendFactor => Vector4.One - f,
                    _ => alpha ? Vector4.One : new Vector4(sat),
                };
            }

            static float Op(BlendFunction op, float a, float b, float sa, float sb) => op switch
            {
                BlendFunction.Add => a * sa + b * sb,
                BlendFunction.Subtract => a * sa - b * sb,
                BlendFunction.ReverseSubtract => b * sb - a * sa,
                BlendFunction.Min => Math.Min(a, b),
                _ => Math.Max(a, b),
            };

            var cs = Factor(s.ColorSourceBlend, false);
            var cd = Factor(s.ColorDestinationBlend, false);
            var as_ = Factor(s.AlphaSourceBlend, true);
            var ad = Factor(s.AlphaDestinationBlend, true);
            var r = new Vector4(
                Op(s.ColorBlendFunction, src.X, dst.X, cs.X, cd.X),
                Op(s.ColorBlendFunction, src.Y, dst.Y, cs.Y, cd.Y),
                Op(s.ColorBlendFunction, src.Z, dst.Z, cs.Z, cd.Z),
                Op(s.AlphaBlendFunction, src.W, dst.W, as_.W, ad.W));
            r = Vector4.Clamp(r, Vector4.Zero, Vector4.One);
            var w = s.ColorWriteChannels;
            return new Vector4(
                (w & ColorWriteChannels.Red) != 0 ? r.X : dst.X,
                (w & ColorWriteChannels.Green) != 0 ? r.Y : dst.Y,
                (w & ColorWriteChannels.Blue) != 0 ? r.Z : dst.Z,
                (w & ColorWriteChannels.Alpha) != 0 ? r.W : dst.W);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _target?.Dispose();
                _other?.Dispose();
                _effect?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
