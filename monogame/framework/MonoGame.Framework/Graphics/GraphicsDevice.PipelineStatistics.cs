// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork: the driver's statistics of a pipeline (monogame/issues/012-pipeline-statistics.md).

using System;
using System.Collections.Generic;
using System.Text;
using MonoGame.Interop;

namespace Microsoft.Xna.Framework.Graphics
{
    /// <summary>One statistic the driver reports for one executable (a shader stage) of a pipeline.</summary>
    public readonly struct PipelineStatistic
    {
        /// <summary>Creates a statistic.</summary>
        public PipelineStatistic(string executable, string name, string value)
        {
            Executable = executable;
            Name = name;
            Value = value;
        }

        /// <summary>The executable's name as the driver gives it (e.g. "Fragment Shader").</summary>
        public string Executable { get; }

        /// <summary>The statistic (e.g. "Register Count").</summary>
        public string Name { get; }

        /// <summary>Its value, as the driver formats it.</summary>
        public string Value { get; }
    }

    public partial class GraphicsDevice
    {
        /// <summary>
        /// The driver's statistics (VK_KHR_pipeline_executable_properties: registers, instructions, spills and whatever
        /// else the driver reports) of the pipeline the last draw (<paramref name="compute"/> false) or the last dispatch
        /// (true) used, or null when they are off: they are on only when the environment variable
        /// <c>MG_PIPELINE_STATISTICS</c> is <c>1</c> at device creation and the driver has the extension. Empty before
        /// the first draw or dispatch. A measurement facility: it allocates and asks the driver at every call.
        /// </summary>
        public unsafe IReadOnlyList<PipelineStatistic> GetPipelineStatistics(bool compute)
        {
            var buffer = new byte[64 * 1024];
            int length;
            fixed (byte* p = buffer)
                length = MGG.GraphicsDevice_GetPipelineStatistics(Handle, compute ? 1 : 0, p, buffer.Length);
            if (length < 0)
                return null;
            var statistics = new List<PipelineStatistic>();
            foreach (var line in Encoding.UTF8.GetString(buffer, 0, length).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.Split('\t');
                if (fields.Length == 3)
                    statistics.Add(new PipelineStatistic(fields[0], fields[1], fields[2]));
            }
            return statistics;
        }
    }
}
