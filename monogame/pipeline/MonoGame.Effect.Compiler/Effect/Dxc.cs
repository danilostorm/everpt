// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork: runs the DXC that ships with the dotnet-mgcb tool, in place of upstream's MonoGame.Tool.Dxc package
// (monogame/pipeline/README.md).

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MonoGame.Effect
{
    /// <summary>
    /// The DirectX shader compiler of the content builder: the <c>dotnet-mgcb</c> tool carries DXC for every host
    /// (<c>linux-x64/bin/dxc</c> with its library in <c>../lib</c>, <c>windows-x64/dxc.exe</c>, <c>osx/bin/dxc</c>). Inside
    /// MGCB the tool's folder is the application's; elsewhere (the effect tests) the tool package is found in the NuGet
    /// cache at the version of the repository's tool manifest.
    /// </summary>
    internal static class Dxc
    {
        /// <summary>The dotnet-mgcb version of <c>.config/dotnet-tools.json</c> (a test keeps them equal).</summary>
        internal const string MgcbToolVersion = "3.8.6";

        private static readonly Lazy<string> Executable = new(Locate);

        /// <summary>The path of DXC inside the tool's folder for this host.</summary>
        internal static string RelativeExecutable
        {
            get
            {
                var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
                if (OperatingSystem.IsWindows())
                    return Path.Combine($"windows-{arch}", "dxc.exe");
                if (OperatingSystem.IsMacOS())
                    return Path.Combine("osx", "bin", "dxc");
                return Path.Combine($"linux-{arch}", "bin", "dxc");
            }
        }

        /// <summary>The DXC this process uses.</summary>
        internal static string ExecutablePath => Executable.Value;

        private static string Locate()
        {
            var inTool = Path.Combine(AppContext.BaseDirectory, RelativeExecutable);
            if (File.Exists(inTool))
                return inTool;

            var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
            if (string.IsNullOrEmpty(packages))
                packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
            var inCache = Path.Combine(packages, "dotnet-mgcb", MgcbToolVersion, "tools", "net8.0", "any", RelativeExecutable);
            if (File.Exists(inCache))
                return inCache;

            throw new FileNotFoundException(
                $"DXC was found neither next to the application ({inTool}) nor in the dotnet-mgcb {MgcbToolVersion} tool " +
                $"package ({inCache}): run 'dotnet tool restore' in the repository.");
        }

        /// <summary>Runs DXC with <paramref name="arguments"/>; returns its exit code.</summary>
        public static int Run(string arguments, out string stdout, out string stderr)
        {
            var info = new ProcessStartInfo(Executable.Value, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {info.FileName}.");
            // Both streams are drained at once: a full pipe on one of them would block DXC.
            var errorTask = process.StandardError.ReadToEndAsync();
            stdout = process.StandardOutput.ReadToEnd();
            stderr = errorTask.GetAwaiter().GetResult();
            process.WaitForExit();
            return process.ExitCode;
        }
    }
}
