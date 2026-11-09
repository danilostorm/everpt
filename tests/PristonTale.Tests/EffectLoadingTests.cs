using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Tests;

internal static class EffectLoadingTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("required shader failure preserves cause and identifies content instead of rendering a fallback", FailureAsync);
    }

    private static Task FailureAsync()
    {
        using var content = new FailedContent();
        var log = new RecordingLog();
        const string asset = PristonShaderLibrary.SkinnedSurfaceAsset;
        try
        {
            PristonShaderLibrary.LoadRequired(content, asset, log);
        }
        catch (ContentLoadException exception)
        {
            Assert.True(ReferenceEquals(exception.InnerException, content.Failure), "original shader loading cause was lost");
            Assert.True(exception.Message.Contains(asset) && exception.Message.Contains(content.RootDirectory), "diagnostic omitted shader or content root");
            Assert.True(log.Errors == 1 && ReferenceEquals(log.Cause, content.Failure), "shader loading failure was not logged exactly once");
            return Task.CompletedTask;
        }

        throw new InvalidOperationException("missing shader silently returned a rendering fallback");
    }

    private sealed class FailedContent : ContentManager
    {
        public FailedContent() : base(new GameServiceContainer(), "missing-client-content")
        {
        }

        public ContentLoadException Failure { get; } = new("shader fixture unavailable");

        public override T Load<T>(string assetName)
        {
            throw Failure;
        }
    }

    private sealed class RecordingLog : IClientLog
    {
        public int Errors { get; private set; }
        public Exception? Cause { get; private set; }

        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
            Errors++;
            Cause = exception;
        }
    }
}
