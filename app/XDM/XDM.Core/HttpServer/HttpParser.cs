using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
#if !NET5_0_OR_GREATER
using XDM.Compatibility;
#endif

namespace XDM.Core.HttpServer
{
    internal static class HttpParser
    {
        internal const long MaxRequestBodyLength = 1024 * 1024;

        public static string ParseRequestStatusLine(string statusLine)
        {
            ParseRequestLine(statusLine, out _, out var path);
            return path;
        }

        internal static void ParseRequestLine(string requestLine, out string method, out string path)
        {
            var parts = requestLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || !parts[2].StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase)
                || parts[0].Length == 0 || parts[0].IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0
                || !parts[1].StartsWith("/", StringComparison.Ordinal))
            {
                throw new HttpRequestException(400, "Bad Request", "Invalid HTTP request line");
            }
            method = parts[0].ToUpperInvariant();
            path = parts[1];
        }

        internal static void ParseHeader(string headerLine, out string key, out string value)
        {
            var index = headerLine.IndexOf(":");
            if (index > 0)
            {
                key = headerLine.Substring(0, index).Trim();
                value = headerLine.Substring(index + 1).Trim();
                return;
            }
            throw new HttpRequestException(400, "Bad Request", "Invalid HTTP header");
        }

        internal static long ParseContentLength(Dictionary<string, List<string>> headers)
        {
            var values = headers.GetValueOrDefault("Content-Length");
            if (values == null) return -1;
            if (values.Count != 1 || !Int64.TryParse(values[0], out var length) || length < 0)
            {
                throw new HttpRequestException(400, "Bad Request", "Invalid Content-Length");
            }
            if (length > MaxRequestBodyLength)
            {
                throw new HttpRequestException(413, "Payload Too Large", "Request body is too large");
            }
            return length;
        }

        private static bool ShouldKeepAlive(Dictionary<string, List<string>> headers)
        {
            var value = headers.GetValueOrDefault("Connection")?[0] ?? "close";
            if (value.Equals("keep-alive", StringComparison.InvariantCultureIgnoreCase))
            {
                return true;
            }
            return false;
        }

        internal static RequestContext ParseContext(TcpClient tcp)
        {
            string method = "GET";
            string path = "/";
            Dictionary<string, List<string>> headers = new(StringComparer.OrdinalIgnoreCase);
            byte[]? body = null;
            var io = tcp.GetStream();
            var first = true;
            //var lines = LineReader.ReadLines(io);
            foreach (var line in LineReader.ReadLines(io))
            {
                if (first)
                {
                    ParseRequestLine(line, out method, out path);
                    first = false;
                    continue;
                }
                ParseHeader(line, out string headerName, out string headerValue);
                var values = headers.GetValueOrDefault(headerName, new List<string>());
                values.Add(headerValue);
                headers[headerName] = values;
            }
            var contentLength = ParseContentLength(headers);
            if (contentLength > 0)
            {
                body = new byte[contentLength];
                using var ms = new MemoryStream(body);
                io.CopyTo(ms, contentLength);
                if (ms.Length != contentLength)
                {
                    throw new HttpRequestException(400, "Bad Request", "Incomplete request body");
                }
                ms.Close();
            }
            return new RequestContext(method, path, headers, body, tcp, ShouldKeepAlive(headers));
        }

        internal static void CopyTo(this Stream stream, Stream destination, long limit = Int64.MaxValue)
        {
            byte[] buffer = new byte[8192];
            int read;
            while (limit > 0 && (read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, limit))) != 0)
            {
                destination.Write(buffer, 0, read);
                limit -= read;
            }
        }
    }

    internal sealed class HttpRequestException : IOException
    {
        internal HttpRequestException(int statusCode, string statusMessage, string message) : base(message)
        {
            StatusCode = statusCode;
            StatusMessage = statusMessage;
        }

        internal int StatusCode { get; }
        internal string StatusMessage { get; }
    }
}
