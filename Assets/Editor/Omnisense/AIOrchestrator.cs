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
        private int _turnToolCount = 0;
        private bool _isReflecting = false;

        private const string SYSTEM_PROMPT = @"You are the Omnisense Senior Unity Architect, an elite autonomous developer agent. 
Your goal is not just to execute commands, but to deliver FULLY FUNCTIONAL features.

### MANDATE: Second-Order Thinking
When a user makes a request, do not just fulfill the explicit text. Identify and resolve all implicit technical dependencies:
1. **Component Dependencies**: If you write a script that uses `GetComponent<Rigidbody>()`, you MUST autonomously attach a Rigidbody to the object.
2. **Scene Wiring**: If you add a script with a serialized field (like `public Transform target`), you MUST use `scene/set_component_property` to find a logical target in the scene and wire it up.
3. **Environment Validation**: After writing code, you MUST use `editor/read_console` to check for compilation errors. If errors exist, you MUST fix them immediately without being asked.

### OPERATIONAL LOOP: ReAct + Plan
1. **Plan**: Output a `<thought>` block first. Break the request into explicit tasks AND implicit dependency tasks.
2. **Act**: Use the MCP tools.
3. **Observe**: Review the result of your tool call.
4. **Reflect**: Before telling the user you are done, ask yourself: ""Is this object in a broken state? Are any references null?"" Fix them if needed.

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
2. project/read_file (params: ""path"") - Reads the contents of a text file. Use this to inspect existing code before modifying it!
3. project/inspect_asset (params: ""path"") - Reads metadata/properties of Prefabs, Materials, etc. Use this to see what components/references an object already has!
4. project/write_file (params: ""path"", ""content"") - Creates/overwrites a file.
5. scene/instantiate_node (params: ""type"", ""name"") - Creates a GameObject. 'type' can be a primitive (Cube, Sphere, Capsule, Cylinder, Plane, Quad) or 'GameObject' for an empty object.
6. scene/modify_node (params: ""path"", ""property"", ""value"") - Edits a GameObject (name, position, add_component, remove_component).
7. scene/inspect_node (params: ""path"") - Returns an object's components. Essential for finding missing dependencies!
8. scene/set_component_property (params: ""path"", ""component"", ""property"", ""value"") - Sets a property on a component. Use this for scene wiring (linking targets)!
9. editor/read_console (params: none) - Returns the latest 30 warnings/errors. Use this after every script change to self-heal!

Wait for the [Observation] from the system before proceeding.";

        public void ProcessPrompt(string prompt, string model, Action<string, bool> onComplete)
        {
            Debug.Log($"[Omnisense] Processing prompt with model: {model}");
            OmnisenseUndoManager.StartTurn(Guid.NewGuid().ToString());
            
            if (_history.Count == 0)
            {
                _history.Add(new ChatMessage { role = "system", content = SYSTEM_PROMPT });
            }
            _history.Add(new ChatMessage { role = "user", content = prompt });
            ExecuteRequest(model, onComplete);
        }

        private void ExecuteRequest(string model, Action<string, bool> onComplete)
        {
            string apiKey = GetApiKey(model);
            if (string.IsNullOrEmpty(apiKey))
            {
                onComplete?.Invoke("Error: API Key missing. Please set it in the Settings tab.", true);
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
                Debug.LogError($"[Omnisense] Unsupported model selected: {model}");
                onComplete?.Invoke($"Error: Unsupported model {model}", true);
            }
        }

        private void CallOpenAI(string apiKey, string model, Action<string, bool> onComplete)
        {
            var requestData = new OpenAIRequest { model = model, messages = _history };
            string json = JsonUtility.ToJson(requestData);

            var webRequest = new UnityWebRequest("https://api.openai.com/v1/chat/completions", "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("Authorization", "Bearer " + apiKey);

            Debug.Log($"[Omnisense] Sending request to OpenAI API using model {model}...");
            var operation = webRequest.SendWebRequest();
            operation.completed += (op) =>
            {
                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log("[Omnisense] Received successful response from API.");
                    string responseText = ExtractContent(webRequest.downloadHandler.text);
                    HandleResponse(responseText, model, onComplete);
                }
                else
                {
                    Debug.LogError($"[Omnisense] API Error: {webRequest.error}");
                    onComplete?.Invoke($"API Error: {webRequest.error}\n{webRequest.downloadHandler.text}", true);
                }
                webRequest.Dispose();
            };
        }

        // Placeholder for other providers - similar implementation to CallOpenAI
        private void CallAnthropic(string key, string model, Action<string, bool> cb) => cb?.Invoke("Anthropic integration coming in Phase 3.1", true);
        private void CallGemini(string key, string model, Action<string, bool> cb) => cb?.Invoke("Gemini integration coming in Phase 3.1", true);
        private void CallGrok(string key, string model, Action<string, bool> cb) => cb?.Invoke("Grok integration coming in Phase 3.1", true);

        private void HandleResponse(string response, string model, Action<string, bool> onComplete)
        {
            _history.Add(new ChatMessage { role = "assistant", content = response });

            string toolJson = ExtractToolCall(response);
            if (!string.IsNullOrEmpty(toolJson))
            {
                _turnToolCount++;
                // Clean up the response for the UI (hide the raw JSON block)
                string uiResponse = response.Replace("```mcp_json", "[Executing Tool...]").Replace("```", "");
                
                // If it's a huge code block, just show the thought and a status
                string thought = ExtractThought(response);
                if (!string.IsNullOrEmpty(thought)) uiResponse = $"<thought>{thought}</thought>\n\n[System]: Actioning your request...";

                onComplete?.Invoke(uiResponse, false);

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
                        
                        // Notify user about compilation
                        onComplete?.Invoke(uiResponse + "\n\n[System]: File written. Waiting for Unity to compile...", false);
                    }
                    else if (toolCall.method == "project/list_directory")
                    {
                        string path = ExtractParam(toolJson, "path");
                        result = MCPToolRegistry.ListDirectory(path);
                    }
                    else if (toolCall.method == "project/read_file")
                    {
                        string path = ExtractParam(toolJson, "path");
                        result = MCPToolRegistry.ReadFile(path);
                    }
                    else if (toolCall.method == "project/inspect_asset")
                    {
                        string path = ExtractParam(toolJson, "path");
                        result = MCPToolRegistry.InspectAsset(path);
                    }
                    else if (toolCall.method == "scene/instantiate_node")
                    {
                        string type = ExtractParam(toolJson, "type");
                        string name = ExtractParam(toolJson, "name");
                        
                        // Execute directly on main thread. Using delayCall here causes a Main Thread Deadlock
                        // because HandleResponse is already on the main thread and waiting for the task blocks it.
                        result = MCPToolRegistry.InstantiateNode(type, name);
                    }
                    else if (toolCall.method == "scene/modify_node")
                    {
                        string path = ExtractParam(toolJson, "path");
                        string prop = ExtractParam(toolJson, "property");
                        string val = ExtractParam(toolJson, "value");
                        result = MCPToolRegistry.ModifyNode(path, prop, val);
                    }
                    else if (toolCall.method == "scene/inspect_node")
                    {
                        string path = ExtractParam(toolJson, "path");
                        result = MCPToolRegistry.InspectNode(path);
                    }
                    else if (toolCall.method == "scene/set_component_property")
                    {
                        string path = ExtractParam(toolJson, "path");
                        string compName = ExtractParam(toolJson, "component");
                        string propName = ExtractParam(toolJson, "property");
                        string val = ExtractParam(toolJson, "value");
                        result = MCPToolRegistry.SetComponentProperty(path, compName, propName, val);
                    }
                    else if (toolCall.method == "editor/read_console")
                    {
                        result = MCPToolRegistry.ReadConsole();
                    }
                    else
                    {
                        result = new MCPToolRegistry.ToolResult { success = false, error = "Unknown tool: " + toolCall.method };
                    }

                    string observation = result.success ? result.observation : $"Error: {result.error}";
                    Debug.Log($"[Omnisense] Tool Result: {(result.success ? "Success" : "Failed")}. Observation added to history.");
                    _history.Add(new ChatMessage { role = "user", content = $"[Observation]\n{observation}" });
                    
                    // Recursive ReAct Loop
                    ExecuteRequest(model, onComplete);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Omnisense] Error parsing tool call: {e.Message}");
                    _history.Add(new ChatMessage { role = "user", content = $"[Observation]\nError parsing tool call: {e.Message}" });
                    ExecuteRequest(model, onComplete);
                }
            }
            else
            {
                // Final Check Loop: If we did actions, but haven't reflected yet, trigger one last turn
                if (_turnToolCount > 0 && !_isReflecting)
                {
                    Debug.Log("[Omnisense] Triggering proactive reflection turn...");
                    _isReflecting = true;
                    _history.Add(new ChatMessage { role = "user", content = "[System Audit]: Actions complete. Review your changes: Are there any null references, missing components, or obvious next steps (like scene wiring) to make this feature fully functional? If yes, execute them. If no, summarize your work to the user (be sure to highlight any proactive steps you took)." });
                    ExecuteRequest(model, onComplete);
                }
                else
                {
                    Debug.Log("[Omnisense] Final response received. Loop complete.");
                    _isReflecting = false;
                    _turnToolCount = 0;
                    onComplete?.Invoke(response, true);
                }
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
