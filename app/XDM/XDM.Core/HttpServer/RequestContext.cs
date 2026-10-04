using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
#if !NET5_0_OR_GREATER
using XDM.Compatibility;
#endif

namespace XDM.Core.HttpServer
{
    public class RequestContext
    {
        private TcpClient tcp;
        public string RequestMethod { get; }
        public string RequestPath { get; }
        public byte[]? RequestBody { get; }
        public Dictionary<string, List<string>> RequestHeaders { get; }
        public byte[]? ResponseBody { set; get; }
        public Dictionary<string, List<string>> ResponseHeaders { set; get; }
        public ResponseStatus ResponseStatus { set; get; }
        public bool KeepAlive { get; private set; }

        internal RequestContext(string method, string path, Dictionary<string, List<string>> headers, byte[]? body, TcpClient tcp, bool keepAlive)
        {
            this.RequestMethod = method;
            this.RequestPath = path;
            this.RequestHeaders = headers;
            this.RequestBody = body;
            this.tcp = tcp;
            this.ResponseHeaders = new();
            this.ResponseStatus = new ResponseStatus { StatusCode = 200, StatusMessage = "OK" };
            this.KeepAlive = keepAlive;
        }

        internal System.IO.Stream UpgradeWebSocket(string key)
        {
            KeepAlive = false;
            tcp.SendTimeout = 5000;
            using var sha = System.Security.Cryptography.SHA1.Create();
            var accept = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(
                key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            var bytes = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Protocol: xdm-controller\r\n" +
                "Sec-WebSocket-Accept: " + accept + "\r\n\r\n");
            var stream = tcp.GetStream();
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
            return stream;
        }
        public void SendResponse()
        {
            var io = this.tcp.GetStream();
            var responseBuffer = new StringBuilder();
            responseBuffer.Append($"HTTP/1.0 {this.ResponseStatus.StatusCode} {this.ResponseStatus.StatusMessage}\r\n");
            foreach (var headerName in ResponseHeaders.Keys)
            {
                if (headerName.Equals("content-length", StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }
                foreach (var value in ResponseHeaders[headerName])
                {
                    responseBuffer.Append($"{headerName}: {value}\r\n");
                }
            }
            responseBuffer.Append($"Connection: {(KeepAlive ? "keep-alive" : "close")}\r\n");
            if (ResponseBody != null && ResponseBody.Length > 0)
            {
                responseBuffer.Append($"Content-Length: {ResponseBody.Length}\r\n");
            }
            responseBuffer.Append("\r\n");
            var bytes = Encoding.UTF8.GetBytes(responseBuffer.ToString());
            io.Write(bytes, 0, bytes.Length);
            if (ResponseBody != null && ResponseBody.Length > 0)
            {
                io.Write(ResponseBody, 0, ResponseBody.Length);
            }
            io.Flush();
        }

        public void AddResponseHeader(string name, string value)
        {
            var values = this.ResponseHeaders.GetValueOrDefault(name, new List<string>(1));
            values.Add(value);
            this.ResponseHeaders[name] = values;
        }
    }
}
