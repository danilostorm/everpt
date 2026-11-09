using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading;

namespace MonoGame.Interop;

// The game runs on a worker and WebGPU belongs to the page's thread, where one synchronous call costs tens of
// microseconds. So every graphics call is written to a stream in shared memory, which mgweb.js replays. See README.md.
// The numbers here are mirrored by mgweb.js.
[SupportedOSPlatform("browser")]
internal static unsafe partial class WebStream
{
    internal enum Op
    {
        ResizeSwapchain = 1, BeginFrame, Clear, Present, SetBlendState, SetDepthStencilState, SetRasterizerState, SetViewport, SetScissor,
        SetRenderTargets, SetConstantBuffer, SetTexture, SetSampler, SetIndexBuffer, SetVertexBuffer, SetShader, SetInputLayout,
        Draw, DrawIndexed, DrawIndexedInstanced, Resolve, CreateBlendState, CreateDepthStencilState, CreateRasterizerState, CreateSampler,
        CreateBuffer, DestroyBuffer, BufferSetData, CreateTexture, DestroyTexture, TextureSetData, TextureGetData, BufferGetData,
        CreateInputLayout, DestroyInputLayout, CreateShader, DestroyShader, Dispatch, DispatchIndirect, SetShaderBuffer, SetShaderImage,
        DestroyState, BackBufferGetData,
    }

    internal const int FrameInFlightLimit = 1;

    private const int ChunkBytes = 4 * 1024 * 1024;
    private const int ControlWords = 8192;
    private const int QueueHead = 0;
    private const int QueueTail = 1;
    private const int FramesSubmitted = 2;
    private const int FramesAcknowledged = 3;
    private const int EventHead = 6;
    private const int EventTail = 7;
    private const int QueueBase = 16;
    private const int QueueLength = 256;
    private const int EventBase = QueueBase + QueueLength * 2;
    private const int EventWords = 6;
    internal const int EventCapacity = (ControlWords - EventBase) / EventWords;

    private readonly struct Posted(int sequence, nint chunk, int capacity)
    {
        public readonly int Sequence = sequence;
        public readonly nint Chunk = chunk;
        public readonly int Capacity = capacity;
    }

    private static readonly Queue<Posted> _posted = new();
    private static readonly Stack<nint> _free = new();
    private static int* _control;
    private static int* _chunk;
    private static int* _cursor;
    private static int* _end;
    private static int _capacity;
    private static int _nextHandle = 1;
    private static int _sequence;

    [JSImport("startRuntime", "mgweb")]
    private static partial void StartRuntime(int control);

    internal static void EnsureStarted()
    {
        if (_control != null)
        {
            return;
        }

        _control = (int*)NativeMemory.AllocZeroed(ControlWords, sizeof(int));
        StartRuntime((int)_control);
        NewChunk(ChunkBytes);
    }

    internal static int NewHandle() => _nextHandle++;

    private static void NewChunk(int bytes)
    {
        nint chunk;
        if (bytes == ChunkBytes && _free.Count > 0)
        {
            chunk = _free.Pop();
        }
        else
        {
            chunk = (nint)NativeMemory.Alloc((nuint)bytes);
        }

        _chunk = (int*)chunk;
        _cursor = _chunk;
        _capacity = bytes;
        _end = _chunk + bytes / sizeof(int);
    }

    private static void Reclaim()
    {
        var tail = Volatile.Read(ref _control[QueueTail]);
        while (_posted.Count > 0 && _posted.Peek().Sequence < tail)
        {
            var posted = _posted.Dequeue();
            if (posted.Capacity == ChunkBytes)
            {
                _free.Push(posted.Chunk);
            }
            else
            {
                NativeMemory.Free((void*)posted.Chunk);
            }
        }
    }

    // Hands what is written to the page's thread.
    internal static void Flush(int nextChunkBytes = ChunkBytes)
    {
        EnsureStarted();
        var words = (int)(_cursor - _chunk);
        if (words > 0)
        {
            while (_sequence - Volatile.Read(ref _control[QueueTail]) >= QueueLength)
            {
                Thread.Sleep(0);
            }

            var slot = _sequence % QueueLength;
            _control[QueueBase + slot * 2] = (int)_chunk;
            _control[QueueBase + slot * 2 + 1] = words;
            _posted.Enqueue(new Posted(_sequence, (nint)_chunk, _capacity));
            _sequence++;
            Volatile.Write(ref _control[QueueHead], _sequence);
            Reclaim();
            NewChunk(nextChunkBytes);
        }
        else if (nextChunkBytes > _capacity)
        {
            NativeMemory.Free(_chunk);
            NewChunk(nextChunkBytes);
        }
    }

    private static int* Reserve(int words)
    {
        if (_cursor + words > _end)
        {
            Flush(Math.Max(ChunkBytes, words * sizeof(int)));
        }

        var start = _cursor;
        _cursor += words;
        return start;
    }

    internal static void Write(Op op)
    {
        *Reserve(1) = (int)op;
    }

    internal static void Write(Op op, int a)
    {
        var w = Reserve(2);
        w[0] = (int)op;
        w[1] = a;
    }

    internal static void Write(Op op, int a, int b)
    {
        var w = Reserve(3);
        w[0] = (int)op;
        w[1] = a;
        w[2] = b;
    }

    internal static void Write(Op op, int a, int b, int c)
    {
        var w = Reserve(4);
        w[0] = (int)op;
        w[1] = a;
        w[2] = b;
        w[3] = c;
    }

    internal static void Write(Op op, int a, int b, int c, int d)
    {
        var w = Reserve(5);
        w[0] = (int)op;
        w[1] = a;
        w[2] = b;
        w[3] = c;
        w[4] = d;
    }

    internal static void Write(Op op, params ReadOnlySpan<int> values)
    {
        var w = Reserve(values.Length + 1);
        w[0] = (int)op;
        for (var i = 0; i < values.Length; i++)
        {
            w[i + 1] = values[i];
        }
    }

    internal static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

    // An operation whose last value is a byte count followed by the bytes, which the caller's pinned memory may not outlive.
    internal static void WriteData(Op op, ReadOnlySpan<int> values, byte* data, int bytes)
    {
        var padded = (bytes + 3) / 4;
        var w = Reserve(values.Length + 2 + padded);
        w[0] = (int)op;
        for (var i = 0; i < values.Length; i++)
        {
            w[i + 1] = values[i];
        }

        w[values.Length + 1] = bytes;
        if (bytes > 0)
        {
            if ((bytes & 3) != 0)
            {
                w[values.Length + 1 + padded] = 0;
            }

            Buffer.MemoryCopy(data, w + values.Length + 2, padded * 4L, bytes);
        }
    }

    // Runs a read back: mgweb.js writes the bytes to data and then the flag; the caller's data stays pinned during the wait.
    internal static void WaitForRead(Op op, ReadOnlySpan<int> values)
    {
        var flag = (int*)NativeMemory.AllocZeroed(1, sizeof(int));
        var w = Reserve(values.Length + 2);
        w[0] = (int)op;
        for (var i = 0; i < values.Length; i++)
        {
            w[i + 1] = values[i];
        }

        w[values.Length + 1] = (int)flag;
        Flush();
        while (Volatile.Read(ref *flag) == 0)
        {
            Thread.Sleep(0);
        }

        NativeMemory.Free(flag);
    }

    // Ends a frame, and waits while too many frames wait for the display: this paces the game like a swap chain.
    internal static void EndFrame()
    {
        Write(Op.Present);
        Flush();
        var submitted = ++_control[FramesSubmitted];
        while (submitted - Volatile.Read(ref _control[FramesAcknowledged]) > FrameInFlightLimit)
        {
            Thread.Sleep(1);
        }
    }

    internal static bool TryReadEvent(out int type, out int a, out int b, out int c, out int d)
    {
        var tail = _control[EventTail];
        if (tail == Volatile.Read(ref _control[EventHead]))
        {
            type = a = b = c = d = 0;
            return false;
        }

        var e = _control + EventBase + tail % EventCapacity * EventWords;
        type = e[0];
        a = e[1];
        b = e[2];
        c = e[3];
        d = e[4];
        Volatile.Write(ref _control[EventTail], tail + 1);
        return true;
    }
}
