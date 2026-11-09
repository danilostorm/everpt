namespace PristonTale.Tests;

// A failed assertion: its own type, so an expected refusal is never satisfied by an assertion failing inside it.
internal sealed class AssertionException(string message) : Exception(message);

internal static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new AssertionException(message);
        }
    }

    public static void Near(float expected, float actual, float tolerance = 0.0001f)
    {
        True(float.IsFinite(actual) && MathF.Abs(expected - actual) <= tolerance, $"expected {expected}, got {actual}");
    }

    public static TException Throws<TException>(Action operation, string message) where TException : Exception
    {
        try
        {
            operation();
        }
        catch (Exception ex) when (ex is not AssertionException)
        {
            return Expected<TException>(ex, message);
        }

        throw new AssertionException($"{message}: nothing was thrown");
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> operation, string message) where TException : Exception
    {
        try
        {
            await operation();
        }
        catch (Exception ex) when (ex is not AssertionException)
        {
            return Expected<TException>(ex, message);
        }

        throw new AssertionException($"{message}: nothing was thrown");
    }

    private static TException Expected<TException>(Exception thrown, string message) where TException : Exception =>
        thrown as TException ?? throw new AssertionException($"{message}: {thrown.GetType().Name} instead of {typeof(TException).Name}: {thrown.Message}");
}
