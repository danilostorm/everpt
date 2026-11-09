using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MonoGame.Framework.Utilities
{
    internal static partial class ReflectionHelpers
    {
        // This helper caches the Marshal.SizeOf result
        // as it generates an allocation on each call.
        // Etervale fork: private, as upstream's Obsolete text asked; FastSizeOf<T>() is the way in (CS0618 was the
        // warning of that unfinished change).
        private static class SizeOf<T>
        {
            static int _sizeOf;

            static SizeOf()
            {
                _sizeOf = Marshal.SizeOf<T>();
            }

            static public int Get()
            {
                return _sizeOf;
            }
        }
    }
}
