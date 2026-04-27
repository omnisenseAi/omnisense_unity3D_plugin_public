using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Omnisense
{
    public class AIOrchestrator
    {
        private static AIOrchestrator _instance;
        public static AIOrchestrator Instance => _instance ??= new AIOrchestrator();

        [Serializable]
        public class ChatMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private class OpenAIRequest
        {
            public string model;
            public List<ChatMessage> messages;
        }

        [Serializable]
        private class OpenAIResponse
        {
            public List<Choice> choices;
        }

        [Serializable]
        private class Choice
        {
            public ChatMessage message;
        }

        private List<ChatMessage> _history = new List<ChatMessage>();

        private const string SYSTEM_PROMPT = @"You are Omnisense AI, an autonomous developer agent embedded directly in the Unity Editor.
You operate using the ReAct (Reason, Act, Observe) loop. 
Always output a <thought> block first to reason about your plan, followed by an action.

You have access to the following MCP tools. To use a tool, output exactly this format:
```mcp_json
{
    ""method"": ""TOOL_NAME"",
    ""params"": {
        ""key"": ""value""
    }
}
```

Available Tools:
1. project/list_directory (params: ""path"") - Lists files.
2. project/write_file (params: ""path"", ""content"") - Creates/overwrites a file.
3. scene/instantiate_node (params: ""type"", ""name"") - Creates a GameObject.

Wait for the [Observation] from the system before proceeding.";

        public void ProcessPrompt(string prompt, string model, Action<string> onComplete)
        {
            if (_history.Count == 0)
            {
                _history.Add(new ChatMessage { role = "system", content = SYSTEM_PROMPT });
            }
            _history.Add(new ChatMessage { role = "user", content = prompt });
            ExecuteRequest(model, onComplete);
        }

        private void ExecuteRequest(string model, Action<string> onComplete)
        {
            string apiKey = GetApiKey(model);
            if (string.IsNullOrEmpty(apiKey))
            {
                onComplete?.Invoke("Error: API Key missing. Please set it in the Settings tab.");
                return;
            }

            // Prepare request based on provider
            if (model.StartsWith("gpt") || model.StartsWith("o3"))
            {
                CallOpenAI(apiKey, model, onComplete);
            }
            else if (model.StartsWith("claude"))
            {
                CallAnthropic(apiKey, model, onComplete);
            }
            else if (model.StartsWith("gemini"))
            {
                CallGemini(apiKey, model, onComplete);
            }
            else if (model.StartsWith("grok"))
            {
                CallGrok(apiKey, model, onComplete);
            }
            else
            {
                onComplete?.Invoke($"Error: Unsupported model {model}");
            }
        }

        private void CallOpenAI(string apiKey, string model, Action<string> onComplete)
        {
            var requestData = new OpenAIRequest { model = model, messages = _history };
            string json = JsonUtility.ToJson(requestData);

            var webRequest = new UnityWebRequest("https://api.openai.com/v1/chat/completions", "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("Authorization", "Bearer " + apiKey);

            var operation = webRequest.SendWebRequest();
            operation.completed += (op) =>
            {
                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    string responseText = ExtractContent(webRequest.downloadHandler.text);
                    HandleResponse(responseText, model, onComplete);
                }
                else
                {
                    onComplete?.Invoke($"API Error: {webRequest.error}\n{webRequest.downloadHandler.text}");
                }
                webRequest.Dispose();
            };
        }

        // Placeholder for other providers - similar implementation to CallOpenAI
        private void CallAnthropic(string key, string model, Action<string> cb) => cb?.Invoke("Anthropic integration coming in Phase 3.1");
        private void CallGemini(string key, string model, Action<string> cb) => cb?.Invoke("Gemini integration coming in Phase 3.1");
        private void CallGrok(string key, string model, Action<string> cb) => cb?.Invoke("Grok integration coming in Phase 3.1");

        private void HandleResponse(string response, string model, Action<string> onComplete)
        {
            _history.Add(new ChatMessage { role = "assistant", content = response });

            string toolJson = ExtractToolCall(response);
            if (!string.IsNullOrEmpty(toolJson))
            {
                // Send what we have so far to UI
                onComplete?.Invoke(response);

                try
                {
                    // Parse tool JSON
                    var toolCall = JsonUtility.FromJson<MCPToolRequest>(toolJson);
                    MCPToolRegistry.ToolResult result = null;

                    if (toolCall.method == "project/write_file")
                    {
                        string path = ExtractParam(toolJson, "path");
                        string content = ExtractContentRaw(toolJson, "content");
                        result = MCPToolRegistry.WriteFile(path, content);
                    }
                    else if (toolCall.method == "project/list_directory")
                    {
                        string path = ExtractParam(toolJson, "path");
                        result = MCPToolRegistry.ListDirectory(path);
                    }
                    else if (toolCall.method == "scene/instantiate_node")
                    {
                        string type = ExtractParam(toolJson, "type");
                        string name = ExtractParam(toolJson, "name");
                        
                        var tcs = new System.Threading.Tasks.TaskCompletionSource<MCPToolRegistry.ToolResult>();
                        EditorApplication.delayCall += () => {
                            tcs.SetResult(MCPToolRegistry.InstantiateNode(type, name));
                        };
                        result = tcs.Task.Result;
                    }
                    else
                    {
                        result = new MCPToolRegistry.ToolResult { success = false, error = "Unknown tool" };
                    }

                    string observation = result.success ? result.observation : $"Error: {result.error}";
                    _history.Add(new ChatMessage { role = "user", content = $"[Observation]\n{observation}" });
                    
                    // Recursive ReAct Loop
                    ExecuteRequest(model, onComplete);
                }
                catch (Exception e)
                {
                    _history.Add(new ChatMessage { role = "user", content = $"[Observation]\nError parsing tool call: {e.Message}" });
                    ExecuteRequest(model, onComplete);
                }
            }
            else
            {
                onComplete?.Invoke(response);
            }
        }

        [Serializable]
        private class MCPToolRequest { public string method; }

        private string GetApiKey(string model)
        {
            if (model.Contains("gpt") || model.Contains("o3")) return EditorPrefs.GetString("Omnisense_OpenAI_Key", "");
            if (model.Contains("claude")) return EditorPrefs.GetString("Omnisense_Anthropic_Key", "");
            if (model.Contains("gemini")) return EditorPrefs.GetString("Omnisense_Gemini_Key", "");
            if (model.Contains("grok")) return EditorPrefs.GetString("Omnisense_Grok_Key", "");
            return "";
        }

        private string ExtractContent(string rawJson)
        {
            try 
            {
                var response = JsonUtility.FromJson<OpenAIResponse>(rawJson);
                if (response != null && response.choices != null && response.choices.Count > 0)
                {
                    return response.choices[0].message.content;
                }
            }
            catch { }
            
            return "Error parsing response content: " + rawJson;
        }

        private string ExtractThought(string content)
        {
            var match = Regex.Match(content, "<thought>(.*?)</thought>", RegexOptions.Singleline);
            return match.Success ? match.Groups[1].Value : null;
        }

        private string ExtractToolCall(string content)
        {
            var match = Regex.Match(content, "```mcp_json\n(.*?)\n```", RegexOptions.Singleline);
            return match.Success ? match.Groups[1].Value : null;
        }
        private string ExtractContentRaw(string json, string key)
        {
            // Specifically for multiline content like scripts
            string search = $"\"{key}\": \"";
            int start = json.IndexOf(search);
            if (start == -1) return null;
            
            start += search.Length;
            int end = json.LastIndexOf("\"");
            if (end <= start) return null;
            
            return json.Substring(start, end - start).Replace("\\n", "\n").Replace("\\\"", "\"");
        }

        private string ExtractParam(string json, string key)
        {
            string search = $"\"{key}\":\"";
            int start = json.IndexOf(search);
            if (start == -1) 
            {
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
