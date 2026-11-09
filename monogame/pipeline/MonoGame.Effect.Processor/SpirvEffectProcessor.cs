// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork: MonoGame's EffectProcessor (MonoGame.Framework.Content.Pipeline/Processors/EffectProcessor.cs, v3.8.6)
// over the fork's effect compiler (monogame/issues/007-mgcb-profiles.md). The content builder's own effect compiler is
// compiled into its MonoGame.Framework.Content.Pipeline assembly and cannot be extended, so the fork's runs as this
// processor, loaded by MGCB from Content.mgcb (/reference).

extern alias ContentPipeline;

using System.Text.RegularExpressions;
using ContentPipeline::Microsoft.Xna.Framework.Content.Pipeline;
using ContentPipeline::Microsoft.Xna.Framework.Content.Pipeline.Graphics;
using ContentPipeline::Microsoft.Xna.Framework.Content.Pipeline.Processors;

namespace MonoGame.Effect.Processor;

/// <summary>
/// Processes an effect into the MGFX 12 format of Etervale's MonoGame fork: SM 6 shaders compiled by DXC to SPIR-V for
/// the DesktopVK platform, with compute passes (<c>ComputeShader = compile cs_6_0 F();</c>), storage buffers and images,
/// and the SM 6.5 profiles.
/// </summary>
[ContentProcessor(DisplayName = "Effect - Etervale fork (SPIR-V, MGFX 12)")]
public class SpirvEffectProcessor : ContentProcessor<EffectContent, CompiledEffectContent>
{
    private static readonly Regex errorOrWarning = new(@"(.*)\((\d*,\d*(?>,\d*,\d*)?)\):\s*(.*)", RegexOptions.Compiled);

    /// <summary>
    /// The debug mode for compiling effects.
    /// </summary>
    /// <value>The debug mode to use when compiling effects.</value>
    public virtual EffectProcessorDebugMode DebugMode { get; set; } = EffectProcessorDebugMode.Auto;

    /// <summary>
    /// Define assignments for the effect.
    /// </summary>
    /// <value>A list of define assignments delimited by semicolons.</value>
    public virtual string Defines { get; set; } = "";

    /// <summary>
    /// Processes the string representation of the specified effect into the fork's binary format.
    /// </summary>
    /// <param name="input">The effect string to be processed.</param>
    /// <param name="context">Context for the specified processor.</param>
    /// <returns>The compiled effect.</returns>
    public override CompiledEffectContent Process(EffectContent input, ContentProcessorContext context)
    {
        if (input.Identity == null)
        {
            throw new InvalidContentException("Passed EffectContent input is invalid!");
        }

        if (context.TargetPlatform != TargetPlatform.DesktopVK)
        {
            throw new InvalidContentException($"The Etervale fork's effect processor builds for DesktopVK (Vulkan) only, not {context.TargetPlatform}.", input.Identity);
        }

        var options = EffectCompiler.VulkanOptions(input.Identity.SourceFilename, context.OutputFilename,
            DebugMode == EffectProcessorDebugMode.Debug, Defines);

        // Parse the MGFX file expanding includes, macros, and returning the techniques.
        ShaderResult shaderResult;
        try
        {
            shaderResult = EffectCompiler.Parse(options, new ContentPipelineEffectCompilerOutput(context));

            // Add the include dependencies so that if they change
            // it will trigger a rebuild of this effect.
            foreach (var dep in shaderResult.Dependencies)
                context.AddDependency(dep);
        }
        catch (InvalidContentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // TODO: Extract good line numbers from mgfx parser!
            throw new InvalidContentException(ex.Message, input.Identity, ex);
        }

        // Compile and write the effect.
        byte[] mgfx;
        var shaderErrorsAndWarnings = string.Empty;
        try
        {
            mgfx = EffectCompiler.Compile(shaderResult, options, out shaderErrorsAndWarnings);

            // If there were any additional output files we register
            // them so that the cleanup process can manage them.
            foreach (var outfile in shaderResult.AdditionalOutputFiles)
                context.AddOutputFile(outfile);
        }
        catch (ShaderCompilerException)
        {
            // This will log any warnings and errors and throw.
            ProcessErrorsAndWarnings(true, shaderErrorsAndWarnings, input.Identity, context);
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidContentException("Failed to compile the effect!", input.Identity, ex);
        }

        // Process any warning messages that the shader compiler might have produced.
        ProcessErrorsAndWarnings(false, shaderErrorsAndWarnings, input.Identity, context);

        return new CompiledEffectContent(mgfx);
    }

    private static void ProcessErrorsAndWarnings(bool buildFailed, string shaderErrorsAndWarnings, ContentIdentity inputIdentity, ContentProcessorContext context)
    {
        // Split the errors and warnings into individual lines.
        var errorsAndWarningArray = shaderErrorsAndWarnings.Split(["\n", "\r", Environment.NewLine], StringSplitOptions.RemoveEmptyEntries);
        ContentIdentity? identity = null;
        var allErrorsAndWarnings = new System.Text.StringBuilder();

        // Process all the lines.
        foreach (var errorOrWarningLine in errorsAndWarningArray)
        {
            var match = errorOrWarning.Match(errorOrWarningLine);
            if (!match.Success || match.Groups.Count != 4)
            {
                // Just log anything we don't recognize as a warning.
                if (buildFailed)
                    allErrorsAndWarnings.AppendLine(errorOrWarningLine);
                else
                    context.Logger.Log(LogLevel.Warning, $"{errorOrWarningLine}: {context.Logger.GetCurrentFilename(inputIdentity)}");

                continue;
            }

            var fileName = match.Groups[1].Value;
            var lineAndColumn = match.Groups[2].Value;
            var message = match.Groups[3].Value;

            // Try to ensure a good file name for the error message.
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = inputIdentity.SourceFilename;
            }
            else if (!File.Exists(fileName))
            {
                var folder = Path.GetDirectoryName(inputIdentity.SourceFilename) ?? "";
                fileName = Path.Combine(folder, fileName);
            }

            var newIdentity = new ContentIdentity(fileName, inputIdentity.SourceTool, lineAndColumn);

            // If we got an exception then we'll be throwing an exception
            // below, so just gather the lines to throw later.
            if (buildFailed)
            {
                if (identity == null)
                {
                    identity = newIdentity;
                    allErrorsAndWarnings.AppendLine(message);
                }
                else
                    allErrorsAndWarnings.AppendLine(errorOrWarningLine);
            }
            else
                context.Logger.Log(LogLevel.Warning, $"{message}: {context.Logger.GetCurrentFilename(newIdentity)}");
        }

        if (buildFailed)
        {
            throw new InvalidContentException(allErrorsAndWarnings.ToString(), identity ?? inputIdentity);
        }
    }

    private class ContentPipelineEffectCompilerOutput(ContentProcessorContext context) : IEffectCompilerOutput
    {
        public void WriteWarning(string file, int line, int column, string message)
            => context.Logger.Log(LogLevel.Warning, $"{message}: {context.Logger.GetCurrentFilename(CreateContentIdentity(file, line, column))}");

        public void WriteError(string file, int line, int column, string message)
            => throw new InvalidContentException(message, CreateContentIdentity(file, line, column));

        private static ContentIdentity CreateContentIdentity(string file, int line, int column) => new(file, null, line + "," + column);
    }
}
