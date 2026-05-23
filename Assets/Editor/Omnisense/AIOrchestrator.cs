using System;
using System.Collections.Generic;
using System.Linq;
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

        [Serializable]
        public class ManagerEvaluation
        {
            public bool is_complete;
            public string feedback;
        }

        [Serializable]
        public class PlannerResponse
        {
            public string intent;
            public bool requires_tools;
            public List<string> tasks;
        }

        private List<ChatMessage> _history = new List<ChatMessage>();
        private int _turnToolCount = 0;
        private bool _isReflecting = false;
        private bool _isManagerEvaluating = false;
        private bool _isPlanning = false;
        private bool _isConceptualTurn = false;
        private Queue<string> _pendingTasks = new Queue<string>();
        private string _currentTask = "";
        private int _stepCount = 0;
        private const int MAX_STEPS = 25;
        private List<string> _actionHistory = new List<string>();
        private List<string> _turnContextLog = new List<string>();
        private List<string> _persistentScratchpad = new List<string>();
        private string _lastWorkerResponse = "";
        private System.Text.StringBuilder _currentTurnTrace = new System.Text.StringBuilder();
        private int _consecutiveManagerRejections = 0;
        private string _routingDecision = "planner";

        [Serializable]
        public class HistoryWrapper { public List<ChatMessage> list; }

        [Serializable]
        public class OrchestratorState
        {
            public bool isReflecting;
            public bool isManagerEvaluating;
            public bool isPlanning;
            public bool isConceptualTurn;
            public List<string> pendingTasks;
            public string currentTask;
            public int stepCount;
            public int turnToolCount;
            public string lastWorkerResponse;
            public List<string> actionHistory;
            public List<string> turnContextLog;
            public List<string> persistentScratchpad;
            public int consecutiveManagerRejections;
            public string routingDecision;
        }

        private void SaveState()
        {
            var state = new OrchestratorState
            {
                isReflecting = _isReflecting,
                isManagerEvaluating = _isManagerEvaluating,
                isPlanning = _isPlanning,
                isConceptualTurn = _isConceptualTurn,
                pendingTasks = _pendingTasks.ToList(),
                currentTask = _currentTask,
                stepCount = _stepCount,
                turnToolCount = _turnToolCount,
                lastWorkerResponse = _lastWorkerResponse,
                actionHistory = _actionHistory,
                turnContextLog = _turnContextLog,
                persistentScratchpad = _persistentScratchpad,
                consecutiveManagerRejections = _consecutiveManagerRejections,
                routingDecision = _routingDecision
            };
            EditorPrefs.SetString("Omnisense_AI_State", JsonUtility.ToJson(state));
        }

        private void LoadState()
        {
            string json = EditorPrefs.GetString("Omnisense_AI_State", "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var state = JsonUtility.FromJson<OrchestratorState>(json);
                    if (state != null)
                    {
                        _isReflecting = state.isReflecting;
                        _isManagerEvaluating = state.isManagerEvaluating;
                        _isPlanning = state.isPlanning;
                        _isConceptualTurn = state.isConceptualTurn;
                        _pendingTasks = new Queue<string>(state.pendingTasks ?? new List<string>());
                        _currentTask = state.currentTask ?? "";
                        _stepCount = state.stepCount;
                        _turnToolCount = state.turnToolCount;
                        _lastWorkerResponse = state.lastWorkerResponse ?? "";
                        _actionHistory = state.actionHistory ?? new List<string>();
                        _turnContextLog = state.turnContextLog ?? new List<string>();
                        _persistentScratchpad = state.persistentScratchpad ?? new List<string>();
                        _consecutiveManagerRejections = state.consecutiveManagerRejections;
                        _routingDecision = state.routingDecision ?? "planner";
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Omnisense] Failed to deserialize Orchestrator State: {e.Message}");
                }
            }
        }

        public AIOrchestrator()
        {
            LoadHistory();
        }

        private void SaveHistory()
        {
            var wrapper = new HistoryWrapper { list = _history };
            EditorPrefs.SetString("Omnisense_AI_History", JsonUtility.ToJson(wrapper));
            SaveState();
        }

        public void ClearHistory()
        {
            _history.Clear();
            _pendingTasks.Clear();
            _currentTask = "";
            _lastWorkerResponse = "";
            _isReflecting = false;
            _isManagerEvaluating = false;
            _isPlanning = false;
            _isConceptualTurn = false;
            _stepCount = 0;
            _turnToolCount = 0;
            _actionHistory.Clear();
            _turnContextLog.Clear();
            _persistentScratchpad.Clear();
            _consecutiveManagerRejections = 0;
            SaveHistory();
            Debug.Log("[Omnisense] AI History cleared.");
        }

        public void SyncWithSession(ChatSession session)
        {
            if (session == null) { ClearHistory(); return; }
            
            _history.Clear();
            _pendingTasks.Clear();
            _currentTask = "";
            _lastWorkerResponse = "";
            _isReflecting = false;
            _isManagerEvaluating = false;
            _isPlanning = false;
            _isConceptualTurn = false;
            _stepCount = 0;
            _turnToolCount = 0;
            _actionHistory.Clear();
            _turnContextLog.Clear();
            _persistentScratchpad.Clear();

            // Initialize with correct System Prompt
            string model = EditorPrefs.GetString("Omnisense_SelectedModel", "gpt-4o");
            string promptToUse = model == "self-hosted" ? SYSTEM_PROMPT_LITE : GENERIC_WORKER_PROMPT;
            _history.Add(new ChatMessage { role = "system", content = promptToUse });

            // Re-bootstrap DNA
            try {
                string dnaPath = System.IO.Path.Combine(Application.dataPath, "..", ".omnisense_dna.md");
                if (System.IO.File.Exists(dnaPath)) {
                    string dnaContent = System.IO.File.ReadAllText(dnaPath);
                    _history.Add(new ChatMessage { role = "system", content = $"[PROJECT DNA]\n{dnaContent}" });
                }
            } catch { }

            // Map Session Messages (User/AI) to AIOrchestrator Messages (User/Assistant)
            foreach (var msg in session.messages)
            {
                if (msg.sender == "User")
                {
                    // If it was a planner request, keep it wrapped if possible, 
                    // but usually sessions store the raw text. 
                    // For history restoration, we treat it as a standard user message.
                    _history.Add(new ChatMessage { role = "user", content = msg.content });
                }
                else if (msg.sender == "AI")
                {
                    _history.Add(new ChatMessage { role = "assistant", content = msg.content });
                }
            }
            SaveHistory();
            Debug.Log($"[Omnisense] AI Brain synced with Session: {session.name} ({_history.Count} messages).");
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
            LoadState();
        }

        private const string PLANNER_SYSTEM_PROMPT = @"You are the Omnisense Senior Planner.
Analyze the user's latest request and plan a series of sub-tasks to achieve it.
- If the user explicitly asks to CREATE, MODIFY, INSPECT, or DELETE files/scripts/objects, you MUST set 'requires_tools' to true and provide a list of granular, concrete sub-tasks.
- If the user is asking a general conceptual question or advice, set 'requires_tools' to false.

Output ONLY a valid JSON object in this exact format:
{
  ""intent"": ""project_modification"" | ""conceptual_q_and_a"",
  ""requires_tools"": true | false,
  ""tasks"": [""Task 1 description"", ""Task 2 description""]
}
Do NOT output any other text or execute any tools.";

        private const string MANAGER_SYSTEM_PROMPT = @"You are the Omnisense Senior AI Architect & Router.
Your job is to manage the execution of the user's overall goal.
You review the conversation history and the active sub-task.

### DIRECTIVES:
1. **Routing**: Determine which specialized agent is best equipped to handle the CURRENT sub-task:
   - If the task involves creating, editing, or positioning UI components, Canvas, EventSystem, Layout Groups, Texts, Panels, or Buttons, route to 'ui_agent'.
   - If the task is about standard C# scripting, logic, folders, tags/layers, asset lookups, or general Unity setup, route to 'generic_agent'.
   - If the overall goal is fully accomplished, route to 'end'.
2. **Quality Audit**: Review the worker's latest response.
   - If the worker successfully finished the sub-task, approve it by setting 'is_complete' to true.
   - If the worker left things incomplete or there are active compilation errors, reject it by setting 'is_complete' to false and provide feedback.

Output ONLY a valid JSON object in this exact format:
{
  ""routing"": ""ui_agent"" | ""generic_agent"" | ""end"",
  ""is_complete"": true | false,
  ""feedback"": ""Your audit feedback or routing justification""
}";

        private const string UI_SPECIALIST_PROMPT = @"You are the Omnisense UI Specialist Agent, an expert in Unity GUI, Canvas Layouts, and TextMeshPro.
Your goal is to build responsive, modern, and visually stunning user interfaces.

### CRITICAL RULES:
1. **Ensure Canvas Baseline**: Never leave canvas/EventSystem missing. Use 'ui/setup_canvas' first.
2. **Component Hierarchy**: Make sure panels, buttons, and texts are correctly parented.
3. **Advanced UI Tools**: Proactively use your high-level UI tools to execute tasks in one turn:
   - 'ui/setup_canvas' (instantiates Canvas and EventSystem)
   - 'ui/create_panel' (creates container panels with default background)
   - 'ui/create_text' (creates aligned TextMeshPro / standard text)
   - 'ui/create_button' (creates beautiful buttons with text child)
   - 'ui/setup_layout_group' (sets up Vertical/Horizontal/Grid layouts with content size fitters)
4. **Visual Excellence**: Choose harmonious dark theme or glowing primary colors.
5. **No Placeholders**: Deliver fully functional UI components, not raw mocks.

### OPERATIONAL LOOP: ReAct
1. **Thought & Action**: Think step-by-step in a <thought> block, then immediately output the ```mcp_json tool block.
2. If you are completely finished with your task, summarize your progress and output plain text without any tool call to signal completion.";

        private const string GENERIC_WORKER_PROMPT = @"You are the Omnisense Senior Unity Architect, an elite autonomous developer agent.
Your goal is to write clean, compiled, and functional C# scripts, logic, and general Unity concerns.

### MANDATE: Second-Order Thinking
When editing or creating scripts/objects:
1. **Component Dependencies**: If you write a script using Rigidbody, make sure it is attached.
2. **Environment Validation**: After writing code, you MUST use 'editor/read_console' to check for compilation errors. If errors exist, you MUST fix them immediately.
3. **Project DNA**: Conform to the persistent rules in '.omnisense_dna.md'.

### OPERATIONAL LOOP: ReAct
1. **Thought & Action**: Output a <thought> block to plan your steps, then IMMEDIATELY output the ```mcp_json tool block.
2. If you are completely finished with your task, summarize your progress and output plain text without any tool call to signal completion.";

        private const string SYSTEM_PROMPT_LITE = @"You are the Omnisense Assistant, a helpful AI developer agent.
Your goal is to execute commands to help the user. Think step by step using a <thought> block, and THEN IMMEDIATELY output your tool call in the same message. NEVER stop generating after a thought block.
Wait for the [Observation] from the system ONLY AFTER you have output a tool block.

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
3. project/write_file (params: ""path"", ""content"") - Creates a NEW file.
4. project/edit_file (params: ""path"", ""search_block"", ""replace_block"") - Edits existing files. Use exact string matches for search_block.
5. scene/instantiate_node (params: ""type"", ""name"") - Creates a GameObject.
6. scene/modify_node (params: ""path"", ""property"", ""value"") - Edits a Scene GameObject OR a Project Prefab.
7. scene/inspect_node (params: ""path"") - Returns an object/prefab's components.
8. editor/read_console (params: none) - Returns the latest warnings/errors.
9. scene/list_all_nodes (params: none) - Returns a list of all root GameObjects.
10. scene/inspect_component (params: ""path"", ""component"") - Lists properties of a specific component.
11. scene/set_component_property (params: ""path"", ""component"", ""property"", ""value"") - Sets a property on a component.";

        public void ProcessPrompt(string prompt, string model, string turnId, Action<string, string, bool> onComplete)
        {
            Debug.Log($"[Omnisense-Diagnostics] --- NEW TURN STARTED: {turnId} ---");
            Debug.Log($"[Omnisense-Diagnostics] Processing prompt (Length: {prompt?.Length ?? 0} chars) with model: {model}");
            _currentTurnTrace.Clear();
            _currentTurnTrace.AppendLine("[System]: Analyzing request and classifying intent...");
            _turnToolCount = 0;
            _isReflecting = false;
            _isManagerEvaluating = false;
            _isPlanning = false;
            _isConceptualTurn = false;
            _isAborted = false;
            _stepCount = 0;
            _actionHistory.Clear();
            _turnContextLog.Clear();
            _lastWorkerResponse = "";
            _consecutiveManagerRejections = 0;
            _routingDecision = "planner";
            OmnisenseUndoManager.StartTurn(turnId);

            // Persistently add the user's prompt so the Worker and Manager can see the full context
            _history.Add(new ChatMessage { role = "user", content = prompt });
            SaveHistory();

            onComplete?.Invoke("[System]: Analyzing request and classifying intent...", _currentTurnTrace.ToString(), false);
            ExecuteRequest(model, onComplete);
        }

        private void RefreshSystemContext(string model)
        {
            _history.RemoveAll(m => m.role == "system");

            string rolePrompt = "";
            if (_routingDecision == "planner")
            {
                rolePrompt = PLANNER_SYSTEM_PROMPT;
            }
            else if (_routingDecision == "manager")
            {
                rolePrompt = MANAGER_SYSTEM_PROMPT;
            }
            else if (_routingDecision == "ui")
            {
                rolePrompt = UI_SPECIALIST_PROMPT;
            }
            else
            {
                rolePrompt = model == "self-hosted" ? SYSTEM_PROMPT_LITE : GENERIC_WORKER_PROMPT;
            }

            _history.Insert(0, new ChatMessage { role = "system", content = rolePrompt });

            try {
                string dnaPath = System.IO.Path.Combine(Application.dataPath, "..", ".omnisense_dna.md");
                if (System.IO.File.Exists(dnaPath)) {
                    string dnaContent = System.IO.File.ReadAllText(dnaPath);
                    _history.Insert(1, new ChatMessage { role = "system", content = $"[PROJECT DNA]\nThis is the persistent memory of this project. Conform to these architectural rules:\n\n{dnaContent}" });
                }
            } catch { }

            if (_persistentScratchpad.Count > 0)
            {
                string stateBanner = "[CURRENT ENVIRONMENT STATE]\n";
                foreach (var item in _persistentScratchpad.Distinct().Reverse().Take(15).Reverse())
                {
                    stateBanner += $"- {item}\n";
                }
                _history.Insert(2, new ChatMessage { role = "system", content = stateBanner });
            }
        }

        private void StartNextTask(string model, Action<string, string, bool> onComplete)
        {
            if (_pendingTasks.Count == 0)
            {
                Debug.Log("[Omnisense-Orchestration] StartNextTask called but queue is empty.");
                _routingDecision = "end";
                onComplete?.Invoke("[System]: All tasks in the execution plan have been successfully completed.", _currentTurnTrace.ToString(), true);
                return;
            }

            _currentTask = _pendingTasks.Dequeue();
            _stepCount = 0;
            _lastWorkerResponse = "";
            Debug.Log($"[Omnisense-Orchestration] Starting Sub-Task: {_currentTask}");
            _history.Add(new ChatMessage { role = "user", content = $"[Sub-Task]: {_currentTask}" });
            SaveHistory();
            
            string taskHeader = $"\n<color=#00FFFF><b>[Executing Task]:</b> {_currentTask}</color>\n";
            _currentTurnTrace.AppendLine(taskHeader);
            onComplete?.Invoke(taskHeader, _currentTurnTrace.ToString(), false);

            _routingDecision = "manager";
            string managerRoutePrompt = $"Review the current task: '{_currentTask}'. Which specialized agent is best suited to start this task? Route to 'ui_agent' or 'generic_agent'. Output ONLY valid JSON: {{\"routing\": \"ui_agent\" | \"generic_agent\", \"is_complete\": false, \"feedback\": \"Justification for routing\"}}";
            
            _history.Add(new ChatMessage { role = "user", content = managerRoutePrompt });
            SaveHistory();

            ExecuteRequest(model, onComplete);
        }

        public void Resume(string model, Action<string, string, bool> onComplete)
        {
            Debug.Log($"[Omnisense-Diagnostics] Resuming execution with model: {model}");
            ExecuteRequest(model, onComplete);
        }

        private void ExecuteRequest(string model, Action<string, string, bool> onComplete, List<ChatMessage> customHistory = null)
        {
            _stepCount++;
            if (_stepCount > MAX_STEPS)
            {
                string limitMsg = $"\n\n[System Warning]: Maximum turn iterations ({MAX_STEPS}) reached for this sub-task. To prevent an infinite loop, I have paused execution. Please review my progress and provide further instructions.";
                _currentTurnTrace.AppendLine(limitMsg);
                onComplete?.Invoke(limitMsg, _currentTurnTrace.ToString(), true);
                _history.Add(new ChatMessage { role = "assistant", content = "I have reached the execution limit for this sub-task. Pausing for user feedback." });
                SaveHistory();
                
                EditorPrefs.SetBool("Omnisense_AI_PendingResume", false);
                return;
            }

            if (customHistory == null)
            {
                RefreshSystemContext(model);
            }

            List<ChatMessage> activeHistory = customHistory ?? _history;
            Debug.Log($"[Omnisense-Diagnostics] ExecuteRequest invoked. History count before prune: {activeHistory.Count}");
            PruneHistory(activeHistory);
            Debug.Log($"[Omnisense-Diagnostics] History count after prune: {activeHistory.Count}. Retrieving API Key...");

            string apiKey = GetApiKey(model);
            if (string.IsNullOrEmpty(apiKey) && model != "self-hosted")
            {
                Debug.LogError("[Omnisense-Diagnostics] API Key is missing or empty.");
                onComplete?.Invoke("Error: API Key missing. Please set it in the Settings tab.", _currentTurnTrace.ToString(), true);
                return;
            }

            Debug.Log($"[Omnisense-Diagnostics] Dispatching payload to provider...");
            // Set pending state to allow auto-resume after assembly reload
            EditorPrefs.SetBool("Omnisense_AI_PendingResume", true);
            EditorPrefs.SetString("Omnisense_AI_LastModel", model);

            // Prepare request based on provider
            if (model.StartsWith("gpt") || model.StartsWith("o3"))
            {
                CallOpenAI(apiKey, model, onComplete, activeHistory);
            }
            else if (model.StartsWith("claude"))
            {
                CallAnthropic(apiKey, model, onComplete, activeHistory);
            }
            else if (model.StartsWith("gemini"))
            {
                CallGemini(apiKey, model, onComplete, activeHistory);
            }
            else if (model.StartsWith("grok"))
            {
                CallGrok(apiKey, model, onComplete, activeHistory);
            }
            else if (model == "self-hosted")
            {
                CallSelfHosted(apiKey, model, onComplete, activeHistory);
            }
            else
            {
                EditorPrefs.SetBool("Omnisense_AI_PendingResume", false);
                Debug.LogError($"[Omnisense] Unsupported model selected: {model}");
                onComplete?.Invoke($"Error: Unsupported model {model}", _currentTurnTrace.ToString(), true);
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

        private void CallOpenAI(string apiKey, string model, Action<string, string, bool> onComplete, List<ChatMessage> activeHistory)
        {
            if (_isAborted) return;
            // We previously injected an aggressive format reminder here, but it caused the LLM 
            // to hallucinate endless tool calls because it thought it was forced to output JSON.
            var payloadMessages = new List<ChatMessage>(activeHistory);

            int maxTokens = EditorPrefs.GetInt("Omnisense_OpenAI_MaxTokens", 4096);
            var requestData = new OpenAIRequest { model = model, messages = payloadMessages, max_completion_tokens = maxTokens };
            string json = JsonUtility.ToJson(requestData);

            _activeRequest = new UnityWebRequest("https://api.openai.com/v1/chat/completions", "POST");
            UnityWebRequest req = _activeRequest;
            req.timeout = 60; // 60 second timeout
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);

            Debug.Log($"[Omnisense] Sending request to OpenAI API using model {model} ({payloadMessages.Count} messages in history)...");
            var operation = req.SendWebRequest();
            operation.completed += (op) =>
            {
                if (req.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log("[Omnisense] Received successful response from API.");
                    string responseText = ExtractContent(req.downloadHandler.text);
                    HandleResponse(responseText, model, onComplete);
                }
                else if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
                {
                    string errorDetail = "";
                    try { errorDetail = req.downloadHandler?.text ?? ""; } catch { }
                    Debug.LogError($"[Omnisense] API Error: {req.error}\n{errorDetail}");
                    onComplete?.Invoke($"[System Error]: API Request failed ({req.result}).\nDetails: {req.error}\n{errorDetail}", _currentTurnTrace.ToString(), true);
                }
                else if (!_isAborted)
                {
                    onComplete?.Invoke($"[System Error]: Unexpected API failure ({req.result}).", _currentTurnTrace.ToString(), true);
                }
                
                req.Dispose();
                if (_activeRequest == req) _activeRequest = null;
            };
        }

        private void CallAnthropic(string apiKey, string model, Action<string, string, bool> onComplete, List<ChatMessage> activeHistory)
        {
            if (_isAborted) return;
            var payloadMessages = new List<ChatMessage>(activeHistory);
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
            UnityWebRequest req = _activeRequest;
            req.timeout = 60;
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("x-api-key", apiKey);
            req.SetRequestHeader("anthropic-version", "2023-06-01");

            Debug.Log($"[Omnisense] Sending request to Anthropic API using model {model} ({payloadMessages.Count} messages in history)...");
            var operation = req.SendWebRequest();
            operation.completed += (op) => {
                if (req.result == UnityWebRequest.Result.Success) {
                    // Anthropic response is different
                    string resp = req.downloadHandler.text;
                    // Simple extraction for now
                    var match = Regex.Match(resp, "\"text\":\"(.*?)\"", RegexOptions.Singleline);
                    HandleResponse(match.Success ? match.Groups[1].Value.Replace("\\n", "\n").Replace("\\\"", "\"") : resp, model, onComplete);
                } else if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError) {
                    string errorDetail = "";
                    try { errorDetail = req.downloadHandler?.text ?? ""; } catch { }
                    onComplete?.Invoke($"Anthropic Error: {req.error}\n{errorDetail}", _currentTurnTrace.ToString(), true);
                } else if (!_isAborted) {
                    onComplete?.Invoke($"Anthropic Error: Unexpected failure ({req.result})", _currentTurnTrace.ToString(), true);
                }
                
                req.Dispose();
                if (_activeRequest == req) _activeRequest = null;
            };
        }

        private void CallGemini(string apiKey, string model, Action<string, string, bool> onComplete, List<ChatMessage> activeHistory)
        {
            if (_isAborted) return;
            int maxTokens = EditorPrefs.GetInt("Omnisense_Gemini_MaxTokens", 4096);
            
            // Gemini JSON structure is nested: contents -> parts -> text
            string contentsJson = "[";
            for(int i=0; i<activeHistory.Count; i++) {
                string role = activeHistory[i].role == "assistant" ? "model" : "user";
                contentsJson += "{\"role\":\"" + role + "\",\"parts\":[{\"text\":\"" + activeHistory[i].content.Replace("\"", "\\\"").Replace("\n", "\\n") + "\"}]}";
                if(i < activeHistory.Count-1) contentsJson += ",";
            }
            contentsJson += "]";
            
            string json = "{\"contents\":" + contentsJson + ",\"generationConfig\":{\"maxOutputTokens\":" + maxTokens + "}}";

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            _activeRequest = new UnityWebRequest(url, "POST");
            UnityWebRequest req = _activeRequest;
            req.timeout = 60;
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            Debug.Log($"[Omnisense] Sending request to Gemini API using model {model} ({_history.Count} messages in history)...");
            var operation = req.SendWebRequest();
            operation.completed += (op) => {
                if (req.result == UnityWebRequest.Result.Success) {
                    string resp = req.downloadHandler.text;
                    var match = Regex.Match(resp, "\"text\":\\s*\"(.*?)\"", RegexOptions.Singleline);
                    HandleResponse(match.Success ? match.Groups[1].Value.Replace("\\n", "\n").Replace("\\\"", "\"") : resp, model, onComplete);
                } else if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError) {
                    string errorDetail = "";
                    try { errorDetail = req.downloadHandler?.text ?? ""; } catch { }
                    onComplete?.Invoke($"Gemini Error: {req.error}\n{errorDetail}", _currentTurnTrace.ToString(), true);
                } else if (!_isAborted) {
                    onComplete?.Invoke($"Gemini Error: Unexpected failure ({req.result})", _currentTurnTrace.ToString(), true);
                }
                
                req.Dispose();
                if (_activeRequest == req) _activeRequest = null;
            };
        }

        private void CallGrok(string apiKey, string model, Action<string, string, bool> onComplete, List<ChatMessage> activeHistory)
        {
            if (_isAborted) return;
            int maxTokens = EditorPrefs.GetInt("Omnisense_Grok_MaxTokens", 4096);
            var payloadMessages = new List<ChatMessage>(activeHistory);
            var requestData = new OpenAIRequest { model = model, messages = payloadMessages, max_completion_tokens = maxTokens };
            string json = JsonUtility.ToJson(requestData);

            _activeRequest = new UnityWebRequest("https://api.x.ai/v1/chat/completions", "POST");
            UnityWebRequest req = _activeRequest;
            req.timeout = 60;
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);

            Debug.Log($"[Omnisense] Sending request to Grok API using model {model} ({payloadMessages.Count} messages in history)...");
            var operation = req.SendWebRequest();
            operation.completed += (op) => {
                if (req.result == UnityWebRequest.Result.Success) {
                    string responseText = ExtractContent(req.downloadHandler.text);
                    HandleResponse(responseText, model, onComplete);
                } else if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError) {
                    string errorDetail = "";
                    try { errorDetail = req.downloadHandler?.text ?? ""; } catch { }
                    onComplete?.Invoke($"Grok Error: {req.error}\n{errorDetail}", _currentTurnTrace.ToString(), true);
                } else if (!_isAborted) {
                    onComplete?.Invoke($"Grok Error: Unexpected failure ({req.result})", _currentTurnTrace.ToString(), true);
                }
                
                req.Dispose();
                if (_activeRequest == req) _activeRequest = null;
            };
        }

        private void CallSelfHosted(string apiKey, string model, Action<string, string, bool> onComplete, List<ChatMessage> activeHistory)
        {
            if (_isAborted) return;
            string baseUrl = EditorPrefs.GetString("Omnisense_SelfHosted_URL", "http://localhost:11434/v1");
            if (string.IsNullOrEmpty(baseUrl)) baseUrl = "http://localhost:11434/v1";
            
            // Normalize URL to ensure it ends with /chat/completions
            string endpoint = baseUrl;
            if (!endpoint.EndsWith("/chat/completions")) {
                if (endpoint.EndsWith("/")) endpoint += "chat/completions";
                else endpoint += "/chat/completions";
            }

            string targetModel = EditorPrefs.GetString("Omnisense_SelfHosted_Model", "llama3:8b");
            int maxTokens = EditorPrefs.GetInt("Omnisense_SelfHosted_MaxTokens", 4096);
            var payloadMessages = new List<ChatMessage>(activeHistory);
            
            var requestData = new OpenAIRequest { model = targetModel, messages = payloadMessages, max_completion_tokens = maxTokens };
            string json = JsonUtility.ToJson(requestData);

            _activeRequest = new UnityWebRequest(endpoint, "POST");
            _activeRequest.timeout = 120; // Local models can be slow
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            _activeRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            _activeRequest.downloadHandler = new DownloadHandlerBuffer();
            _activeRequest.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(apiKey))
            {
                _activeRequest.SetRequestHeader("Authorization", "Bearer " + apiKey);
            }

            Debug.Log($"[Omnisense] Sending request to Self-Hosted API ({endpoint}) using model {targetModel}...");
            var operation = _activeRequest.SendWebRequest();
            operation.completed += (op) => {
                if (_isAborted || _activeRequest == null) return;
                if (_activeRequest.result == UnityWebRequest.Result.Success) {
                    string responseText = ExtractContent(_activeRequest.downloadHandler.text);
                    HandleResponse(responseText, model, onComplete);
                } else {
                    string errorDetail = "";
                    try { errorDetail = _activeRequest.downloadHandler?.text ?? ""; } catch { }
                    onComplete?.Invoke($"Self-Hosted Error: {_activeRequest.error}\n{errorDetail}", _currentTurnTrace.ToString(), true);
                }
                _activeRequest?.Dispose(); _activeRequest = null;
            };
        }

        public void TestSelfHostedConnection()
        {
            string baseUrl = EditorPrefs.GetString("Omnisense_SelfHosted_URL", "http://localhost:11434/v1");
            string apiKey = EditorPrefs.GetString("Omnisense_SelfHosted_Key", "");
            string targetModel = EditorPrefs.GetString("Omnisense_SelfHosted_Model", "llama3:8b");
            
            string endpoint = baseUrl;
            if (!endpoint.EndsWith("/chat/completions")) {
                endpoint += endpoint.EndsWith("/") ? "chat/completions" : "/chat/completions";
            }

            var requestData = new OpenAIRequest { 
                model = targetModel, 
                messages = new List<ChatMessage> { new ChatMessage { role = "user", content = "Ping." } },
                max_completion_tokens = 5
            };
            
            var req = new UnityWebRequest(endpoint, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(JsonUtility.ToJson(requestData));
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(apiKey)) req.SetRequestHeader("Authorization", "Bearer " + apiKey);

            Debug.Log($"[Omnisense] Testing Self-Hosted connection at {endpoint}...");
            req.SendWebRequest().completed += (op) => {
                if (req.result == UnityWebRequest.Result.Success) {
                    Debug.Log("[Omnisense] Self-Hosted connection SUCCESS!");
                    EditorUtility.DisplayDialog("Connection Test", "Successfully connected to the local runner and model!", "OK");
                } else {
                    Debug.LogError($"[Omnisense] Self-Hosted connection failed: {req.error}");
                    EditorUtility.DisplayDialog("Connection Test Failed", $"Failed to connect to local runner.\n\nError: {req.error}\n\nPlease check your URL and make sure the server is running.", "OK");
                }
                req.Dispose();
            };
        }

        public void FetchSelfHostedModels()
        {
            string baseUrl = EditorPrefs.GetString("Omnisense_SelfHosted_URL", "http://localhost:11434/v1");
            string apiKey = EditorPrefs.GetString("Omnisense_SelfHosted_Key", "");
            
            string endpoint = baseUrl;
            if (endpoint.EndsWith("/chat/completions")) endpoint = endpoint.Substring(0, endpoint.Length - 17);
            if (!endpoint.EndsWith("/models")) {
                endpoint += endpoint.EndsWith("/") ? "models" : "/models";
            }

            var req = UnityWebRequest.Get(endpoint);
            if (!string.IsNullOrEmpty(apiKey)) req.SetRequestHeader("Authorization", "Bearer " + apiKey);

            Debug.Log($"[Omnisense] Fetching models from {endpoint}...");
            req.SendWebRequest().completed += (op) => {
                if (req.result == UnityWebRequest.Result.Success) {
                    try {
                        var json = req.downloadHandler.text;
                        var modelsData = JsonUtility.FromJson<ModelsResponse>(json);
                        if (modelsData != null && modelsData.data != null && modelsData.data.Count > 0) {
                            string modelList = "";
                            foreach(var m in modelsData.data) modelList += $"- {m.id}\n";
                            EditorUtility.DisplayDialog("Available Models", $"Found {modelsData.data.Count} models:\n\n{modelList}", "OK");
                        } else {
                            EditorUtility.DisplayDialog("Available Models", "Request succeeded, but couldn't parse the model list automatically. Check the console for the raw JSON.", "OK");
                            Debug.Log($"[Omnisense] Raw models response: {json}");
                        }
                    } catch {
                        EditorUtility.DisplayDialog("Available Models", "Failed to parse models. Check console for raw output.", "OK");
                        Debug.Log($"[Omnisense] Raw models response: {req.downloadHandler.text}");
                    }
                } else {
                    EditorUtility.DisplayDialog("Fetch Failed", $"Failed to fetch models.\n\nError: {req.error}", "OK");
                }
                req.Dispose();
            };
        }

        [Serializable]
        private class ModelsResponse { public List<ModelData> data; }
        [Serializable]
        private class ModelData { public string id; }

        public event Action<string, Action<bool>> OnPendingAction;

        [Serializable]
        public class ManagerDecision
        {
            public string routing;
            public bool is_complete;
            public string feedback;
        }

        private void HandleResponse(string response, string model, Action<string, string, bool> onComplete)
        {
            if (_routingDecision == "planner")
            {
                Debug.Log($"[Omnisense-Orchestration] Planner Response Received. Parsing Task List...");
                _currentTurnTrace.AppendLine("[System]: Planner Response Received. Parsing Task List...");

                bool requiresTools = true;
                try 
                {
                    string json = response.Replace("```json", "").Replace("```", "").Trim();
                    int startIdx = json.IndexOf('{');
                    int endIdx = json.LastIndexOf('}');
                    if (startIdx >= 0 && endIdx >= startIdx) 
                    {
                        json = json.Substring(startIdx, endIdx - startIdx + 1);
                        var plan = JsonUtility.FromJson<PlannerResponse>(json);
                        if (plan != null) 
                        {
                            requiresTools = plan.requires_tools;
                            if (!requiresTools && (plan.intent == "conceptual_q_and_a" || plan.intent == "general_knowledge"))
                            {
                                Debug.Log($"[Omnisense-Orchestration] Intent classified as conceptual. Bypassing tool loop.");
                                _isConceptualTurn = true;
                                _routingDecision = "end";
                                _history.Add(new ChatMessage { role = "assistant", content = "<b>[Manager] Classified as General Knowledge. Bypassing tool execution.</b>" });
                                SaveHistory();
                                
                                _history.Add(new ChatMessage { role = "user", content = "[System]: The user has asked a conceptual or general question. You do not need to use tools to answer this. Please answer the user directly and comprehensively in plain text. DO NOT output a tool block." });
                                SaveHistory();
                                
                                _currentTurnTrace.AppendLine("<b>[Manager] Classified as General Knowledge. Bypassing tool execution.</b>");
                                onComplete?.Invoke("\n<b>[Manager] Classified as General Knowledge. Bypassing tool execution...</b>\n\n[System]: Generating response...", _currentTurnTrace.ToString(), false);
                                ExecuteRequest(model, onComplete);
                                return;
                            }
                            else if (plan.tasks != null)
                            {
                                foreach (var t in plan.tasks) _pendingTasks.Enqueue(t);
                                Debug.Log($"[Omnisense-Orchestration] Plan Parsed Successfully: {_pendingTasks.Count} sub-tasks queued.");
                            }
                        }
                    }
                } 
                catch { Debug.LogWarning("[Omnisense] Failed to parse Execution Plan JSON. Defaulting to single task."); }

                if (_pendingTasks.Count == 0)
                {
                    _pendingTasks.Enqueue("Execute the user's request.");
                }

                string planUi = "<b>[Manager] Execution Plan Created:</b>\n";
                int idx = 1;
                foreach (var t in _pendingTasks) planUi += $"{idx++}. {t}\n";
                
                _history.Add(new ChatMessage { role = "assistant", content = planUi });
                SaveHistory();

                _currentTurnTrace.AppendLine(planUi);

                _routingDecision = "manager";
                StartNextTask(model, onComplete);
                return;
            }

            if (_routingDecision == "manager")
            {
                Debug.Log("[Omnisense-Orchestration] Manager Response Received. Evaluating routing/completion...");
                _currentTurnTrace.AppendLine("[System]: Manager Response Received. Evaluating routing/completion...");

                string nextRouting = "generic";
                bool isComplete = false;
                string feedback = "";

                try 
                {
                    string json = response.Replace("```json", "").Replace("```", "").Trim();
                    int startIdx = json.IndexOf('{');
                    int endIdx = json.LastIndexOf('}');
                    if (startIdx >= 0 && endIdx >= startIdx) 
                    {
                        json = json.Substring(startIdx, endIdx - startIdx + 1);
                        var eval = JsonUtility.FromJson<ManagerDecision>(json);
                        if (eval != null) 
                        {
                            nextRouting = eval.routing ?? "generic";
                            isComplete = eval.is_complete;
                            feedback = eval.feedback ?? "";
                        }
                    }
                } 
                catch 
                {
                    Debug.LogWarning("[Omnisense] Failed to parse Manager Decision. Defaulting to standard routing.");
                }

                if (isComplete || nextRouting == "end")
                {
                    Debug.Log("[Omnisense-Orchestration] Manager Approved Task Completion.");
                    _turnToolCount = 0;
                    _isReflecting = false;
                    _consecutiveManagerRejections = 0;
                    PruneHistory();
                    
                    _history.Add(new ChatMessage { role = "assistant", content = $"<thought>Manager approved sub-task completion.</thought> {feedback}" });

                    if (_turnContextLog.Count > 0)
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"[Turn Summary] Task: '{_currentTask}'");
                        sb.AppendLine("The agent successfully completed the following actions:");
                        foreach (var entry in _turnContextLog.Distinct())
                            sb.AppendLine($"  - {entry}");
                        _history.Add(new ChatMessage { role = "system", content = sb.ToString().Trim() });
                    }

                    SaveHistory();
                    
                    if (_pendingTasks.Count > 0)
                    {
                        Debug.Log($"[Omnisense-Orchestration] Moving to next sub-task. ({_pendingTasks.Count} remaining)");
                        string subTaskResult = !string.IsNullOrEmpty(_lastWorkerResponse) 
                             ? $"{_lastWorkerResponse}\n\n<color=#00FF00><b>[Manager Approved Sub-Task]:</b></color> {feedback}" 
                            : $"<color=#00FF00><b>[Manager Approved Sub-Task]:</b></color> {feedback}";
                        
                        _currentTurnTrace.AppendLine($"[Manager Approved Sub-Task]: {feedback}");
                        onComplete?.Invoke(subTaskResult, _currentTurnTrace.ToString(), false);
                        StartNextTask(model, onComplete);
                    }
                    else
                    {
                        Debug.Log("[Omnisense-Orchestration] All tasks approved. Loop terminating.");
                        _routingDecision = "end";
                        string finalResult = !string.IsNullOrEmpty(_lastWorkerResponse) 
                            ? $"{_lastWorkerResponse}\n\n<color=#00FF00><b>[Manager Approved]: All tasks complete.</b></color>\n{feedback}" 
                            : $"<color=#00FF00><b>[Manager Approved]: All tasks complete.</b></color>\n{feedback}";
                        
                        _currentTurnTrace.AppendLine($"[Manager Approved]: All tasks complete. {feedback}");
                        onComplete?.Invoke(finalResult, _currentTurnTrace.ToString(), true);
                        
                        EditorPrefs.SetBool("Omnisense_AI_PendingResume", false);
                    }
                }
                else
                {
                    bool isInitialRouting = string.IsNullOrEmpty(_lastWorkerResponse);

                    if (isInitialRouting)
                    {
                        _routingDecision = nextRouting == "ui_agent" ? "ui" : "generic";
                        Debug.Log($"[Omnisense-Orchestration] Manager routed initial task to: {_routingDecision}");
                        
                        string routeMsg = $"<b>[Manager] Task routed to specialized {(_routingDecision == "ui" ? "UI Specialist" : "Generic Architect")} Agent.</b>";
                        _currentTurnTrace.AppendLine(routeMsg);
                        onComplete?.Invoke(routeMsg, _currentTurnTrace.ToString(), false);
                        ExecuteRequest(model, onComplete);
                    }
                    else
                    {
                        _consecutiveManagerRejections++;
                        Debug.Log($"[Omnisense-Orchestration] Manager REJECTED Completion. Feedback: {feedback} (Rejections: {_consecutiveManagerRejections})");
                        
                        if (_consecutiveManagerRejections >= 3)
                        {
                            _pendingTasks.Clear();
                            _consecutiveManagerRejections = 0;
                            _routingDecision = "end";
                            EditorPrefs.SetBool("Omnisense_AI_PendingResume", false);
                            
                            string limitMsg = "\n\n[System Intervention]: The Manager has rejected this task 3 times consecutively. Paused to prevent death loop. Review feedback and guide the agent manually.";
                            _currentTurnTrace.AppendLine(limitMsg);
                            onComplete?.Invoke($"<color=#FF5555><b>[System Intervention]:</b></color> The Manager has rejected this task 3 times consecutively. Paused to prevent a death loop. Review feedback and guide the agent manually.\n\nFeedback: {feedback}", _currentTurnTrace.ToString(), true);
                            _history.Add(new ChatMessage { role = "assistant", content = "Manager has rejected this task 3 times consecutively. Pausing for user feedback." });
                            SaveHistory();
                            return;
                        }

                        _routingDecision = nextRouting == "ui_agent" ? "ui" : "generic";
                        _history.Add(new ChatMessage { role = "user", content = $"[Manager Audit Failed]: The Manager detected that the task is incomplete. Feedback: {feedback}\n\nSYSTEM DIRECTIVE: Review the feedback above, plan how to resolve the missing requirements, and immediately apply the changes. You must write/edit code or hierarchy to complete the task." });
                        SaveHistory();
                        
                        _currentTurnTrace.AppendLine($"[Manager Rejected Sub-Task]: {feedback}. Re-routing to specialized agent...");
                        onComplete?.Invoke($"<color=#FF5555><b>[Manager Rejected]:</b></color> {feedback}\nResuming execution with specialized agent...", _currentTurnTrace.ToString(), false);
                        ExecuteRequest(model, onComplete);
                    }
                }
                return;
            }

            _history.Add(new ChatMessage { role = "assistant", content = response });
            SaveHistory();
            _currentTurnTrace.AppendLine($"[Assistant]\n{response}\n");

            // Loop detection on identical consecutive responses (Cognitive Loop Detector)
            int assistantMsgCount = _history.Count(m => m.role == "assistant");
            if (assistantMsgCount >= 3)
            {
                var assistantMsgs = _history.Where(m => m.role == "assistant").Reverse().Take(3).ToList();
                if (assistantMsgs[0].content == assistantMsgs[1].content && assistantMsgs[1].content == assistantMsgs[2].content)
                {
                    _pendingTasks.Clear();
                    string loopMsg = "\n\n[System Intervention]: Identical consecutive worker responses detected. Pausing to prevent cognitive death loop. Please check your instructions.";
                    _currentTurnTrace.AppendLine(loopMsg);
                    onComplete?.Invoke($"<color=#FF5555><b>[System Intervention]:</b></color> Identical consecutive worker responses detected. Pausing to prevent a cognitive death loop.\n\nResponse:\n{response}", _currentTurnTrace.ToString(), true);
                    
                    // Reset pending state
                    EditorPrefs.SetBool("Omnisense_AI_PendingResume", false);
                    return;
                }
            }

            string toolJson = ExtractToolCall(response);
            if (!string.IsNullOrEmpty(toolJson))
            {
                _turnToolCount++;
                string uiResponse = response.Replace("```mcp_json", "[Executing Tool...]").Replace("```", "");
                string thought = ExtractThought(response);
                if (!string.IsNullOrEmpty(thought)) uiResponse = $"<thought>{thought}</thought>\n\n[System]: Actioning your request...";
                onComplete?.Invoke(uiResponse, _currentTurnTrace.ToString(), false);

                try
                {
                    var toolCall = JsonUtility.FromJson<MCPToolRequest>(toolJson);
                    
                    bool isDestructive = toolCall.method == "project/write_file" ||
                                         toolCall.method == "project/edit_file" ||
                                         toolCall.method == "project/create_prefab" ||
                                         toolCall.method == "project/create_tag_or_layer" ||
                                         toolCall.method == "scene/instantiate_node" ||
                                         toolCall.method == "scene/modify_node" ||
                                         toolCall.method == "scene/set_component_property" ||
                                         toolCall.method == "scene/execute_transactions" ||
                                         toolCall.method == "ui/setup_canvas" ||
                                         toolCall.method == "ui/create_panel" ||
                                         toolCall.method == "ui/create_text" ||
                                         toolCall.method == "ui/create_button" ||
                                         toolCall.method == "ui/setup_layout_group";

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
                                _currentTurnTrace.AppendLine("[Observation]\nUser rejected this change.");
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
                    _history.Add(new ChatMessage { role = "user", content = $"[System Error]\nFailed to parse tool request. Ensure your JSON is strictly formatted and enclosed in correct markdown blocks. Do NOT output multiple JSON blocks at once. Only one tool call is allowed per response.\nDetails: {e.Message}" });
                    SaveHistory();
                    _currentTurnTrace.AppendLine($"[System Error]: Failed to parse tool request: {e.Message}");
                    ExecuteRequest(model, onComplete);
                }
            }
            else
            {
                if (_isConceptualTurn)
                {
                    Debug.Log("[Omnisense] Conceptual turn complete.");
                    onComplete?.Invoke(response, _currentTurnTrace.ToString(), true);
                    return;
                }

                string extractedThought = ExtractThought(response);
                string textWithoutThought = string.IsNullOrEmpty(extractedThought) ? response : response.Replace($"<thought>{extractedThought}</thought>", "").Trim();
                
                // If the agent barely said anything outside the thought block, it's a phantom turn.
                bool isPhantomTurn = textWithoutThought.Length < 30;

                if (isPhantomTurn)
                {
                    if (_isReflecting)
                    {
                        Debug.Log("[Omnisense] Phantom turn during reflection. Nudging model to finalize.");
                        _history.Add(new ChatMessage { role = "user", content = "[System]\nYou output a thought block but no summary or tool call. If you are finished, you MUST summarize your work to the user in plain text. If you need to fix something, output a valid ```mcp_json tool block. NEVER stop generating without providing a tool or a summary." });
                        SaveHistory();
                        string nudgeUiResponse = !string.IsNullOrEmpty(extractedThought) ? $"<thought>{extractedThought}</thought>\n\n[System]: Nudging agent to finalize reflection..." : "[System]: Nudging agent to finalize reflection...";
                        onComplete?.Invoke(nudgeUiResponse, _currentTurnTrace.ToString(), false);
                        ExecuteRequest(model, onComplete);
                    }
                    else
                    {
                        Debug.Log("[Omnisense] Phantom turn detected. Nudging the model...");
                        _history.Add(new ChatMessage { role = "user", content = "[System]\nYou created a plan but did not execute a tool. Please execute your plan by outputting a valid ```mcp_json tool block in your very next response. NEVER stop generating." });
                        SaveHistory();
                        string nudgeUiResponse = !string.IsNullOrEmpty(extractedThought) ? $"<thought>{extractedThought}</thought>\n\n[System]: Nudging agent to execute plan..." : "[System]: Nudging agent to execute plan...";
                        onComplete?.Invoke(nudgeUiResponse, _currentTurnTrace.ToString(), false);
                        ExecuteRequest(model, onComplete);
                    }
                }
                else if (_turnToolCount > 0 && !_isReflecting)
                {
                    Debug.Log("[Omnisense] Triggering proactive reflection turn...");
                    _isReflecting = true;
                    _history.Add(new ChatMessage { role = "user", content = "[System Audit]: If you are finished with the user's request, review your changes: Are there any null references, missing components, or obvious next steps to make the feature functional? If yes, execute tools to fix them. If you are NOT finished, please continue your work by using a tool. If everything is done, summarize your work to the user." });
                    SaveHistory();
                    onComplete?.Invoke(response + "\n\n[System]: Auditing changes and finalizing...", _currentTurnTrace.ToString(), false);
                    ExecuteRequest(model, onComplete);
                }
                else
                {
                    Debug.Log("[Omnisense] Worker thinks it is done. Triggering Manager Evaluator...");
                    _isManagerEvaluating = true;
                    _lastWorkerResponse = response;
                    
                    string managerAuditPrompt = $"MANAGER AUDIT: You are the Manager Agent. Review the chat history and evaluate the Worker's execution of the CURRENT SUB-TASK: '{_currentTask}'. Did the worker successfully complete this specific sub-task? Do NOT evaluate against the entire user request, ONLY evaluate if this specific sub-task is done. Do not be overly pedantic about terminology. If the worker provides tool output (such as InspectNode) that reasonably shows the requested change exists, approve the task. Remember that Unity Prefab Assets are often displayed as 'GameObjects' or '[Prefab Asset]' in tool outputs. Output ONLY valid JSON in this exact format: {{\"is_complete\": true/false, \"feedback\": \"If false, list exactly what is missing from THIS sub-task. If true, summarize the success.\"}}";
                    
                    var managerHistory = new List<ChatMessage>(_history);
                    managerHistory.Add(new ChatMessage { role = "user", content = managerAuditPrompt });
                    
                    onComplete?.Invoke(response + "\n\n[System]: Manager is verifying completion...", _currentTurnTrace.ToString(), false);
                    ExecuteRequest(model, onComplete, managerHistory);
                }
            }
        }

        private void PruneHistory(List<ChatMessage> history = null)
        {
            if (history == null) history = _history;

            // 1. LEVELED SLIDING WINDOW: Pin core System Prompts, DNA, Environment State and the first User prompt,
            // while pruning intermediate middle tool calls and observations to protect conversational context.
            
            // Find the initial user message (first user message sent that is not a system control nudge/subtask)
            ChatMessage firstUserMsg = null;
            foreach (var msg in history)
            {
                if (msg.role == "user" && !msg.content.StartsWith("[Observation]") && !msg.content.StartsWith("[Sub-Task]:") && !msg.content.StartsWith("[Manager Audit Failed]:"))
                {
                    firstUserMsg = msg;
                    break;
                }
            }

            // Create a list of pinned messages
            List<ChatMessage> pinned = new List<ChatMessage>();
            ChatMessage latestSubTask = null;
            ChatMessage latestManagerFailed = null;

            foreach (var msg in history)
            {
                // Pin core system setup messages
                if (msg.role == "system" && (msg.content.StartsWith("You are the Omnisense") || msg.content.StartsWith("You are the AI") || msg.content.StartsWith("[PROJECT DNA]") || msg.content.StartsWith("[CURRENT ENVIRONMENT STATE]")))
                {
                    pinned.Add(msg);
                }
                if (msg.role == "user" && msg.content.StartsWith("[Sub-Task]:"))
                {
                    latestSubTask = msg;
                }
                if (msg.role == "user" && msg.content.StartsWith("[Manager Audit Failed]:"))
                {
                    latestManagerFailed = msg;
                }
            }
            if (firstUserMsg != null && !pinned.Contains(firstUserMsg))
            {
                pinned.Add(firstUserMsg);
            }
            if (latestSubTask != null && !pinned.Contains(latestSubTask))
            {
                pinned.Add(latestSubTask);
            }
            if (latestManagerFailed != null && !pinned.Contains(latestManagerFailed))
            {
                pinned.Add(latestManagerFailed);
            }

            int targetThreshold = 30; // Max allowed history messages before leveled pruning
            if (history.Count > targetThreshold)
            {
                List<ChatMessage> optimizedHistory = new List<ChatMessage>();
                
                // Add all pinned items first
                foreach (var pMsg in pinned)
                {
                    optimizedHistory.Add(pMsg);
                }

                // Add the last 20 messages (excluding what is already pinned)
                int lastNCount = 20;
                int startIdx = Mathf.Max(0, history.Count - lastNCount);
                List<ChatMessage> recentMessages = new List<ChatMessage>();
                for (int i = startIdx; i < history.Count; i++)
                {
                    if (!pinned.Contains(history[i]))
                    {
                        recentMessages.Add(history[i]);
                    }
                }
                optimizedHistory.AddRange(recentMessages);

                int removedCount = history.Count - optimizedHistory.Count;
                if (removedCount > 0)
                {
                    if (history == _history)
                    {
                        _history = optimizedHistory;
                    }
                    else
                    {
                        history.Clear();
                        history.AddRange(optimizedHistory);
                    }
                    Debug.Log($"[Omnisense] Leveled Sliding Window active: Pruned {removedCount} intermediate messages. Pins: {pinned.Count}, Recents: {recentMessages.Count}");
                }
            }

            // 2. BI-DIRECTIONAL CONTENT PRUNING: Truncate large blocks in BOTH User and Assistant roles.
            for (int i = 0; i < history.Count; i++)
            {
                if (history[i].role == "system" || history[i] == firstUserMsg || i > history.Count - 15) continue;

                if (history[i].role == "user" && history[i].content.Length > 1500)
                {
                    if (history[i].content.StartsWith("[Observation]"))
                    {
                        int length = history[i].content.Length;
                        string head = history[i].content.Substring(0, 1000);
                        string tail = history[i].content.Substring(length - 500);
                        history[i].content = $"{head}\n\n... [Truncated {length - 1500} characters to preserve context window] ...\n\n{tail}";
                    }
                }
                else if (history[i].role == "assistant" && history[i].content.Length > 3000)
                {
                    string snippet = history[i].content.Substring(0, 3000);
                    history[i].content = $"{snippet}...\n\n(Previous large output/code truncated to prevent context saturation).";
                }
            }
        }

        private string GenerateDiffSummary(MCPToolRequest toolCall, string toolJson)
        {
            if (toolCall.@params == null) return "Pending changes...";
            
            if (toolCall.method == "ui/setup_canvas")
                return "<color=#00FF00>+ Setup Canvas:</color> Create high-performance UI Canvas & EventSystem";
            if (toolCall.method == "ui/create_panel")
                return $"<color=#00FF00>+ Create Panel:</color> Create '{toolCall.@params.name}' under parent '{toolCall.@params.parentPath}'";
            if (toolCall.method == "ui/create_text")
                return $"<color=#00FF00>+ Create Text:</color> Create '{toolCall.@params.name}' with text '{toolCall.@params.textContent}'";
            if (toolCall.method == "ui/create_button")
                return $"<color=#00FF00>+ Create Button:</color> Create '{toolCall.@params.name}' with label '{toolCall.@params.labelText}'";
            if (toolCall.method == "ui/setup_layout_group")
                return $"<color=#FFFF00>~ Layout Group:</color> Configure '{toolCall.@params.groupType}' on '{toolCall.@params.path}'";
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
            if (toolCall.method == "scene/execute_transactions")
            {
                if (toolCall.@params.operations == null || toolCall.@params.operations.Count == 0)
                {
                    toolCall.@params.operations = ParseOperationsFallback(toolJson);
                }
                
                if (toolCall.@params.operations == null || toolCall.@params.operations.Count == 0)
                {
                    return "<color=#FFFF00>~ Execute Transactions:</color> Empty transaction list.";
                }
                var list = new List<string>();
                foreach (var op in toolCall.@params.operations)
                {
                    string act = (!string.IsNullOrEmpty(op.action) ? op.action : op.tool)?.ToLower() ?? "";
                    if (act == "instantiate_node" || act == "scene/instantiate_node")
                        list.Add($"  + Instantiate: {op.type} as '{op.name}'");
                    else if (act == "modify_node" || act == "scene/modify_node")
                        list.Add($"  ~ Modify Node '{op.path}': set {op.property} = '{op.value}'");
                    else if (act == "add_component" || act == "addcomponent")
                        list.Add($"  + Add Component: {op.component ?? op.value} on '{op.path}'");
                    else if (act == "remove_component" || act == "removecomponent")
                        list.Add($"  - Remove Component: {op.component ?? op.value} from '{op.path}'");
                    else if (act == "set_component_property" || act == "scene/set_component_property" || act == "set_property" || act == "setproperty")
                        list.Add($"  ~ Set Property: {op.component}.{op.property} = '{op.value}' on '{op.path}'");
                    else if (act == "add_child")
                        list.Add($"  + Add Child '{op.name}' to '{op.parent ?? op.path}' with components [{(op.components != null ? string.Join(", ", op.components) : op.component)}]");
                }
                return $"<color=#FFFF00>~ Execute Transactions (Batched {toolCall.@params.operations.Count} operations):</color>\n{string.Join("\n", list)}";
            }
            return "Pending changes...";
        }

        private async void ExecuteToolAndResume(MCPToolRequest toolCall, string toolJson, string uiResponse, string model, Action<string, string, bool> onComplete)
        {

            // Loop Detection
            string actionSignature = $"{toolCall.method}:{JsonUtility.ToJson(toolCall.@params)}";
            _actionHistory.Add(actionSignature);
            if (_actionHistory.Count >= 3)
            {
                int n = _actionHistory.Count;
                if (_actionHistory[n-1] == _actionHistory[n-2] && _actionHistory[n-2] == _actionHistory[n-3])
                {
                    _pendingTasks.Clear();
                    string overrideMsg = "[System Intervention]: Redundant tool execution loop detected. The current task is either already complete or impossible with current tools. The task queue has been flushed. Summarize the current state for the user and await further instructions.";
                    _history.Add(new ChatMessage { role = "user", content = overrideMsg });
                    SaveHistory();
                    
                    _currentTurnTrace.AppendLine("\n[System Intervention]: Loop detected. Flushing task queue.\n");
                    onComplete?.Invoke(uiResponse + "\n\n[System Intervention]: Loop detected. Flushing task queue and requesting summary...", _currentTurnTrace.ToString(), false);
                    ExecuteRequest(model, onComplete);
                    return;
                }
            }

            MCPToolRegistry.ToolResult result = null;
            var p = toolCall.@params ?? new MCPToolParams();

            _currentTurnTrace.AppendLine($"[Executing Tool...]\n{toolCall.method}: {JsonUtility.ToJson(p)}\n");

            try
            {
                if (toolCall.method == "project/write_file")
                {
                    result = MCPToolRegistry.WriteFile(p.path, p.content);
                    onComplete?.Invoke(uiResponse + "\n\n[System]: File written. Waiting for Unity to compile...", _currentTurnTrace.ToString(), false);
                }
                else if (toolCall.method == "project/edit_file")
                {
                    result = MCPToolRegistry.EditFile(p.path, p.search_block, p.replace_block);
                    onComplete?.Invoke(uiResponse + "\n\n[System]: File edited. Waiting for Unity to compile...", _currentTurnTrace.ToString(), false);
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
                else if (toolCall.method == "scene/inspect_component")
                    result = MCPToolRegistry.InspectComponent(p.path, p.component);
                else if (toolCall.method == "ui/setup_canvas")
                {
                    result = MCPToolRegistry.SetupCanvas();
                    onComplete?.Invoke(uiResponse + "\n\n[System]: Configuring Canvas...", _currentTurnTrace.ToString(), false);
                }
                else if (toolCall.method == "ui/create_panel")
                {
                    result = MCPToolRegistry.CreateUIPanel(p.parentPath, p.name);
                    onComplete?.Invoke(uiResponse + "\n\n[System]: Instantiating UI Panel...", _currentTurnTrace.ToString(), false);
                }
                else if (toolCall.method == "ui/create_text")
                {
                    result = MCPToolRegistry.CreateUIText(p.parentPath, p.name, p.textContent, p.fontSize == 0 ? 24 : p.fontSize, string.IsNullOrEmpty(p.alignment) ? "Center" : p.alignment);
                    onComplete?.Invoke(uiResponse + "\n\n[System]: Instantiating UI Text...", _currentTurnTrace.ToString(), false);
                }
                else if (toolCall.method == "ui/create_button")
                {
                    result = MCPToolRegistry.CreateUIButton(p.parentPath, p.name, p.labelText);
                    onComplete?.Invoke(uiResponse + "\n\n[System]: Instantiating UI Button...", _currentTurnTrace.ToString(), false);
                }
                else if (toolCall.method == "ui/setup_layout_group")
                {
                    result = MCPToolRegistry.SetupLayoutGroup(p.path, p.groupType, p.spacing, string.IsNullOrEmpty(p.paddingCSV) ? "10,10,10,10" : p.paddingCSV, string.IsNullOrEmpty(p.childAlignment) ? "UpperLeft" : p.childAlignment);
                    onComplete?.Invoke(uiResponse + "\n\n[System]: Configuring Layout Group...", _currentTurnTrace.ToString(), false);
                }
                else if (toolCall.method == "scene/set_component_property")
                    result = MCPToolRegistry.SetComponentProperty(p.path, p.component, p.property, p.value);
                else if (toolCall.method == "scene/execute_transactions")
                {
                    if (p.operations == null || p.operations.Count == 0)
                    {
                        p.operations = ParseOperationsFallback(toolJson);
                    }
                    result = MCPToolRegistry.ExecuteTransactions(p.operations);
                }
                else if (toolCall.method == "editor/read_console")
                    result = MCPToolRegistry.ReadConsole();
                else if (toolCall.method == "project/create_prefab")
                    result = MCPToolRegistry.CreatePrefab(p.path, p.destinationAssetPath);
                else if (toolCall.method == "project/create_tag_or_layer")
                    result = MCPToolRegistry.CreateTagOrLayer(p.type, p.name);
                else if (toolCall.method == "project/list_tags_and_layers")
                    result = MCPToolRegistry.ListTagsAndLayers();
                else if (toolCall.method == "project/search_assets")
                    result = MCPToolRegistry.SearchAssets(p.query);
                else if (toolCall.method == "project/inspect_player_settings")
                    result = MCPToolRegistry.InspectPlayerSettings();
                else if (toolCall.method == "project/list_packages")
                    result = MCPToolRegistry.ListPackages();
                else if (toolCall.method == "scene/list_all_nodes")
                    result = MCPToolRegistry.ListAllNodes();
                else if (toolCall.method == "project/inspect_build_settings")
                    result = MCPToolRegistry.InspectBuildSettings();
                else if (toolCall.method == "project/get_asset_guid")
                    result = MCPToolRegistry.GetAssetGUID(p.path);
                else
                    result = new MCPToolRegistry.ToolResult { success = false, error = "Unknown tool: " + toolCall.method };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Omnisense] Unhandled exception during tool execution '{toolCall.method}': {ex.Message}\n{ex.StackTrace}");
                result = new MCPToolRegistry.ToolResult { success = false, error = $"Tool Execution Exception: {ex.Message}" };
            }

            string observation = result.success ? result.observation : $"Error: {(string.IsNullOrEmpty(result.error) ? "An unknown error occurred during tool execution." : result.error)}";
            Debug.Log($"[Omnisense] Tool Result: {(result.success ? "Success" : "Failed")}. Observation added to history.");

            // ── CONTEXT CONDENSATION: Log what was touched ────────────────────────────
            if (result.success)
            {
                var q = toolCall.@params ?? new MCPToolParams();
                string logEntry = null;
                switch (toolCall.method)
                {
                    case "ui/setup_canvas":
                        logEntry = "Configured Canvas and EventSystem in scene"; break;
                    case "ui/create_panel":
                        logEntry = $"Created UI Panel: '{q.name}' under parent '{q.parentPath}'"; break;
                    case "ui/create_text":
                        logEntry = $"Created UI Text: '{q.name}' with text '{q.textContent}'"; break;
                    case "ui/create_button":
                        logEntry = $"Created UI Button: '{q.name}' with label '{q.labelText}'"; break;
                    case "ui/setup_layout_group":
                        logEntry = $"Set up layout group ({q.groupType}) on '{q.path}'"; break;
                    case "project/write_file":
                        logEntry = $"Wrote file: '{q.path}'"; break;
                    case "project/edit_file":
                        logEntry = $"Edited file: '{q.path}'"; break;
                    case "project/read_file":
                        logEntry = $"Read file: '{q.path}'"; break;
                    case "project/search_assets":
                        logEntry = $"Searched assets for: '{q.query}'"; break;
                    case "scene/instantiate_node":
                        logEntry = $"Created GameObject: '{q.name}' (type: {q.type})"; break;
                    case "scene/modify_node":
                        logEntry = $"Modified node '{q.path}': set {q.property} = '{q.value}'"; break;
                    case "scene/inspect_node":
                        logEntry = $"Inspected node/prefab at path: '{q.path}'"; break;
                    case "scene/inspect_component":
                        logEntry = $"Inspected component '{q.component}' on: '{q.path}'"; break;
                    case "scene/set_component_property":
                        logEntry = $"Set '{q.component}.{q.property}' = '{q.value}' on: '{q.path}'"; break;
                    case "scene/execute_transactions":
                        logEntry = $"Executed batched transactions: {q.operations?.Count ?? 0} operations"; break;
                    case "project/create_prefab":
                        logEntry = $"Created prefab: '{q.destinationAssetPath}' from '{q.path}'"; break;
                    case "project/inspect_asset":
                        logEntry = $"Inspected asset: '{q.path}'"; break;
                }
                if (logEntry != null)
                {
                    _turnContextLog.Add(logEntry);
                    // Add to persistent scratchpad and prevent duplicates
                    _persistentScratchpad.Remove(logEntry);
                    _persistentScratchpad.Add(logEntry);
                }
            }
            // ──────────────────────────────────────────────────────────────────────────

            // Implement Tool Output Truncation before entering history
            if (observation != null && observation.Length > 8000)
            {
                observation = observation.Substring(0, 8000) + "\n\n[System Warning]: Output was truncated due to length limits. If you need more information, use more specific search parameters or read a smaller chunk.";
            }

            _history.Add(new ChatMessage { role = "user", content = $"[Observation]\n{observation}" });
            SaveHistory();
            _currentTurnTrace.AppendLine($"[Observation]\n{observation}\n");
            onComplete?.Invoke($"[Observation]\n{observation}", _currentTurnTrace.ToString(), false);

            
            if (toolCall.method != "project/write_file" && toolCall.method != "project/edit_file") {
                onComplete?.Invoke(uiResponse + "\n\n[System]: Tool executed. Analyzing results...", _currentTurnTrace.ToString(), false);
            }
            
            bool wasCompiling = UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating;
            if (wasCompiling)
            {
                _currentTurnTrace.AppendLine("[System]: Waiting for Unity to finish compiling...");
                onComplete?.Invoke(uiResponse + "\n\n[System]: Waiting for Unity to finish compiling...", _currentTurnTrace.ToString(), false);
                while (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating)
                {
                    await System.Threading.Tasks.Task.Delay(500);
                }
                _currentTurnTrace.AppendLine("[System]: Compilation finished. Resuming execution...");
                onComplete?.Invoke(uiResponse + "\n\n[System]: Compilation finished. Resuming execution...", _currentTurnTrace.ToString(), false);
            }

            // Break the synchronous closure chain
            await System.Threading.Tasks.Task.Yield();
            
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
            // First, try to extract from inside markdown blocks if present
            var match = Regex.Match(content, @"```(?:mcp_json|json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.Singleline);
            string searchArea = match.Success ? match.Groups[1].Value : content;

            // Find the first '{' that has '"method"' inside the block
            int methodIdx = searchArea.IndexOf("\"method\"");
            if (methodIdx == -1) return null;

            int startIdx = searchArea.LastIndexOf('{', methodIdx);
            if (startIdx == -1) return null;

            // Robust brace matching to extract exactly one balanced JSON object
            int braceCount = 0;
            for (int i = startIdx; i < searchArea.Length; i++)
            {
                if (searchArea[i] == '{') braceCount++;
                else if (searchArea[i] == '}') braceCount--;

                if (braceCount == 0)
                {
                    return searchArea.Substring(startIdx, i - startIdx + 1).Trim();
                }
            }

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
            public string destinationAssetPath;
            public string query;
            public List<TransactionOperation> operations;

            // UI Specialist Fields
            public string parentPath;
            public string textContent;
            public int fontSize;
            public string alignment;
            public string labelText;
            public string groupType;
            public float spacing;
            public string paddingCSV;
            public string childAlignment;
        }

        public static List<TransactionOperation> ParseOperationsFallback(string rawJson)
        {
            var list = new List<TransactionOperation>();
            try
            {
                var match = Regex.Match(rawJson, @"""operations""\s*:\s*\[([\s\S]*?)\]", RegexOptions.IgnoreCase);
                if (!match.Success) return list;

                string arrayContent = match.Groups[1].Value;
                var objMatches = Regex.Matches(arrayContent, @"\{([\s\S]*?)\}");
                foreach (Match objMatch in objMatches)
                {
                    string objJson = objMatch.Value;
                    var op = new TransactionOperation();
                    op.action = ExtractJsonStringField(objJson, "action") ?? ExtractJsonStringField(objJson, "tool");
                    op.tool = ExtractJsonStringField(objJson, "tool");
                    op.path = ExtractJsonStringField(objJson, "path");
                    op.parent = ExtractJsonStringField(objJson, "parent");
                    op.name = ExtractJsonStringField(objJson, "name");
                    op.property = ExtractJsonStringField(objJson, "property");
                    op.value = ExtractJsonStringField(objJson, "value");
                    op.component = ExtractJsonStringField(objJson, "component");
                    op.type = ExtractJsonStringField(objJson, "type");
                    
                    var compMatch = Regex.Match(objJson, @"""components""\s*:\s*\[([\s\S]*?)\]", RegexOptions.IgnoreCase);
                    if (compMatch.Success)
                    {
                        op.components = new List<string>();
                        var comps = Regex.Matches(compMatch.Groups[1].Value, @"""([^""]+)""");
                        foreach (Match c in comps)
                        {
                            op.components.Add(c.Groups[1].Value);
                        }
                    }
                    
                    list.Add(op);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Omnisense] Fallback operations parser failed: {ex.Message}");
            }
            return list;
        }

        private static string ExtractJsonStringField(string json, string fieldName)
        {
            var match = Regex.Match(json, @"""" + fieldName + @"""" + @"\s*:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
