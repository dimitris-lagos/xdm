using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using TraceLog;

namespace XDM.Core.HttpServer
{
    public class NanoServer
    {
        private readonly TcpListener listener;
        public event EventHandler<RequestContextEventArgs>? RequestReceived;

        public NanoServer(int port) : this(IPAddress.Any, port) { }

        public NanoServer(IPAddress host, int port)
        {
            this.listener = new TcpListener(host, port);
        }

        public void Start()
        {
            listener.Start();
            while (true)
            {
                var tcp = listener.AcceptTcpClient();
                ProcessRequest(tcp);
            }
        }

        public void Stop()
        {
            try
            {
                this.listener.Stop();
            }
            catch { }
        }

        private void ProcessRequest(TcpClient tcp)
        {
            new Thread(() =>
            {
                try
                {
                    while (true)
                    {
                        var ctx = HttpParser.ParseContext(tcp);
                        this.RequestReceived?.Invoke(this, new RequestContextEventArgs(ctx));
                        if (!ctx.KeepAlive)
                        {
                            break;
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    SendError(tcp, ex.StatusCode, ex.StatusMessage, ex.Message);
                    Log.Debug(ex, ex.Message);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, ex.Message);
                }
                finally
                {
                    try { tcp.Close(); } catch { }
                }
            }).Start();
        }

        private static void SendError(TcpClient tcp, int statusCode, string statusMessage, string message)
        {
            try
            {
                var body = Encoding.UTF8.GetBytes("{\"error\":\"" + message.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}");
                var header = Encoding.ASCII.GetBytes($"HTTP/1.0 {statusCode} {statusMessage}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                var stream = tcp.GetStream();
                stream.Write(header, 0, header.Length);
                stream.Write(body, 0, body.Length);
                stream.Flush();
            }
            catch { }
        }
    }
}
