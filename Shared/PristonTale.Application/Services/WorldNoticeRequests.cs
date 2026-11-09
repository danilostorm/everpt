using PristonTale.Application.Chat;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;

namespace PristonTale.Application.Services;

// A request that runs on the world and answers only lines, which reach their players through the chat. A full command
// queue drops the request, as it drops a spoken line.
internal static class WorldNoticeRequests
{
    public static async Task RunAsync(ISessionRegistry sessions, ChatDirectory chat, string sessionToken, Func<PlayerSession, Task<WorldNotice[]>> action)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return;
        }

        try
        {
            chat.Deliver(await action(session));
        }
        catch (WorldBusyException)
        {
        }
    }
}
