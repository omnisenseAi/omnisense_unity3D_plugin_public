using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Omnisense
{
    [InitializeOnLoad]
    public static class MCPServer
    {
        private static HttpListener _listener;
        private static Thread _serverThread;
        private static bool _isRunning = false;

        static MCPServer()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += StopServer;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Stop server before domain reload
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
            {
                StopServer();
            }
        }
        private const string Port = "3000";

        [Serializable]
        private class JsonRpcRequest
        {
            public string jsonrpc;
            public string method;
            public string id;
            // Note: JsonUtility doesn't handle 'params' well if it's dynamic.
            // We will parse the raw string for params if needed.
        }

        [Serializable]
        private class JsonRpcResponse
        {
            public string jsonrpc = "2.0";
            public string result;
            public string error;
            public string id;
        }

        public static void StartServer()
        {
            if (_isRunning) return;

            int currentPort = 3000;
            int retriesOnSamePort = 0;
            bool success = false;

            while (!success && currentPort < 3010)
            {
                try
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://localhost:{currentPort}/");
                    _listener.Start();
                    
                    _isRunning = true;
                    _serverThread = new Thread(Listen);
                    _serverThread.IsBackground = true;
                    _serverThread.Start();
                    
                    Debug.Log($"[Omnisense] MCPServer started on http://localhost:{currentPort}/");
                    success = true;
                }
                catch (HttpListenerException e)
                {
                    // Error 10048 (WSAEADDRINUSE) or 32/183 typically mean the port is busy
                    if (retriesOnSamePort < 3)
                    {
                        // The OS often holds the port in a TIME_WAIT state for a second during a Domain Reload (Play mode)
                        _listener?.Close();
                        Thread.Sleep(500); // Wait half a second for the OS to release the socket
                        retriesOnSamePort++;
                    }
                    else
                    {
                        Debug.LogWarning($"[Omnisense] Port {currentPort} firmly in use (Code: {e.NativeErrorCode}), trying {currentPort + 1}...");
                        _listener?.Close();
                        currentPort++;
                        retriesOnSamePort = 0; // Reset retries for the new port
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Omnisense] Failed to start MCPServer on {currentPort}: {e.Message}");
                    break;
                }
            }
        }

        public static void StopServer()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _listener?.Stop();
            _listener?.Close();
            _serverThread?.Join(500);
            
            Debug.Log("[Omnisense] MCPServer stopped.");
        }

        private static void Listen()
        {
            while (_isRunning && _listener.IsListening)
            {
                try
                {
                    var context = _listener.GetContext();
                    Task.Run(() => ProcessRequest(context));
                }
                catch (HttpListenerException) { }
                catch (Exception e)
                {
                    Debug.LogError($"[Omnisense] Request processing error: {e.Message}");
                }
            }
        }

        private static void ProcessRequest(HttpListenerContext context)
        {
            string responseJson = "";
            string requestId = "null";

            try
            {
                // Read request body
                string requestBody;
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                {
                    requestBody = reader.ReadToEnd();
                }

                var request = JsonUtility.FromJson<JsonRpcRequest>(requestBody);
                requestId = request.id;

                MCPToolRegistry.ToolResult result = null;

                // Routing
                if (request.method == "project/list_directory")
                {
                    // Simple param extraction for demo (JsonUtility limitation)
                    string path = ExtractParam(requestBody, "path") ?? "Assets";
                    result = MCPToolRegistry.ListDirectory(path);
                }
                else if (request.method == "scene/instantiate_node")
                {
                    string type = ExtractParam(requestBody, "type") ?? "Cube";
                    string name = ExtractParam(requestBody, "name") ?? "OmniObject";

                    // Synchronize with main thread
                    var tcs = new TaskCompletionSource<MCPToolRegistry.ToolResult>();
                    EditorApplication.delayCall += () =>
                    {
                        try {
                            tcs.SetResult(MCPToolRegistry.InstantiateNode(type, name));
                        } catch (Exception e) {
                            tcs.SetException(e);
                        }
                    };
                    result = tcs.Task.Result;
                }
                else
                {
                    result = new MCPToolRegistry.ToolResult { success = false, error = $"Method not found: {request.method}" };
                }

                var response = new JsonRpcResponse 
                { 
                    id = requestId,
                    result = result.success ? result.observation : null,
                    error = result.success ? null : result.error
                };
                responseJson = JsonUtility.ToJson(response);
            }
            catch (Exception e)
            {
                var errorResponse = new JsonRpcResponse { id = requestId, error = e.Message };
                responseJson = JsonUtility.ToJson(errorResponse);
            }

            // Send Response
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);
            context.Response.ContentLength64 = buffer.Length;
            context.Response.ContentType = "application/json";
            
            try {
                using (var output = context.Response.OutputStream)
                {
                    output.Write(buffer, 0, buffer.Length);
                }
            } catch (Exception) { /* Connection might be closed */ }
        }

        private static string ExtractParam(string json, string key)
        {
            // Simple string-based extraction for JsonUtility fallback
            string search = $"\"{key}\":\"";
            int start = json.IndexOf(search);
            if (start == -1) 
            {
                // Try without quotes on value (for numbers or bools if needed, though here we expect strings)
                search = $"\"{key}\": \"";
                start = json.IndexOf(search);
                if (start == -1) return null;
            }
            
            start += search.Length;
            int end = json.IndexOf("\"", start);
            if (end == -1) return null;
            
            return json.Substring(start, end - start);
        }
    }
}
