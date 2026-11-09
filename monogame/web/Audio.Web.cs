using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace MonoGame.Interop;

// The browser's audio runtime: the MGA_* calls, written to a queue in shared memory that mgaudio.js plays through Web Audio.
// Unlike graphics, sounds are prepared and started on several threads, so this queue has a lock and its own memory. The page
// keeps no state the game needs back: a voice's state comes from its clock here. The numbers are mirrored by mgaudio.js.
[SupportedOSPlatform("browser")]
internal static unsafe partial class MGA
{
    private enum Op
    {
        BufferPcm = 1, BufferDestroy, VoiceCreate, VoiceDestroy, VoiceSetBuffer, VoiceAppend, VoicePlay, VoicePause, VoiceResume, VoiceStop,
        VoicePan, VoicePitch, VoiceVolume, VoiceFilter, VoiceClearFilter, VoicePosition,
    }

    private sealed class BufferInfo
    {
        public nint Pcm;
        public int Bytes;
        public int FrameBytes = 1;
        public int SampleRate = 1;
        public ulong DurationMs;
    }

    private sealed class VoiceInfo
    {
        public BufferInfo Buffer;
        public bool Looped;
        public bool Playing;
        public bool Paused;
        public double PositionSeconds;
        public long ResumedAt;
        public float Rate = 1f;
        public int SampleRate;
        public int Channels;
        public double QueuedEndSeconds;
        public readonly List<double> QueuedEnds = new();
    }

    private const int ControlWords = 1 << 18;
    private const int HeadWord = 0;
    private const int TailWord = 1;
    private const int RingBase = 16;
    private const int RingWords = ControlWords - RingBase;
    private const int MaxVoices = 64;
    private const int WaveFormatPcm = 1;
    private const int WaveFormatFloat = 3;

    private static readonly object Gate = new();
    private static readonly Dictionary<int, BufferInfo> Buffers = new();
    private static readonly Dictionary<int, VoiceInfo> Voices = new();
    private static readonly Queue<(int Head, nint Memory)> PendingFrees = new();
    private static int* _control;
    private static int _head;
    private static int _nextId = 1;

    [JSImport("startAudio", "mgweb")]
    private static partial void StartAudio(int control);

    // On .NET's main thread, the one that can call JavaScript (WebRuntime.Start).
    internal static void Start()
    {
        _control = (int*)NativeMemory.AllocZeroed(ControlWords, sizeof(int));
        StartAudio((int)_control);
    }

    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

    private static void Write(Op op, params ReadOnlySpan<int> values)
    {
        lock (Gate)
        {
            var count = values.Length + 2;
            if (_control == null || _head - Volatile.Read(ref _control[TailWord]) + count > RingWords)
            {
                return;
            }

            _control[RingBase + _head % RingWords] = count;
            _control[RingBase + (_head + 1) % RingWords] = (int)op;
            for (var i = 0; i < values.Length; i++)
            {
                _control[RingBase + (_head + 2 + i) % RingWords] = values[i];
            }

            _head += count;
            Volatile.Write(ref _control[HeadWord], _head);
            var tail = Volatile.Read(ref _control[TailWord]);
            while (PendingFrees.Count > 0 && PendingFrees.Peek().Head <= tail)
            {
                NativeMemory.Free((void*)PendingFrees.Dequeue().Memory);
            }
        }
    }

    private static int Id(void* handle) => (int)(nint)handle;

    public static partial MGA_System* System_Create() => (MGA_System*)1;

    public static partial void System_Destroy(MGA_System* system)
    {
    }

    public static partial int System_GetMaxInstances() => MaxVoices;

    public static partial void System_SetReverbSettings(MGA_System* system, in ReverbSettings settings)
    {
    }

    public static partial MGA_Buffer* Buffer_Create(MGA_System* system)
    {
        lock (Gate)
        {
            var id = _nextId++;
            Buffers[id] = new BufferInfo();
            return (MGA_Buffer*)id;
        }
    }

    public static partial void Buffer_Destroy(MGA_Buffer* buffer)
    {
        var id = Id(buffer);
        nint memory;
        lock (Gate)
        {
            if (!Buffers.Remove(id, out var info))
            {
                return;
            }

            memory = info.Pcm;
        }

        Write(Op.BufferDestroy, id);
        if (memory != 0)
        {
            // The page may still read it for an earlier message: freed once the page has passed this one.
            lock (Gate)
            {
                PendingFrees.Enqueue((_head, memory));
            }
        }
    }

    // The bytes stay here for the sound's life and the page decodes a sound the first time it plays, so the thousands of
    // loaded sounds cost the page nothing until they are heard.
    private static void Initialize(int id, byte* data, int bytes, int bits, int sampleRate, int channels, int loopStart, int loopLength)
    {
        var copy = NativeMemory.Alloc((nuint)Math.Max(bytes, 1));
        Buffer.MemoryCopy(data, copy, bytes, bytes);
        var frameBytes = Math.Max(bits / 8 * channels, 1);
        lock (Gate)
        {
            if (Buffers.TryGetValue(id, out var info))
            {
                info.Pcm = (nint)copy;
                info.Bytes = bytes;
                info.FrameBytes = frameBytes;
                info.SampleRate = Math.Max(sampleRate, 1);
                info.DurationMs = (ulong)(bytes / frameBytes * 1000L / info.SampleRate);
            }
        }

        Write(Op.BufferPcm, id, (int)copy, bytes, bits, sampleRate, channels, loopStart, loopLength);
    }

    public static partial void Buffer_InitializeFormat(MGA_Buffer* buffer, byte* waveHeader, byte* waveData, int length, int loopStart, int loopLength)
    {
        var format = *(short*)waveHeader;
        var channels = *(short*)(waveHeader + 2);
        var sampleRate = *(int*)(waveHeader + 4);
        var bits = *(short*)(waveHeader + 14);
        if (format != WaveFormatPcm && format != WaveFormatFloat)
        {
            Console.Error.WriteLine($"[Audio] Wave format {format} is not played in the browser.");
            return;
        }

        Initialize(Id(buffer), waveData, length, bits, sampleRate, channels, loopStart, loopLength);
    }

    public static partial void Buffer_InitializePCM(MGA_Buffer* buffer, byte* waveData, int offset, int length, int sampleBits, int sampleRate, int channels, int loopStart, int loopLength)
    {
        Initialize(Id(buffer), waveData + offset, length, sampleBits, sampleRate, channels, loopStart, loopLength);
    }

    public static partial void Buffer_InitializeXact(MGA_Buffer* buffer, uint codec, byte* waveData, int length, int sampleRate, int blockAlignment, int channels, int loopStart, int loopLength)
    {
        Console.Error.WriteLine($"[Audio] Xact codec {codec} is not played in the browser.");
    }

    public static partial ulong Buffer_GetDuration(MGA_Buffer* buffer)
    {
        lock (Gate)
        {
            return Buffers.TryGetValue(Id(buffer), out var info) ? info.DurationMs : 0;
        }
    }

    public static partial MGA_Voice* Voice_Create(MGA_System* system, int sampleRate, int channels)
    {
        int id;
        lock (Gate)
        {
            id = _nextId++;
            Voices[id] = new VoiceInfo { SampleRate = sampleRate, Channels = channels };
        }

        Write(Op.VoiceCreate, id, sampleRate, channels);
        return (MGA_Voice*)id;
    }

    public static partial void Voice_Destroy(MGA_Voice* voice)
    {
        lock (Gate)
        {
            Voices.Remove(Id(voice));
        }

        Write(Op.VoiceDestroy, Id(voice));
    }

    private static double Now() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private static double Elapsed(VoiceInfo info) => info.Playing && !info.Paused
        ? info.PositionSeconds + (Stopwatch.GetTimestamp() - info.ResumedAt) / (double)Stopwatch.Frequency * info.Rate
        : info.PositionSeconds;

    // A streamed voice's buffers that have played by now, dropped from its queue.
    private static int FinishQueued(VoiceInfo info)
    {
        var now = Now();
        var finished = 0;
        while (info.QueuedEnds.Count > 0 && info.QueuedEnds[0] <= now)
        {
            info.QueuedEnds.RemoveAt(0);
            finished++;
        }

        return finished;
    }

    public static partial int Voice_GetBufferCount(MGA_Voice* voice)
    {
        lock (Gate)
        {
            if (!Voices.TryGetValue(Id(voice), out var info))
            {
                return 0;
            }

            return info.QueuedEnds.Count;
        }
    }

    public static partial int Voice_GetFinishedBufferCount(MGA_Voice* voice)
    {
        lock (Gate)
        {
            return Voices.TryGetValue(Id(voice), out var info) ? FinishQueued(info) : 0;
        }
    }

    public static partial void Voice_SetBuffer(MGA_Voice* voice, MGA_Buffer* buffer)
    {
        lock (Gate)
        {
            if (Voices.TryGetValue(Id(voice), out var info))
            {
                Buffers.TryGetValue(Id(buffer), out info.Buffer);
                info.Playing = false;
                info.PositionSeconds = 0;
            }
        }

        Write(Op.VoiceSetBuffer, Id(voice), Id(buffer));
    }

    public static partial void Voice_AppendBuffer(MGA_Voice* voice, byte* buffer, uint size)
    {
        var bytes = (int)size;
        var copy = NativeMemory.Alloc((nuint)Math.Max(bytes, 1));
        Buffer.MemoryCopy(buffer, copy, bytes, bytes);
        lock (Gate)
        {
            if (Voices.TryGetValue(Id(voice), out var info) && info.SampleRate > 0 && info.Channels > 0)
            {
                var seconds = bytes / (double)(2 * info.Channels) / info.SampleRate;
                info.QueuedEndSeconds = Math.Max(info.QueuedEndSeconds, Now()) + seconds;
                info.QueuedEnds.Add(info.QueuedEndSeconds);
            }
        }

        Write(Op.VoiceAppend, Id(voice), (int)copy, bytes);
        lock (Gate)
        {
            PendingFrees.Enqueue((_head, (nint)copy));
        }
    }

    public static partial void Voice_Play(MGA_Voice* voice, byte looped)
    {
        lock (Gate)
        {
            if (Voices.TryGetValue(Id(voice), out var info))
            {
                info.Playing = true;
                info.Paused = false;
                info.Looped = looped != 0;
                info.PositionSeconds = 0;
                info.ResumedAt = Stopwatch.GetTimestamp();
            }
        }

        Write(Op.VoicePlay, Id(voice), looped);
    }

    public static partial void Voice_Pause(MGA_Voice* voice)
    {
        lock (Gate)
        {
            if (Voices.TryGetValue(Id(voice), out var info) && info.Playing && !info.Paused)
            {
                info.PositionSeconds = Elapsed(info);
                info.Paused = true;
            }
        }

        Write(Op.VoicePause, Id(voice));
    }

    public static partial void Voice_Resume(MGA_Voice* voice)
    {
        lock (Gate)
        {
            if (Voices.TryGetValue(Id(voice), out var info) && info.Paused)
            {
                info.Paused = false;
                info.ResumedAt = Stopwatch.GetTimestamp();
            }
        }

        Write(Op.VoiceResume, Id(voice));
    }

    public static partial void Voice_Stop(MGA_Voice* voice, byte immediate)
    {
        lock (Gate)
        {
            if (Voices.TryGetValue(Id(voice), out var info))
            {
                info.Playing = false;
                info.Paused = false;
                info.PositionSeconds = 0;
                info.QueuedEnds.Clear();
                info.QueuedEndSeconds = 0;
            }
        }

        Write(Op.VoiceStop, Id(voice));
    }

    public static partial SoundState Voice_GetState(MGA_Voice* voice)
    {
        lock (Gate)
        {
            if (!Voices.TryGetValue(Id(voice), out var info) || !info.Playing)
            {
                return SoundState.Stopped;
            }

            if (info.Paused)
            {
                return SoundState.Paused;
            }

            if (!info.Looped && info.Buffer is not null && Elapsed(info) * 1000d >= info.Buffer.DurationMs)
            {
                info.Playing = false;
                return SoundState.Stopped;
            }

            return SoundState.Playing;
        }
    }

    public static partial ulong Voice_GetPosition(MGA_Voice* voice)
    {
        lock (Gate)
        {
            return Voices.TryGetValue(Id(voice), out var info) && info.Buffer is not null ? (ulong)(Elapsed(info) * info.Buffer.SampleRate) : 0;
        }
    }

    public static partial void Voice_SetPan(MGA_Voice* voice, float pan) => Write(Op.VoicePan, Id(voice), Bits(pan));

    public static partial void Voice_SetPitch(MGA_Voice* voice, float pitch)
    {
        lock (Gate)
        {
            if (Voices.TryGetValue(Id(voice), out var info))
            {
                info.PositionSeconds = Elapsed(info);
                info.ResumedAt = Stopwatch.GetTimestamp();
                info.Rate = MathF.Pow(2f, pitch);
            }
        }

        Write(Op.VoicePitch, Id(voice), Bits(pitch));
    }

    public static partial void Voice_SetVolume(MGA_Voice* voice, float volume) => Write(Op.VoiceVolume, Id(voice), Bits(volume));

    public static partial void Voice_SetReverbMix(MGA_Voice* voice, float mix)
    {
    }

    public static partial void Voice_SetFilterMode(MGA_Voice* voice, FilterMode mode, float filterQ, float frequency)
    {
        Write(Op.VoiceFilter, Id(voice), (int)mode, Bits(filterQ), Bits(frequency));
    }

    public static partial void Voice_ClearFilterMode(MGA_Voice* voice) => Write(Op.VoiceClearFilter, Id(voice));

    public static partial void Voice_Apply3D(MGA_Voice* voice, in Listener listener, in Emitter emitter, float distanceScale)
    {
        var offset = emitter.Position - listener.Position;
        var distance = offset.Length();
        var gain = distance > distanceScale && distance > 0f ? distanceScale / distance : 1f;
        var right = Vector3.Cross(listener.Forward, listener.Up);
        var pan = distance > 0f && right != Vector3.Zero ? Vector3.Dot(offset / distance, Vector3.Normalize(right)) : 0f;
        Write(Op.VoicePosition, Id(voice), Bits(gain), Bits(pan));
    }
}
