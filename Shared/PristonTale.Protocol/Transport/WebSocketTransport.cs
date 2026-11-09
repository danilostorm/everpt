using System.Net.WebSockets;

namespace PristonTale.Protocol.Transport;

// The browser's connection: a page can neither open sockets nor run TLS itself, so it speaks the same frames over a
// WebSocket to the site, whose bridge holds the TLS connection to the game server.
public static class WebSocketTransport
{
    // The bridge's address, such as wss://example.com/ws; the browser build sets it from the page's own address.
    public static string BridgeAddress { get; set; } = string.Empty;

    public static async Task<FrameConnection> ConnectAsync(int port, FrameConnectionOptions? options, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"{BridgeAddress}?port={port}"), cancellationToken);
        return new FrameConnection(null, new WebSocketByteStream(socket), options);
    }

    // The frame code reads and writes a byte stream; message boundaries mean nothing here, as with TCP.
    private sealed class WebSocketByteStream(ClientWebSocket socket) : Stream
    {
        public override bool CanRead => true;

        public override bool CanWrite => true;

        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            return result.MessageType == WebSocketMessageType.Close ? 0 : result.Count;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await socket.SendAsync(buffer, WebSocketMessageType.Binary, true, cancellationToken);
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                socket.Abort();
                socket.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
