using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

// Verification of the compute path of Etervale's MonoGame fork (monogame/issues/005 to 008), one check per extension:
//  1. Image: a compute shader writes an RGBA32F storage image (RWTexture2D), a draw samples it into a render target in
//     the same frame (compute → graphics); both read back exactly.
//  2. Mip: the same into mip 1 of a mipmapped storage texture (SetValue(texture, 1)); mip 0 stays untouched.
//  3. Buffers: RWStructuredBuffer<uint2> and RWByteAddressBuffer written by a dispatch, read back with GetData.
//  4. Indirect: one dispatch writes the group counts (5, 3, 1), DispatchIndirect runs that many groups, each counting
//     itself once with an atomic: exactly those 15 groups, once each.
//  5. Ordering: in one frame, upload A, dispatch (copies it), upload B into the same buffer, dispatch again: the first
//     copy holds A, the second B (uploads on the render thread are recorded between the dispatches).
//  6. Another thread: 16 MB uploaded from a worker while frames dispatch from another buffer, then read by a dispatch.
//  7. Frames in flight: 300 frames, each a dispatch adding its frame number to a counter with no read-back in between:
//     the sum of the frame numbers (each dispatch's constants reach it).
//  8. Ray queries: an SM 6.5 effect with RayQuery is refused at load (NotSupportedException).
// Exit code: the number of failed checks (0 = pass).
using var game = new ComputeRepro.ReproGame();
game.Run();
return game.Failures;

namespace ComputeRepro
{
    internal sealed class ReproGame : Game
    {
        private const int Width = 37, Height = 29;   // not multiples of the 8 × 8 groups
        private const int Elements = 1000;
        private const int SumFrames = 300;
        private const int AsyncElements = 4 << 20;   // 16 MB of uint
        private Effect? _effect;
        private VertexBuffer? _quad;
        private StructuredBuffer? _sum;
        private StructuredBuffer? _asyncSource;
        private StructuredBuffer? _asyncTarget;
        private StructuredBuffer? _asyncPairs;
        private uint[]? _asyncData;
        private Thread? _uploader;
        private int _frame;

        public int Failures { get; private set; }

        public ReproGame()
        {
            _ = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 64, PreferredBackBufferHeight = 64 };
            Content.RootDirectory = "Content";
        }

        protected override void LoadContent()
        {
            _effect = Content.Load<Effect>("Compute");
            _quad = new VertexBuffer(GraphicsDevice, VertexPositionTexture.VertexDeclaration, 3, BufferUsage.WriteOnly);
            _quad.SetData(new VertexPositionTexture[]
            {
                new(new Vector3(-1, 1, 0), new Vector2(0, 0)),
                new(new Vector3(3, 1, 0), new Vector2(2, 0)),
                new(new Vector3(-1, -3, 0), new Vector2(0, 2)),
            });
            _sum = new StructuredBuffer(GraphicsDevice, 4, 1, BufferShaderAccess.ReadWrite);
            _effect.Parameters["Sum"].SetValue(_sum);
        }

        private void Report(string name, bool passed, string detail)
        {
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}: {detail}");
            if (!passed)
                Failures++;
        }

        private void Run(string technique, Vector4 value, int groupsX, int groupsY = 1)
        {
            _effect!.Parameters["Value"].SetValue(value);
            _effect.CurrentTechnique = _effect.Techniques[technique];
            var pass = _effect.CurrentTechnique.Passes[0];
            pass.Apply();
            GraphicsDevice.Dispatch(groupsX, groupsY, 1);
        }

        private static uint Hash(uint x, uint y)
        {
            uint h = unchecked((x * 73856093u) ^ (y * 19349663u));
            h ^= h >> 13;
            h = unchecked(h * 0x5bd1e995u);
            return h ^ (h >> 15);
        }

        private static Vector4 Expected(int x, int y, float salt) => new(x, y, (Hash((uint)x, (uint)y) & 0xFFFF) + salt, 1);

        protected override void Draw(GameTime gameTime)
        {
            if (_frame == 0)
            {
                ImageCheck();
                MipCheck();
                BuffersCheck();
                IndirectCheck();
                OrderingCheck();
                RayQueryCheck();
                StartAsync();
            }
            else if (_frame <= SumFrames)
            {
                // The worker uploads into _asyncTarget meanwhile; the frames read _asyncSource.
                Run("SumUp", new Vector4(_frame, 0, 0, 0), 1);
                _effect!.Parameters["Input"].SetValue(_asyncSource);
                _effect.Parameters["Pairs"].SetValue(_asyncPairs);
                Run("Copy", new Vector4(64, _frame, 0, 0), 1);
            }
            else
            {
                SumCheck();
                AsyncCheck();
                Console.WriteLine($"{Failures} failed checks");
                Exit();
            }
            _frame++;
            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(Color.Black);
            base.Draw(gameTime);
        }

        private void ImageCheck()
        {
            using var image = new Texture2D(GraphicsDevice, Width, Height, false, SurfaceFormat.Vector4, TextureShaderAccess.ReadWrite);
            using var target = new RenderTarget2D(GraphicsDevice, Width, Height, false, SurfaceFormat.Vector4, DepthFormat.None);
            _effect!.Parameters["Image"].SetValue(image, 0);
            Run("Image", new Vector4(Width, Height, 7, 0), (Width + 7) / 8, (Height + 7) / 8);

            GraphicsDevice.SetRenderTarget(target);
            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.DepthStencilState = DepthStencilState.None;
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            _effect.Parameters["Sampled"].SetValue(image);
            _effect.Parameters["Value"].SetValue(new Vector4(Width, Height, 0, 0));
            _effect.CurrentTechnique = _effect.Techniques["Sample"];
            GraphicsDevice.SetVertexBuffer(_quad);
            _effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 1);
            GraphicsDevice.SetRenderTarget(null);

            var written = new Vector4[Width * Height];
            var sampled = new Vector4[Width * Height];
            image.GetData(written);
            target.GetData(sampled);
            int wrong = 0, wrongSampled = 0;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    var e = Expected(x, y, 7);
                    if (written[y * Width + x] != e) wrong++;
                    if (sampled[y * Width + x] != e) wrongSampled++;
                }
            }
            Report("image", wrong == 0 && wrongSampled == 0, $"{Width}×{Height} RGBA32F: {wrong} wrong in the storage image, {wrongSampled} wrong where the draw sampled it");
        }

        private void MipCheck()
        {
            using var image = new Texture2D(GraphicsDevice, 2 * Width, 2 * Height, true, SurfaceFormat.Vector4, TextureShaderAccess.ReadWrite);
            // Mip 0 uploaded first (an upload into a storage texture), so "untouched" is a known value.
            var before = Enumerable.Repeat(new Vector4(-1, -2, -3, -4), 4 * Width * Height).ToArray();
            image.SetData(0, null, before, 0, before.Length);
            _effect!.Parameters["Image"].SetValue(image, 1);
            Run("Image", new Vector4(Width, Height, 3, 0), (Width + 7) / 8, (Height + 7) / 8);
            var mip1 = new Vector4[Width * Height];
            image.GetData(1, null, mip1, 0, mip1.Length);
            var mip0 = new Vector4[4 * Width * Height];
            image.GetData(0, null, mip0, 0, mip0.Length);
            int wrong = 0;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (mip1[y * Width + x] != Expected(x, y, 3))
                        wrong++;
                }
            }
            int touched = mip0.Count(v => v != before[0]);
            Report("mip", wrong == 0 && touched == 0, $"mip 1 of {2 * Width}×{2 * Height}: {wrong} wrong; mip 0 texels changed: {touched}");
        }

        private void BuffersCheck()
        {
            using var pairs = new StructuredBuffer(GraphicsDevice, 8, Elements, BufferShaderAccess.ReadWrite);
            using var raw = new StructuredBuffer(GraphicsDevice, 4, Elements, BufferShaderAccess.ReadWrite);
            _effect!.Parameters["Pairs"].SetValue(pairs);
            _effect.Parameters["Raw"].SetValue(raw);
            Run("Buffers", new Vector4(Elements, 0, 0, 0), (Elements + 63) / 64);
            var p = new uint[2 * Elements];
            var r = new uint[Elements];
            pairs.GetData<uint>(p);
            raw.GetData<uint>(r);
            int wrong = 0;
            for (uint i = 0; i < Elements; i++)
            {
                if (p[2 * i] != i || p[2 * i + 1] != i * i || r[i] != (i ^ 0xA5A5A5A5u))
                    wrong++;
            }
            Report("buffers", wrong == 0, $"RWStructuredBuffer<uint2> and RWByteAddressBuffer, {Elements} elements: {wrong} wrong");
        }

        private void IndirectCheck()
        {
            using var args = new StructuredBuffer(GraphicsDevice, 4, 3, BufferShaderAccess.ReadWrite);
            using var counts = new StructuredBuffer(GraphicsDevice, 4, 64 * 64, BufferShaderAccess.ReadWrite);
            _effect!.Parameters["Args"].SetValue(args);
            Run("Args", new Vector4(5, 3, 0, 0), 1);
            _effect.Parameters["Counts"].SetValue(counts);
            _effect.CurrentTechnique = _effect.Techniques["Count"];
            _effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.DispatchIndirect(args, 0);
            var c = new uint[64 * 64];
            counts.GetData<uint>(c);
            int wrong = 0;
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    if (c[y * 64 + x] != (x < 5 && y < 3 ? 1u : 0u))
                        wrong++;
                }
            }
            Report("indirect", wrong == 0, $"arguments (5, 3, 1) written by a dispatch: {wrong} groups wrong of 64×64");
        }

        private void OrderingCheck()
        {
            using var input = new StructuredBuffer(GraphicsDevice, 4, 64, BufferShaderAccess.Read);
            using var pairs = new StructuredBuffer(GraphicsDevice, 8, 128, BufferShaderAccess.ReadWrite);
            var a = Enumerable.Range(0, 64).Select(i => (uint)(1000 + i)).ToArray();
            var b = Enumerable.Range(0, 64).Select(i => (uint)(5000 + 3 * i)).ToArray();
            _effect!.Parameters["Input"].SetValue(input);
            _effect.Parameters["Pairs"].SetValue(pairs);
            // A frame is recording (the checks before drew and dispatched): both uploads go into its command buffer.
            input.SetData(a);
            Run("Copy", new Vector4(64, 1, 0, 0), 1);
            input.SetData(b);
            Run("Copy", new Vector4(64, 2, 64, 0), 1);
            var p = new uint[256];
            pairs.GetData<uint>(p);
            int wrong = 0;
            for (int i = 0; i < 64; i++)
            {
                if (p[2 * i] != a[i] || p[2 * i + 1] != 1) wrong++;
                if (p[2 * (64 + i)] != b[i] || p[2 * (64 + i) + 1] != 2) wrong++;
            }
            Report("ordering", wrong == 0, $"upload A, dispatch, upload B, dispatch in one frame: {wrong} of 128 copies wrong");
        }

        private void RayQueryCheck()
        {
            try
            {
                Content.Load<Effect>("RayQuery");
                Report("ray query", false, "the SM 6.5 effect loaded; it must be refused");
            }
            catch (Exception ex) when (ex is NotSupportedException || ex.InnerException is NotSupportedException)
            {
                var message = (ex as NotSupportedException ?? (NotSupportedException)ex.InnerException!).Message;
                Report("ray query", message.Contains("VK_KHR_ray_query", StringComparison.Ordinal), "refused: " + message);
            }
        }

        private void StartAsync()
        {
            _asyncSource = new StructuredBuffer(GraphicsDevice, 4, AsyncElements, BufferShaderAccess.Read);
            _asyncTarget = new StructuredBuffer(GraphicsDevice, 4, AsyncElements, BufferShaderAccess.Read);
            _asyncPairs = new StructuredBuffer(GraphicsDevice, 8, 64, BufferShaderAccess.ReadWrite);
            _asyncData = new uint[AsyncElements];
            for (uint i = 0; i < _asyncData.Length; i++)
                _asyncData[i] = Hash(i, 77);
            var target = _asyncTarget;
            var data = _asyncData;
            _uploader = new Thread(() => target.SetData(data)) { Name = "ComputeRepro upload" };
            _uploader.Start();
        }

        private void SumCheck()
        {
            var sum = new uint[1];
            _sum!.GetData<uint>(sum);
            uint expected = (uint)(SumFrames * (SumFrames + 1) / 2);
            Report("frames in flight", sum[0] == expected, string.Create(CultureInfo.InvariantCulture, $"sum of {SumFrames} dispatches' constants {sum[0]}, expected {expected}"));
        }

        private void AsyncCheck()
        {
            _uploader!.Join();
            _effect!.Parameters["Input"].SetValue(_asyncTarget);
            _effect.Parameters["Pairs"].SetValue(_asyncPairs);
            Run("Copy", new Vector4(64, 9, 0, 0), 1);
            var p = new uint[128];
            _asyncPairs!.GetData<uint>(p);
            var back = new uint[AsyncElements];
            _asyncTarget!.GetData<uint>(back);
            int wrongCopy = 0, wrongBuffer = 0;
            for (int i = 0; i < 64; i++)
            {
                if (p[2 * i] != _asyncData![i] || p[2 * i + 1] != 9)
                    wrongCopy++;
            }
            for (int i = 0; i < back.Length; i++)
            {
                if (back[i] != _asyncData![i])
                    wrongBuffer++;
            }
            Report("another thread", wrongCopy == 0 && wrongBuffer == 0, $"16 MB uploaded by a worker during {SumFrames} frames: {wrongCopy} of 64 dispatch reads wrong, {wrongBuffer} elements wrong");
            _asyncSource!.Dispose();
            _asyncTarget.Dispose();
            _asyncPairs.Dispose();
        }

        protected override void UnloadContent()
        {
            _quad?.Dispose();
            _sum?.Dispose();
        }
    }
}
