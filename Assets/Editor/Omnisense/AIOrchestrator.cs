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
            public int max_completion_tokens;
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

        [Serializable]
        private class HistoryWrapper { public List<ChatMessage> list; }

        public AIOrchestrator()
        {
            LoadHistory();
        }

        private void SaveHistory()
        {
            var wrapper = new HistoryWrapper { list = _history };
            EditorPrefs.SetString("Omnisense_AI_History", JsonUtility.ToJson(wrapper));
            // Debug.Log($"[Omnisense] AI History saved ({_history.Count} messages).");
        }

        private void LoadHistory()
        {
            string json = EditorPrefs.GetString("Omnisense_AI_History", "");
            if (!string.IsNullOrEmpty(json))
            {
                var wrapper = JsonUtility.FromJson<HistoryWrapper>(json);
                if (wrapper != null && wrapper.list != null) {
                    _history = wrapper.list;
                    Debug.Log($"[Omnisense] AI History restored ({_history.Count} messages) from persistent storage.");
                }
            }
        }

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
5. **Proactivity**: DO NOT ask for permission to investigate or fix issues. If you need to read a file, inspect a node, or write code to solve the user's problem, DO IT IMMEDIATELY using the tools. Never stop midway to ask if you should continue.

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
5. project/write_file (params: ""path"", ""content"") - Creates a NEW file. DO NOT use this for modifying existing files unless you are completely rewriting them.
6. project/edit_file (params: ""path"", ""search_block"", ""replace_block"") - Fast O(1) editing for existing files. Use this to modify existing code. 'search_block' MUST be an exact string match (including whitespace) of the code you want to replace.
7. scene/instantiate_node (params: ""type"", ""name"") - Creates a GameObject. 'type' can be a primitive (Cube, Sphere, Capsule, Cylinder, Plane, Quad) or 'GameObject' for an empty object.
8. scene/modify_node (params: ""path"", ""property"", ""value"") - Edits a GameObject (name, position, add_component, remove_component).
9. scene/inspect_node (params: ""path"") - Returns an object's components.
10. scene/set_component_property (params: ""path"", ""component"", ""property"", ""value"") - Sets a property on a component.
11. editor/read_console (params: none) - Returns the latest 30 warnings/errors.

Wait for the [Observation] from the system before proceeding.";

        public void ProcessPrompt(string prompt, string model, string turnId, Action<string, bool> onComplete)
        {
            Debug.Log($"[Omnisense] Processing prompt with model: {model}");
            _turnToolCount = 0;
            _isReflecting = false;
            _isAborted = false;
            OmnisenseUndoManager.StartTurn(turnId);
            
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
            SaveHistory();
            ExecuteRequest(model, onComplete);
        }

        public void Resume(string model, Action<string, bool> onComplete)
        {
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

            // Set pending state to allow auto-resume after assembly reload
            EditorPrefs.SetBool("Omnisense_AI_PendingResume", true);
            EditorPrefs.SetString("Omnisense_AI_LastModel", model);

            // Prepare request based on provider
            if (model.StartsWith("gpt") || model.StartsWith("o3"))
            {
                CallOpenAI(apiKey, model, (resp, final) => {
                    if (final) EditorPrefs.SetBool("Omnisense_AI_PendingResume", false);
                    onComplete?.Invoke(resp, final);
                });
            }
            else if (model.StartsWith("claude"))
            {
                CallAnthropic(apiKey, model, (resp, final) => {
                    if (final) EditorPrefs.SetBool("Omnisense_AI_PendingResume", false);
                    onComplete?.Invoke(resp, final);
                });
            }
            else if (model.StartsWith("gemini"))
            {
                CallGemini(apiKey, model, (resp, final) => {
                    if (final) EditorPrefs.SetBool("Omnisense_AI_PendingResume", false);
                    onComplete?.Invoke(resp, final);
                });
            }
            else if (model.StartsWith("grok"))
            {
                CallGrok(apiKey, model, (resp, final) => {
                    if (final) EditorPrefs.SetBool("Omnisense_AI_PendingResume", false);
                    onComplete?.Invoke(resp, final);
                });
            }
            else
            {
                EditorPrefs.SetBool("Omnisense_AI_PendingResume", false);
                Debug.LogError($"[Omnisense] Unsupported model selected: {model}");
                onComplete?.Invoke($"Error: Unsupported model {model}", true);
            }
        }

        private UnityWebRequest _activeRequest;
        private bool _isAborted = false;

        public void Abort()
        {
            Debug.Log("[Omnisense] User requested to abort AI execution.");
            _isAborted = true;
            EditorPrefs.SetBool("Omnisense_AI_PendingResume", false);
            if (_activeRequest != null)
            {
                _activeRequest.Abort();
                _activeRequest = null;
            }
        }

        private void CallOpenAI(string apiKey, string model, Action<string, bool> onComplete)
        {
            if (_isAborted) return;
            // Prevent 'Lost in the Middle' by injecting a trailing format reminder
            var payloadMessages = new List<ChatMessage>(_history);
            payloadMessages.Add(new ChatMessage { 
                role = "user", 
                content = "[System Reminder: You must use the exact ```mcp_json {\"method\":\"...\",\"params\":{...}} ``` format for any tool calls. Do NOT forget the closing backticks.]" 
            });

            int maxTokens = EditorPrefs.GetInt("Omnisense_OpenAI_MaxTokens", 4096);
            var requestData = new OpenAIRequest { model = model, messages = payloadMessages, max_completion_tokens = maxTokens };
            string json = JsonUtility.ToJson(requestData);

            _activeRequest = new UnityWebRequest("https://api.openai.com/v1/chat/completions", "POST");
            _activeRequest.timeout = 60; // 60 second timeout
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            _activeRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            _activeRequest.downloadHandler = new DownloadHandlerBuffer();
            _activeRequest.SetRequestHeader("Content-Type", "application/json");
            _activeRequest.SetRequestHeader("Authorization", "Bearer " + apiKey);

            Debug.Log($"[Omnisense] Sending request to OpenAI API using model {model} ({payloadMessages.Count} messages in history)...");
            var operation = _activeRequest.SendWebRequest();
            operation.completed += (op) =>
            {
                if (_isAborted) return;
                if (_activeRequest == null) return;
                
                if (_activeRequest.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log("[Omnisense] Received successful response from API.");
                    string responseText = ExtractContent(_activeRequest.downloadHandler.text);
                    HandleResponse(responseText, model, onComplete);
                }
                else
                {
                    Debug.LogError($"[Omnisense] API Error: {_activeRequest.error}");
                    onComplete?.Invoke($"API Error: {_activeRequest.error}\n{_activeRequest.downloadHandler.text}", true);
                }
                _activeRequest.Dispose();
                _activeRequest = null;
            };
        }

        private void CallAnthropic(string apiKey, string model, Action<string, bool> onComplete)
        {
            if (_isAborted) return;
            var payloadMessages = new List<ChatMessage>(_history);
            int maxTokens = EditorPrefs.GetInt("Omnisense_Anthropic_MaxTokens", 4096);
            
            // Anthropic expects a slightly different JSON structure
            string json = "{\"model\":\"" + model + "\",\"max_tokens\":" + maxTokens + ",\"messages\":" + JsonUtility.ToJson(new HistoryWrapper { list = payloadMessages }) + "}";
            // HistoryWrapper adds a "list" key, but Anthropic wants a raw array. We need a cleaner way.
            string messagesJson = "[";
            for(int i=0; i<payloadMessages.Count; i++) {
                messagesJson += "{\"role\":\"" + (payloadMessages[i].role == "system" ? "user" : payloadMessages[i].role) + "\",\"content\":\"" + payloadMessages[i].content.Replace("\"", "\\\"").Replace("\n", "\\n") + "\"}";
                if(i < payloadMessages.Count-1) messagesJson += ",";
            }
            messagesJson += "]";
            
            json = "{\"model\":\"" + model + "\",\"max_tokens\":" + maxTokens + ",\"messages\":" + messagesJson + "}";

            _activeRequest = new UnityWebRequest("https://api.anthropic.com/v1/messages", "POST");
            _activeRequest.timeout = 60;
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            _activeRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            _activeRequest.downloadHandler = new DownloadHandlerBuffer();
            _activeRequest.SetRequestHeader("Content-Type", "application/json");
            _activeRequest.SetRequestHeader("x-api-key", apiKey);
            _activeRequest.SetRequestHeader("anthropic-version", "2023-06-01");

            Debug.Log($"[Omnisense] Sending request to Anthropic API using model {model} ({payloadMessages.Count} messages in history)...");
            var operation = _activeRequest.SendWebRequest();
            operation.completed += (op) => {
                if (_isAborted || _activeRequest == null) return;
                if (_activeRequest.result == UnityWebRequest.Result.Success) {
                    // Anthropic response is different
                    string resp = _activeRequest.downloadHandler.text;
                    // Simple extraction for now
                    var match = Regex.Match(resp, "\"text\":\"(.*?)\"", RegexOptions.Singleline);
                    HandleResponse(match.Success ? match.Groups[1].Value.Replace("\\n", "\n").Replace("\\\"", "\"") : resp, model, onComplete);
                } else {
                    onComplete?.Invoke($"Anthropic Error: {_activeRequest.error}\n{_activeRequest.downloadHandler.text}", true);
                }
                _activeRequest?.Dispose(); _activeRequest = null;
            };
        }

        private void CallGemini(string apiKey, string model, Action<string, bool> onComplete)
        {
            if (_isAborted) return;
            int maxTokens = EditorPrefs.GetInt("Omnisense_Gemini_MaxTokens", 4096);
            
            // Gemini JSON structure is nested: contents -> parts -> text
            string contentsJson = "[";
            for(int i=0; i<_history.Count; i++) {
                string role = _history[i].role == "assistant" ? "model" : "user";
                contentsJson += "{\"role\":\"" + role + "\",\"parts\":[{\"text\":\"" + _history[i].content.Replace("\"", "\\\"").Replace("\n", "\\n") + "\"}]}";
                if(i < _history.Count-1) contentsJson += ",";
            }
            contentsJson += "]";
            
            string json = "{\"contents\":" + contentsJson + ",\"generationConfig\":{\"maxOutputTokens\":" + maxTokens + "}}";

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            _activeRequest = new UnityWebRequest(url, "POST");
            _activeRequest.timeout = 60;
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            _activeRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            _activeRequest.downloadHandler = new DownloadHandlerBuffer();
            _activeRequest.SetRequestHeader("Content-Type", "application/json");

            Debug.Log($"[Omnisense] Sending request to Gemini API using model {model} ({_history.Count} messages in history)...");
            var operation = _activeRequest.SendWebRequest();
            operation.completed += (op) => {
                if (_isAborted || _activeRequest == null) return;
                if (_activeRequest.result == UnityWebRequest.Result.Success) {
                    string resp = _activeRequest.downloadHandler.text;
                    var match = Regex.Match(resp, "\"text\":\\s*\"(.*?)\"", RegexOptions.Singleline);
                    HandleResponse(match.Success ? match.Groups[1].Value.Replace("\\n", "\n").Replace("\\\"", "\"") : resp, model, onComplete);
                } else {
                    onComplete?.Invoke($"Gemini Error: {_activeRequest.error}\n{_activeRequest.downloadHandler.text}", true);
                }
                _activeRequest?.Dispose(); _activeRequest = null;
            };
        }

        private void CallGrok(string apiKey, string model, Action<string, bool> onComplete)
        {
            if (_isAborted) return;
            int maxTokens = EditorPrefs.GetInt("Omnisense_Grok_MaxTokens", 4096);
            var payloadMessages = new List<ChatMessage>(_history);
            var requestData = new OpenAIRequest { model = model, messages = payloadMessages, max_completion_tokens = maxTokens };
            string json = JsonUtility.ToJson(requestData);

            _activeRequest = new UnityWebRequest("https://api.x.ai/v1/chat/completions", "POST");
            _activeRequest.timeout = 60;
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            _activeRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            _activeRequest.downloadHandler = new DownloadHandlerBuffer();
            _activeRequest.SetRequestHeader("Content-Type", "application/json");
            _activeRequest.SetRequestHeader("Authorization", "Bearer " + apiKey);

            Debug.Log($"[Omnisense] Sending request to Grok API using model {model} ({payloadMessages.Count} messages in history)...");
            var operation = _activeRequest.SendWebRequest();
            operation.completed += (op) => {
                if (_isAborted || _activeRequest == null) return;
                if (_activeRequest.result == UnityWebRequest.Result.Success) {
                    string responseText = ExtractContent(_activeRequest.downloadHandler.text);
                    HandleResponse(responseText, model, onComplete);
                } else {
                    onComplete?.Invoke($"Grok Error: {_activeRequest.error}\n{_activeRequest.downloadHandler.text}", true);
                }
                _activeRequest?.Dispose(); _activeRequest = null;
            };
        }

        public event Action<string, Action<bool>> OnPendingAction;

        private void HandleResponse(string response, string model, Action<string, bool> onComplete)
        {
            _history.Add(new ChatMessage { role = "assistant", content = response });
            SaveHistory();

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
                                         toolCall.method == "project/edit_file" ||
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
                                SaveHistory();
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
                    SaveHistory();
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
                    SaveHistory();
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
            // Truncate large tool observations to prevent Context Bloat, BUT 
            // only prune observations that are older than 10 messages so the agent retains recent context!
            int preserveCount = 10;
            int limit = Mathf.Max(0, _history.Count - preserveCount);
            
            for (int i = 0; i < limit; i++)
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
            if (toolCall.@params == null) return "Pending changes...";
            
            if (toolCall.method == "project/write_file")
                return $"<color=#00FF00>+ Write File:</color> {toolCall.@params.path}";
            if (toolCall.method == "scene/instantiate_node")
                return $"<color=#00FF00>+ Instantiate:</color> {toolCall.@params.type} as '{toolCall.@params.name}'";
            if (toolCall.method == "scene/modify_node")
            {
                string prop = toolCall.@params.property;
                string val = toolCall.@params.value;
                if (prop == "add_component") return $"<color=#00FF00>+ Add Component:</color> {val} on {toolCall.@params.path}";
                if (prop == "remove_component") return $"<color=#FF0000>- Remove Component:</color> {val} from {toolCall.@params.path}";
                return $"<color=#FFFF00>~ Modify Node:</color> Set {prop} to '{val}' on {toolCall.@params.path}";
            }
            if (toolCall.method == "scene/set_component_property")
                return $"<color=#FFFF00>~ Set Property:</color> {toolCall.@params.component}.{toolCall.@params.property} = '{toolCall.@params.value}' on {toolCall.@params.path}";
            return "Pending changes...";
        }

        private void ExecuteToolAndResume(MCPToolRequest toolCall, string toolJson, string uiResponse, string model, Action<string, bool> onComplete)
        {
            MCPToolRegistry.ToolResult result = null;
            var p = toolCall.@params ?? new MCPToolParams();

            if (toolCall.method == "project/write_file")
            {
                result = MCPToolRegistry.WriteFile(p.path, p.content);
                onComplete?.Invoke(uiResponse + "\n\n[System]: File written. Waiting for Unity to compile...", false);
            }
            else if (toolCall.method == "project/edit_file")
            {
                result = MCPToolRegistry.EditFile(p.path, p.search_block, p.replace_block);
                onComplete?.Invoke(uiResponse + "\n\n[System]: File edited. Waiting for Unity to compile...", false);
            }
            else if (toolCall.method == "project/list_directory")
                result = MCPToolRegistry.ListDirectory(p.path);
            else if (toolCall.method == "project/read_file")
                result = MCPToolRegistry.ReadFile(p.path);
            else if (toolCall.method == "project/update_dna")
                result = MCPToolRegistry.UpdateDNA(p.content);
            else if (toolCall.method == "project/inspect_asset")
                result = MCPToolRegistry.InspectAsset(p.path);
            else if (toolCall.method == "scene/instantiate_node")
                result = MCPToolRegistry.InstantiateNode(p.type, p.name);
            else if (toolCall.method == "scene/modify_node")
                result = MCPToolRegistry.ModifyNode(p.path, p.property, p.value);
            else if (toolCall.method == "scene/inspect_node")
                result = MCPToolRegistry.InspectNode(p.path);
            else if (toolCall.method == "scene/set_component_property")
                result = MCPToolRegistry.SetComponentProperty(p.path, p.component, p.property, p.value);
            else if (toolCall.method == "editor/read_console")
                result = MCPToolRegistry.ReadConsole();
            else
                result = new MCPToolRegistry.ToolResult { success = false, error = "Unknown tool: " + toolCall.method };

            string observation = result.success ? result.observation : $"Error: {result.error}";
            Debug.Log($"[Omnisense] Tool Result: {(result.success ? "Success" : "Failed")}. Observation added to history.");
            _history.Add(new ChatMessage { role = "user", content = $"[Observation]\n{observation}" });
            SaveHistory();
            
            ExecuteRequest(model, onComplete);
        }


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
        [Serializable]
        public class MCPToolRequest 
        { 
            public string method; 
            public MCPToolParams @params;
        }

        [Serializable]
        public class MCPToolParams
        {
            public string path;
            public string content;
            public string type;
            public string name;
            public string property;
            public string value;
            public string component;
            public string search_block;
            public string replace_block;
        }
    }
}
