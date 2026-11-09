<%@ WebHandler Language="C#" Class="EtervaleBridge" %>
// ws.ashx - WebSocket bridge between the browser game and the game server on this computer.
//
// Browsers can neither open TCP connections nor run TLS themselves, so each game connection is a WebSocket to this handler
// (wss from the player), and this handler holds the TLS connection the game server requires. The bytes are the game's frames.
// The port is checked against web.config appSettings "EtervaleGamePorts"; only pages of this site may connect.
//
// Needs the IIS features "WebSocket Protocol" and "ASP.NET 4.8".
using System;
using System.Configuration;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.WebSockets;

public class EtervaleBridge : IHttpHandler
{
	public bool IsReusable { get { return true; } }

	public void ProcessRequest( HttpContext context )
	{
		if ( !context.IsWebSocketRequest ) {
			context.Response.StatusCode = 400;
			context.Response.Write( "Etervale game server bridge: WebSocket connections only." );
			return;
		}
		int port;
		if ( !int.TryParse( context.Request.QueryString["port"], out port ) || !PortAllowed( port ) || !SameSite( context.Request ) ) {
			context.Response.StatusCode = 403;
			return;
		}
		context.AcceptWebSocketRequest( ws => Bridge( ws.WebSocket, port ) );
	}

	static bool PortAllowed( int port )
	{
		string list = ConfigurationManager.AppSettings["EtervaleGamePorts"] ?? "10009";
		foreach ( string p in list.Split( ',' ) ) {
			int n;
			if ( int.TryParse( p.Trim(), out n ) && n==port ) return true;
		}
		return false;
	}

	static bool SameSite( HttpRequest request )
	{
		Uri origin;
		return Uri.TryCreate( request.Headers["Origin"], UriKind.Absolute, out origin )
			&& string.Equals( origin.Host, request.Url.Host, StringComparison.OrdinalIgnoreCase );
	}

	static async Task Bridge( WebSocket ws, int port )
	{
		using ( TcpClient tcp = new TcpClient() ) {
			SslStream tls = null;
			bool connected = true;
			try {
				tcp.NoDelay = true;
				await tcp.ConnectAsync( IPAddress.Loopback, port );
				// The server on this computer presents its own self-signed certificate; the connection never leaves the machine.
				tls = new SslStream( tcp.GetStream(), false, ( sender, certificate, chain, errors ) => true );
				await tls.AuthenticateAsClientAsync( "localhost", null, SslProtocols.Tls12, false );
			}
			catch ( Exception ) { connected = false; }
			if ( !connected ) {
				// The game server is not running: the game shows that it cannot connect.
				await CloseQuietly( ws, WebSocketCloseStatus.EndpointUnavailable, "game server not running" );
				return;
			}
			using ( tls ) {
				CancellationTokenSource stop = new CancellationTokenSource();

				Task browserToServer = Task.Run( async () => {
					byte[] buf = new byte[65536];
					try {
						while ( ws.State==WebSocketState.Open ) {
							WebSocketReceiveResult r = await ws.ReceiveAsync( new ArraySegment<byte>( buf ), stop.Token );
							if ( r.MessageType==WebSocketMessageType.Close ) break;
							if ( r.Count>0 ) {
								await tls.WriteAsync( buf, 0, r.Count, stop.Token );
								await tls.FlushAsync( stop.Token );
							}
						}
					}
					catch ( Exception ) { }
				} );

				Task serverToBrowser = Task.Run( async () => {
					byte[] buf = new byte[65536];
					try {
						while ( true ) {
							int n = await tls.ReadAsync( buf, 0, buf.Length, stop.Token );
							if ( n<=0 ) break;
							await ws.SendAsync( new ArraySegment<byte>( buf, 0, n ), WebSocketMessageType.Binary, true, stop.Token );
						}
					}
					catch ( Exception ) { }
				} );

				await Task.WhenAny( browserToServer, serverToBrowser );
				stop.Cancel();
				await CloseQuietly( ws, WebSocketCloseStatus.NormalClosure, "" );
			}
		}
	}

	static async Task CloseQuietly( WebSocket ws, WebSocketCloseStatus status, string reason )
	{
		try {
			if ( ws.State==WebSocketState.Open || ws.State==WebSocketState.CloseReceived )
				await ws.CloseAsync( status, reason, CancellationToken.None );
		}
		catch ( Exception ) { }
	}
}
