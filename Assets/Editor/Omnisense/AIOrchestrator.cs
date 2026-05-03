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
4. **Project DNA**: You have a long-term memory file called `.omnisense_dna.md`. If you learn something new about the project's architecture, folder structure, or naming conventions, you MUST use `project/update_dna` to record it.

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
2. project/read_file (params: ""path"") - Reads the contents of a text file.
3. project/update_dna (params: ""content"") - Updates the .omnisense_dna.md file. Use this to persist architectural rules, naming conventions, and project knowledge.
4. project/inspect_asset (params: ""path"") - Reads metadata/properties of Prefabs, Materials, etc.
5. project/write_file (params: ""path"", ""content"") - Creates/overwrites a file.
6. scene/instantiate_node (params: ""type"", ""name"") - Creates a GameObject. 'type' can be a primitive (Cube, Sphere, Capsule, Cylinder, Plane, Quad) or 'GameObject' for an empty object.
7. scene/modify_node (params: ""path"", ""property"", ""value"") - Edits a GameObject (name, position, add_component, remove_component).
8. scene/inspect_node (params: ""path"") - Returns an object's components.
9. scene/set_component_property (params: ""path"", ""component"", ""property"", ""value"") - Sets a property on a component.
10. editor/read_console (params: none) - Returns the latest 30 warnings/errors.

Wait for the [Observation] from the system before proceeding.";

        public void ProcessPrompt(string prompt, string model, Action<string, bool> onComplete)
        {
            Debug.Log($"[Omnisense] Processing prompt with model: {model}");
            _turnToolCount = 0;
            _isReflecting = false;
            OmnisenseUndoManager.StartTurn(Guid.NewGuid().ToString());
            
            if (_history.Count == 0)
            {
                _history.Add(new ChatMessage { role = "system", content = SYSTEM_PROMPT });
                
                // Load Project DNA if exists
                try {
                    string dnaPath = System.IO.Path.Combine(Application.dataPath, "..", ".omnisense_dna.md");
                    if (System.IO.File.Exists(dnaPath)) {
                        string dnaContent = System.IO.File.ReadAllText(dnaPath);
                        _history.Add(new ChatMessage { role = "system", content = $"[PROJECT DNA]\nThis is the persistent memory of this project. Conform to these architectural rules:\n\n{dnaContent}" });
                        Debug.Log("[Omnisense] Project DNA loaded and injected into context.");
                    }
                } catch { }
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
            // Prevent 'Lost in the Middle' by injecting a trailing format reminder
            var payloadMessages = new List<ChatMessage>(_history);
            payloadMessages.Add(new ChatMessage { 
                role = "user", 
                content = "[System Reminder: You must use the exact ```mcp_json {\"method\":\"...\",\"params\":{...}} ``` format for any tool calls. Do NOT forget the closing backticks.]" 
            });

            var requestData = new OpenAIRequest { model = model, messages = payloadMessages };
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

        public event Action<string, Action<bool>> OnPendingAction;

        private void HandleResponse(string response, string model, Action<string, bool> onComplete)
        {
            _history.Add(new ChatMessage { role = "assistant", content = response });

            string toolJson = ExtractToolCall(response);
            if (!string.IsNullOrEmpty(toolJson))
            {
                _turnToolCount++;
                string uiResponse = response.Replace("```mcp_json", "[Executing Tool...]").Replace("```", "");
                string thought = ExtractThought(response);
                if (!string.IsNullOrEmpty(thought)) uiResponse = $"<thought>{thought}</thought>\n\n[System]: Actioning your request...";
                onComplete?.Invoke(uiResponse, false);

                try
                {
                    var toolCall = JsonUtility.FromJson<MCPToolRequest>(toolJson);
                    
                    bool isDestructive = toolCall.method == "project/write_file" ||
                                         toolCall.method == "scene/instantiate_node" ||
                                         toolCall.method == "scene/modify_node" ||
                                         toolCall.method == "scene/set_component_property";

                    if (isDestructive && OnPendingAction != null)
                    {
                        string diffSummary = GenerateDiffSummary(toolCall, toolJson);
                        OnPendingAction.Invoke(diffSummary, (approved) => {
                            if (approved)
                            {
                                ExecuteToolAndResume(toolCall, toolJson, uiResponse, model, onComplete);
                            }
                            else
                            {
                                Debug.Log("[Omnisense] User rejected pending action.");
                                _history.Add(new ChatMessage { role = "user", content = "[Observation]\nThe user rejected this change. Please revise your plan or ask for clarification." });
                                ExecuteRequest(model, onComplete);
                            }
                        });
                        return; // Halt until callback
                    }
                    else
                    {
                        ExecuteToolAndResume(toolCall, toolJson, uiResponse, model, onComplete);
                    }
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
                    
                    PruneHistory();

                    onComplete?.Invoke(response, true);
                }
            }
        }

        private void PruneHistory()
        {
            // Truncate large tool observations after the turn is complete to prevent Context Bloat.
            for (int i = 0; i < _history.Count; i++)
            {
                if (_history[i].role == "user" && _history[i].content.StartsWith("[Observation]") && _history[i].content.Length > 250)
                {
                    _history[i].content = "[Observation]\n(Output truncated after turn completion to preserve context window).";
                }
            }
            Debug.Log("[Omnisense] Context window pruned for next turn.");
        }

        private string GenerateDiffSummary(MCPToolRequest toolCall, string toolJson)
        {
            if (toolCall.method == "project/write_file")
                return $"<color=#00FF00>+ Write File:</color> {ExtractParam(toolJson, "path")}";
            if (toolCall.method == "scene/instantiate_node")
                return $"<color=#00FF00>+ Instantiate:</color> {ExtractParam(toolJson, "type")} as '{ExtractParam(toolJson, "name")}'";
            if (toolCall.method == "scene/modify_node")
            {
                string prop = ExtractParam(toolJson, "property");
                string val = ExtractParam(toolJson, "value");
                if (prop == "add_component") return $"<color=#00FF00>+ Add Component:</color> {val} on {ExtractParam(toolJson, "path")}";
                if (prop == "remove_component") return $"<color=#FF0000>- Remove Component:</color> {val} from {ExtractParam(toolJson, "path")}";
                return $"<color=#FFFF00>~ Modify Node:</color> Set {prop} to '{val}' on {ExtractParam(toolJson, "path")}";
            }
            if (toolCall.method == "scene/set_component_property")
                return $"<color=#FFFF00>~ Set Property:</color> {ExtractParam(toolJson, "component")}.{ExtractParam(toolJson, "property")} = '{ExtractParam(toolJson, "value")}' on {ExtractParam(toolJson, "path")}";
            return "Pending changes...";
        }

        private void ExecuteToolAndResume(MCPToolRequest toolCall, string toolJson, string uiResponse, string model, Action<string, bool> onComplete)
        {
            MCPToolRegistry.ToolResult result = null;

            if (toolCall.method == "project/write_file")
            {
                string path = ExtractParam(toolJson, "path");
                string content = ExtractContentRaw(toolJson, "content");
                result = MCPToolRegistry.WriteFile(path, content);
                onComplete?.Invoke(uiResponse + "\n\n[System]: File written. Waiting for Unity to compile...", false);
            }
            else if (toolCall.method == "project/list_directory")
                result = MCPToolRegistry.ListDirectory(ExtractParam(toolJson, "path"));
            else if (toolCall.method == "project/read_file")
                result = MCPToolRegistry.ReadFile(ExtractParam(toolJson, "path"));
            else if (toolCall.method == "project/update_dna")
                result = MCPToolRegistry.UpdateDNA(ExtractContentRaw(toolJson, "content"));
            else if (toolCall.method == "project/inspect_asset")
                result = MCPToolRegistry.InspectAsset(ExtractParam(toolJson, "path"));
            else if (toolCall.method == "scene/instantiate_node")
                result = MCPToolRegistry.InstantiateNode(ExtractParam(toolJson, "type"), ExtractParam(toolJson, "name"));
            else if (toolCall.method == "scene/modify_node")
                result = MCPToolRegistry.ModifyNode(ExtractParam(toolJson, "path"), ExtractParam(toolJson, "property"), ExtractParam(toolJson, "value"));
            else if (toolCall.method == "scene/inspect_node")
                result = MCPToolRegistry.InspectNode(ExtractParam(toolJson, "path"));
            else if (toolCall.method == "scene/set_component_property")
                result = MCPToolRegistry.SetComponentProperty(ExtractParam(toolJson, "path"), ExtractParam(toolJson, "component"), ExtractParam(toolJson, "property"), ExtractParam(toolJson, "value"));
            else if (toolCall.method == "editor/read_console")
                result = MCPToolRegistry.ReadConsole();
            else
                result = new MCPToolRegistry.ToolResult { success = false, error = "Unknown tool: " + toolCall.method };

            string observation = result.success ? result.observation : $"Error: {result.error}";
            Debug.Log($"[Omnisense] Tool Result: {(result.success ? "Success" : "Failed")}. Observation added to history.");
            _history.Add(new ChatMessage { role = "user", content = $"[Observation]\n{observation}" });
            
            ExecuteRequest(model, onComplete);
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
            // 1. Try to match proper block WITH closing backticks
            var match = Regex.Match(content, @"```(?:mcp_json|json)?\s*(\{.*?\})\s*```", RegexOptions.Singleline);
            if (match.Success) return match.Groups[1].Value.Trim();

            // 2. Try to match block WITHOUT closing backticks (e.g. if the LLM stopped generating early)
            var matchNoClose = Regex.Match(content, @"```(?:mcp_json|json)?\s*(\{.*\})", RegexOptions.Singleline);
            if (matchNoClose.Success) return matchNoClose.Groups[1].Value.Trim();

            // 3. Fallback: try to match raw JSON without any markdown ticks at all
            var fallback = Regex.Match(content, @"\{\s*""method"":\s*"".*?\}", RegexOptions.Singleline);
            if (fallback.Success) return fallback.Value.Trim();

            return null;
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
