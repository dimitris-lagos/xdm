using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using XDM.Core.HttpServer;
using XDM.Core.DataAccess;
using XDM.Core.Util;

namespace XDM.Core.BrowserMonitoring
{
    internal sealed class DownloadControllerApi
    {
        private const string TokenHeader = "X-XDM-Controller-Token";
        private const string ClientHeader = "X-XDM-Controller-Client";
        private readonly string sessionToken = CreateSessionToken();

        internal bool TryHandle(RequestContext context)
        {
            if (!context.RequestPath.StartsWith("/controller/v1/", StringComparison.Ordinal)) return false;

            var origin = GetHeader(context, "Origin");
            if (context.RequestMethod == "OPTIONS")
            {
                if (!DownloadControllerProtocol.IsOriginAllowed(origin))
                {
                    SendError(context, 403, "Forbidden", "Extension origin is not allowed");
                    return true;
                }
                AddCorsHeaders(context);
                HandlePreflight(context);
                return true;
            }

            if (!DownloadControllerProtocol.IsExtensionRequestAllowed(origin,
                GetHeader(context, ClientHeader), GetHeader(context, "Sec-Fetch-Site"),
                GetHeader(context, "Sec-Fetch-Mode")))
            {
                SendError(context, 403, "Forbidden", "Request is not from the XDM extension");
                return true;
            }

            AddCorsHeaders(context);
            if (context.RequestPath == "/controller/v1/session")
            {
                if (!RequireMethod(context, "GET")) return true;
                SendJson(context, 200, "OK", new { token = sessionToken, apiVersion = 1 });
                return true;
            }

            if (!DownloadControllerProtocol.TokenMatches(sessionToken, GetHeader(context, TokenHeader)))
            {
                SendError(context, 401, "Unauthorized", "Missing or invalid controller token");
                return true;
            }

            if (context.RequestPath == "/controller/v1/downloads")
            {
                if (!RequireMethod(context, "GET")) return true;
                var downloads = CreateSnapshot();
                SendJson(context, 200, "OK", new { downloads });
                return true;
            }

            if (!DownloadControllerProtocol.TryParseActionPath(context.RequestPath, out var id, out var action))
            {
                SendError(context, 404, "Not Found", "Unknown controller endpoint");
                return true;
            }
            if (!RequireMethod(context, "POST") || !RequireJsonBody(context)) return true;

            var result = PerformAction(id, action);
            if (result.StatusCode != 200)
            {
                SendError(context, result.StatusCode, result.StatusMessage, result.Error!);
                return true;
            }
            SendJson(context, 200, "OK", new { ok = true });
            return true;
        }

        private static List<ControllerDownloadDto> CreateSnapshot()
        {
            var result = new List<ControllerDownloadDto>();
            if (!AppDB.Instance.Downloads.LoadDownloads(out var inProgress, out var finished))
            {
                throw new InvalidOperationException("Could not read the download store");
            }

            foreach (var item in inProgress.OrderByDescending(item => item.DateAdded))
            {
                var state = item.Status.ToString();
                DownloadControllerRuntimeState.TryGet(item.Id, out var metrics);
                result.Add(new ControllerDownloadDto
                {
                    id = item.Id,
                    name = item.Name,
                    dateAdded = item.DateAdded,
                    progress = item.Progress,
                    state = state,
                    totalBytes = item.Size > 0 ? item.Size : (long?)null,
                    downloadedBytes = item.Size > 0 ? item.Size * item.Progress / 100 : (long?)null,
                    speed = metrics?.Speed,
                    eta = metrics?.Eta,
                    actions = DownloadControllerProtocol.ActionsForState(state)
                });
            }
            foreach (var item in finished.OrderByDescending(item => item.DateAdded))
            {
                result.Add(new ControllerDownloadDto
                {
                    id = item.Id,
                    name = item.Name,
                    dateAdded = item.DateAdded,
                    progress = 100,
                    state = "Finished",
                    totalBytes = item.Size > 0 ? item.Size : (long?)null,
                    downloadedBytes = item.Size > 0 ? item.Size : (long?)null,
                    actions = DownloadControllerProtocol.ActionsForState("Finished")
                });
            }
            return result;
        }

        private static ControllerActionResult PerformAction(string id, string action)
        {
            var entry = AppDB.Instance.Downloads.GetDownloadById(id);
            if (entry == null) return ControllerActionResult.Fail(404, "Not Found", "Unknown download ID");

            var state = entry is InProgressDownloadItem active ? active.Status.ToString() : "Finished";
            if (!DownloadControllerProtocol.IsActionAllowed(state, action))
            {
                return ControllerActionResult.Fail(409, "Conflict", "Action is not valid for the current download state");
            }

            // Core commands conventionally run on the UI dispatcher, but the HTTP
            // request never reads WPF state and never blocks waiting for that thread.
            ApplicationContext.Application.RunOnUiThread(() =>
            {
                switch (action)
                {
                    case "pause":
                    case "stop":
                        ApplicationContext.CoreService.StopDownloads(new[] { id });
                        break;
                    case "resume":
                        ApplicationContext.CoreService.ResumeDownload(new Dictionary<string, DownloadItemBase> { [id] = entry });
                        break;
                    case "restart":
                        ApplicationContext.CoreService.RestartDownload(entry);
                        break;
                    case "open":
                        PlatformHelper.OpenFile(System.IO.Path.Combine(entry.TargetDir, entry.Name));
                        break;
                    case "open-folder":
                        PlatformHelper.OpenFolder(entry.TargetDir, entry.Name);
                        break;
                }
            });
            return ControllerActionResult.Success();
        }

        private static bool RequireMethod(RequestContext context, string method)
        {
            if (context.RequestMethod == method) return true;
            context.AddResponseHeader("Allow", method + ", OPTIONS");
            SendError(context, 405, "Method Not Allowed", "HTTP method is not allowed");
            return false;
        }

        private static bool RequireJsonBody(RequestContext context)
        {
            var contentType = GetHeader(context, "Content-Type") ?? String.Empty;
            if (!DownloadControllerProtocol.IsJsonContentType(contentType))
            {
                SendError(context, 415, "Unsupported Media Type", "Content-Type must be application/json");
                return false;
            }
            if (context.RequestBody == null || context.RequestBody.Length == 0
                || context.RequestBody.Length > DownloadControllerProtocol.MaxActionBodyLength)
            {
                SendError(context, 400, "Bad Request", "A small JSON object body is required");
                return false;
            }
            try
            {
                var body = JsonConvert.DeserializeObject<Dictionary<string, object>>(Encoding.UTF8.GetString(context.RequestBody));
                if (body == null || body.Count != 0) throw new JsonException();
                return true;
            }
            catch (JsonException)
            {
                SendError(context, 400, "Bad Request", "Request body must be an empty JSON object");
                return false;
            }
        }

        private static void HandlePreflight(RequestContext context)
        {
            var requestedMethod = GetHeader(context, "Access-Control-Request-Method");
            if (requestedMethod != "GET" && requestedMethod != "POST")
            {
                SendError(context, 403, "Forbidden", "CORS method is not allowed");
                return;
            }
            var requestedHeaders = (GetHeader(context, "Access-Control-Request-Headers") ?? String.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim());
            if (requestedHeaders.Any(value => !value.Equals("content-type", StringComparison.OrdinalIgnoreCase)
                && !value.Equals(TokenHeader, StringComparison.OrdinalIgnoreCase)
                && !value.Equals(ClientHeader, StringComparison.OrdinalIgnoreCase)))
            {
                SendError(context, 403, "Forbidden", "CORS header is not allowed");
                return;
            }
            context.ResponseStatus = new ResponseStatus { StatusCode = 204, StatusMessage = "No Content" };
            context.AddResponseHeader("Access-Control-Max-Age", "600");
            context.SendResponse();
        }

        private static void AddCorsHeaders(RequestContext context)
        {
            context.AddResponseHeader("Access-Control-Allow-Origin", DownloadControllerProtocol.AllowedOrigin);
            context.AddResponseHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            context.AddResponseHeader("Access-Control-Allow-Headers", "Content-Type, " + TokenHeader + ", " + ClientHeader);
            // Chromium-family browsers preflight extension requests to loopback as
            // private-network requests. This is still restricted to AllowedOrigin.
            context.AddResponseHeader("Access-Control-Allow-Private-Network", "true");
            context.AddResponseHeader("Vary", "Origin, Access-Control-Request-Private-Network");
        }

        private static string? GetHeader(RequestContext context, string name)
        {
            return context.RequestHeaders.TryGetValue(name, out var values) ? values.FirstOrDefault() : null;
        }

        private static string CreateSessionToken()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static void SendError(RequestContext context, int statusCode, string statusMessage, string error)
        {
            SendJson(context, statusCode, statusMessage, new { error });
        }

        internal static void SendInternalError(RequestContext context)
        {
            if (DownloadControllerProtocol.IsOriginAllowed(GetHeader(context, "Origin"))) AddCorsHeaders(context);
            SendError(context, 500, "Internal Server Error", "Controller request failed");
        }

        private static void SendJson(RequestContext context, int statusCode, string statusMessage, object value)
        {
            context.ResponseStatus = new ResponseStatus { StatusCode = statusCode, StatusMessage = statusMessage };
            context.AddResponseHeader("Content-Type", "application/json; charset=utf-8");
            context.AddResponseHeader("Cache-Control", "no-store");
            context.ResponseBody = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));
            context.SendResponse();
        }
    }

    internal sealed class ControllerActionResult
    {
        internal int StatusCode { get; private set; }
        internal string StatusMessage { get; private set; } = String.Empty;
        internal string? Error { get; private set; }
        internal static ControllerActionResult Success() => new ControllerActionResult { StatusCode = 200, StatusMessage = "OK" };
        internal static ControllerActionResult Fail(int code, string message, string error) =>
            new ControllerActionResult { StatusCode = code, StatusMessage = message, Error = error };
    }
}
