using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEngine.SceneManagement;

namespace Omnisense
{
    public class OmnisenseWindow : EditorWindow
    {
        private VisualElement _chatContainer;
        private VisualElement _settingsContainer;
        private VisualElement _contextContainer;
        private ScrollView _chatHistory;
        private TextField _chatInput;
        private Label _placeholderLabel;
        private DropdownField _modelSelector;
        private VisualElement _contextChips;
        private ChatSession _currentSession;

        // Spinner state
        private Label _loadingIndicator;
        private string[] _spinnerFrames = new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
        private int _spinnerIndex = 0;
        private double _lastSpinnerTime = 0;

        private string _currentTurnAIContent = "";

        private VisualElement _commercialSettings;
        private VisualElement _selfhostedSettings;

        private Button _tabChat;
        private Button _tabSettings;
        private Button _tabContext;

        private Button _tabCommercial;
        private Button _tabSelfhosted;

        [MenuItem("Window/Omnisense AI")]
        public static void ShowWindow()
        {
            OmnisenseWindow wnd = GetWindow<OmnisenseWindow>();
            wnd.titleContent = new GUIContent("Omnisense AI");
            wnd.minSize = new Vector2(500, 600);
        }

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;

            // Import UXML
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Editor/Omnisense/OmnisenseWindow.uxml");
            if (visualTree == null)
            {
                visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.rahul.omnisense/OmnisenseWindow.uxml");
            }

            if (visualTree == null)
            {
                root.Add(new Label("Failed to load OmnisenseWindow.uxml. Please ensure the package is correctly installed."));
                return;
            }
            
            VisualElement content = visualTree.Instantiate();
            content.style.flexGrow = 1;
            root.Add(content);

            // Import USS
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Editor/Omnisense/OmnisenseWindow.uss");
            if (styleSheet == null)
            {
                styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.rahul.omnisense/OmnisenseWindow.uss");
            }

            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            // Query elements
            _chatContainer = root.Q<VisualElement>("chat-container");
            _settingsContainer = root.Q<VisualElement>("settings-container");
            _contextContainer = root.Q<VisualElement>("context-container");

            _commercialSettings = root.Q<VisualElement>("commercial-settings");
            _selfhostedSettings = root.Q<VisualElement>("selfhosted-settings");
            _chatHistory = root.Q<ScrollView>("chat-history");
            _chatInput = root.Q<TextField>("chat-input");
            _placeholderLabel = root.Q<Label>("manual-placeholder");
            _modelSelector = root.Q<DropdownField>("model-selector");
            _contextChips = root.Q<VisualElement>("context-chips-container");

            _tabChat = root.Q<Button>("tab-chat");
            _tabSettings = root.Q<Button>("tab-settings");
            _tabContext = root.Q<Button>("tab-context");

            _tabCommercial = root.Q<Button>("settings-tab-commercial");
            _tabSelfhosted = root.Q<Button>("settings-tab-selfhosted");

            // Setup Tab Events
            _tabChat.clicked += () => SwitchMainTab("chat");
            _tabSettings.clicked += () => SwitchMainTab("settings");
            _tabContext.clicked += () => {
                SwitchMainTab("context");
                RefreshContext();
            };

            _tabCommercial.clicked += () => SwitchSettingsTab("commercial");
            _tabSelfhosted.clicked += () => SwitchSettingsTab("selfhosted");

            // Chat Events
            var btnAttach = root.Q<Button>("btn-attach");
            if (btnAttach != null) btnAttach.clicked += OnAttachClicked;

            root.Q<Button>("send-button").clicked += SendMessage;
            root.Q<Button>("btn-history").clicked += ShowHistory;
            root.Q<Button>("btn-undo").clicked += () => OmnisenseUndoManager.PerformUndo();
            root.Q<Button>("btn-new-chat").clicked += () => {
                _chatHistory.Clear();
                var label = new Label("New session started.");
                label.AddToClassList("system-message");
                _chatHistory.Add(label);
            };

            var btnCopyChat = root.Q<Button>("btn-copy-chat");
            if (btnCopyChat != null)
            {
                btnCopyChat.clicked += () => {
                    if (_currentSession == null || _currentSession.messages == null) return;
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    foreach (var msg in _currentSession.messages)
                    {
                        sb.AppendLine($"[{msg.sender}]");
                        sb.AppendLine(msg.content);
                        sb.AppendLine("----------------------------------------\n");
                    }
                    EditorGUIUtility.systemCopyBuffer = sb.ToString();
                    btnCopyChat.text = "Copied!";
                    btnCopyChat.schedule.Execute(() => btnCopyChat.text = "Copy Chat").StartingIn(1500);
                };
            }

            var btnStop = root.Q<Button>("btn-stop");
            if (btnStop != null)
            {
                btnStop.clicked += () => {
                    AIOrchestrator.Instance.Abort();
                    ToggleStopButton(false);
                    if (_loadingIndicator != null)
                    {
                        if (_chatHistory.Contains(_loadingIndicator)) _chatHistory.Remove(_loadingIndicator);
                        EditorApplication.update -= UpdateSpinner;
                        _loadingIndicator = null;
                    }
                    AddMessageToChat("System", "AI execution aborted by user.");
                };
            }

            _chatInput.RegisterCallback<KeyDownEvent>(evt => {
                if (evt.keyCode == KeyCode.Return) {
                    if (evt.shiftKey) {
                        // Allow native TextField newline insertion
                        return;
                    } else {
                        SendMessage();
                        evt.StopPropagation();
                        evt.PreventDefault();
                    }
                }
            });

            AIOrchestrator.Instance.OnPendingAction += HandlePendingAction;

            root.RegisterCallback<KeyDownEvent>(evt => {
                if (evt.actionKey && evt.keyCode == KeyCode.V)
                {
                    string paste = EditorGUIUtility.systemCopyBuffer;
                    if (!string.IsNullOrEmpty(paste))
                    {
                        if (File.Exists(paste) || Directory.Exists(paste))
                        {
                            AddContextChipByPath(paste);
                            evt.StopPropagation();
                            evt.PreventDefault();
                        }
                    }
                }
            });

            _chatInput.RegisterValueChangedCallback(evt => {
                if (_placeholderLabel != null)
                {
                    _placeholderLabel.style.display = string.IsNullOrEmpty(evt.newValue) ? DisplayStyle.Flex : DisplayStyle.None;
                }
            });

            // Drag & Drop
            root.RegisterCallback<DragEnterEvent>(OnDragEnter);
            root.RegisterCallback<DragLeaveEvent>(OnDragLeave);
            root.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            root.RegisterCallback<DragPerformEvent>(OnDragPerform);

            // Settings Persistence
            LoadSettings();
            
            // Auto-save API keys on change
            root.Q<TextField>("openai-key").RegisterValueChangedCallback(_ => SaveSettings());
            root.Q<TextField>("anthropic-key").RegisterValueChangedCallback(_ => SaveSettings());
            root.Q<TextField>("gemini-key").RegisterValueChangedCallback(_ => SaveSettings());
            root.Q<TextField>("grok-key").RegisterValueChangedCallback(_ => SaveSettings());

            // Initialize Session
            string lastSessionId = EditorPrefs.GetString("Omnisense_LastSessionId", "");
            var restoredSession = OmnisenseSessionManager.GetSessionById(lastSessionId);
            
            if (restoredSession != null)
            {
                LoadSession(restoredSession);
            }
            else
            {
                _currentSession = OmnisenseSessionManager.CreateNewSession();
                EditorPrefs.SetString("Omnisense_LastSessionId", _currentSession.id);
            }

            // Initialize Model Selector
            if (_modelSelector != null)
            {
                _modelSelector.choices = new List<string> { 
                    "gpt-5.5", "gpt-5.4-mini", "o3-mini",
                    "claude-4.7-opus", "claude-4.6-sonnet", "claude-4.5-haiku",
                    "gemini-3.1-pro", "gemini-3.1-flash", "gemini-3.1-flash-lite",
                    "grok-4.3-beta", "grok-4.20-beta-2", "grok-4.20-fast"
                };
                
                string savedModel = EditorPrefs.GetString("Omnisense_SelectedModel", "gpt-5.5");
                if (_modelSelector.choices.Contains(savedModel)) _modelSelector.value = savedModel;
                else _modelSelector.value = "gpt-5.5";

                _modelSelector.RegisterValueChangedCallback(evt => {
                    EditorPrefs.SetString("Omnisense_SelectedModel", evt.newValue);
                });
            }

            // Initial refresh
            RefreshContext();

            // Auto-resume AI if it was interrupted by reload
            if (EditorPrefs.GetBool("Omnisense_AI_PendingResume", false))
            {
                string lastModel = EditorPrefs.GetString("Omnisense_AI_LastModel", "gpt-4o");
                ResumeAIProcess(lastModel);
            }

            // Initialize background server
            MCPServer.StartServer();
        }

        private void ResumeAIProcess(string model)
        {
            Debug.Log("[Omnisense] Resuming AI process after assembly reload...");
            
            // Add Loading Spinner
            _loadingIndicator = new Label("⠋ Resuming Thought Process...");
            _loadingIndicator.AddToClassList("system-message");
            _chatHistory.Add(_loadingIndicator);
            SafeScrollTo(_loadingIndicator);
            EditorApplication.update += UpdateSpinner;
            ToggleStopButton(true);
            _currentTurnAIContent = "";

            AIOrchestrator.Instance.Resume(model, (response, isFinal) => {
                if (isFinal) ToggleStopButton(false);
                if (isFinal && _loadingIndicator != null)
                {
                    if (_chatHistory.Contains(_loadingIndicator)) _chatHistory.Remove(_loadingIndicator);
                    EditorApplication.update -= UpdateSpinner;
                    _loadingIndicator = null;
                }
                
                _currentTurnAIContent += response + "\n\n";
                // When resuming, we might not have the original turnId easily, so we can try to grab it from the UndoManager
                string currentTurnId = OmnisenseUndoManager.CurrentTurnId;
                AddMessageToChat("AI", response, isFinal, currentTurnId);
                if (isFinal) Debug.Log("[Omnisense] Auto-Resume completed successfully.");
            });
        }

        private void RefreshContext()
        {
            var projectTree = rootVisualElement.Q<VisualElement>("project-tree");
            var sceneTree = rootVisualElement.Q<VisualElement>("scene-tree");

            if (projectTree != null)
            {
                projectTree.Clear();
                BuildProjectTree(Application.dataPath, projectTree);
            }

            if (sceneTree != null)
            {
                sceneTree.Clear();
                BuildSceneTree(sceneTree);
            }
        }

        private void BuildProjectTree(string path, VisualElement container)
        {
            try
            {
                // Add Directories
                foreach (var dir in Directory.GetDirectories(path))
                {
                    var dirName = Path.GetFileName(dir);
                    if (dirName.StartsWith(".")) continue; // Skip hidden folders

                    var foldout = new Foldout { text = $"📁 {dirName}" };
                    container.Add(foldout);
                    BuildProjectTree(dir, foldout);
                }

                // Add Files
                foreach (var file in Directory.GetFiles(path))
                {
                    if (file.EndsWith(".meta")) continue; // Skip meta files
                    var fileName = Path.GetFileName(file);
                    var label = new Label($"📄 {fileName}");
                    label.style.paddingLeft = 20;
                    container.Add(label);
                }
            }
            catch (System.Exception e)
            {
                container.Add(new Label($"Error: {e.Message}"));
            }
        }

        private void BuildSceneTree(VisualElement container)
        {
            var activeScene = SceneManager.GetActiveScene();
            var rootObjects = activeScene.GetRootGameObjects();

            foreach (var obj in rootObjects)
            {
                AddGameObjectToTree(obj, container);
            }
        }

        private void AddGameObjectToTree(GameObject obj, VisualElement container)
        {
            if (obj.transform.childCount > 0)
            {
                var foldout = new Foldout { text = $"○ {obj.name}" };
                container.Add(foldout);
                foreach (Transform child in obj.transform)
                {
                    AddGameObjectToTree(child.gameObject, foldout);
                }
            }
            else
            {
                var label = new Label($"○ {obj.name}");
                label.style.paddingLeft = 20;
                container.Add(label);
            }
        }

        private void SwitchMainTab(string tabName)
        {
            // Update Visibility
            _chatContainer.ToggleInClassList("hidden");
            _settingsContainer.ToggleInClassList("hidden");
            _contextContainer.ToggleInClassList("hidden");

            _chatContainer.SetEnabled(tabName == "chat");
            _settingsContainer.SetEnabled(tabName == "settings");
            _contextContainer.SetEnabled(tabName == "context");

            // Simple show/hide
            _chatContainer.style.display = (tabName == "chat") ? DisplayStyle.Flex : DisplayStyle.None;
            _settingsContainer.style.display = (tabName == "settings") ? DisplayStyle.Flex : DisplayStyle.None;
            _contextContainer.style.display = (tabName == "context") ? DisplayStyle.Flex : DisplayStyle.None;

            // Update Tab Styling
            _tabChat.RemoveFromClassList("active-tab");
            _tabSettings.RemoveFromClassList("active-tab");
            _tabContext.RemoveFromClassList("active-tab");

            if (tabName == "chat") _tabChat.AddToClassList("active-tab");
            else if (tabName == "settings") _tabSettings.AddToClassList("active-tab");
            else if (tabName == "context") _tabContext.AddToClassList("active-tab");
        }

        private void SwitchSettingsTab(string tabName)
        {
            _commercialSettings.style.display = (tabName == "commercial") ? DisplayStyle.Flex : DisplayStyle.None;
            _selfhostedSettings.style.display = (tabName == "selfhosted") ? DisplayStyle.Flex : DisplayStyle.None;

            _tabCommercial.RemoveFromClassList("active-sub-tab");
            _tabSelfhosted.RemoveFromClassList("active-sub-tab");

            if (tabName == "commercial") _tabCommercial.AddToClassList("active-sub-tab");
            else if (tabName == "selfhosted") _tabSelfhosted.AddToClassList("active-sub-tab");
        }

        private void SaveSettings()
        {
            EditorPrefs.SetString("Omnisense_OpenAI_Key", rootVisualElement.Q<TextField>("openai-key").value);
            EditorPrefs.SetString("Omnisense_Anthropic_Key", rootVisualElement.Q<TextField>("anthropic-key").value);
            EditorPrefs.SetString("Omnisense_Gemini_Key", rootVisualElement.Q<TextField>("gemini-key").value);
            EditorPrefs.SetString("Omnisense_Grok_Key", rootVisualElement.Q<TextField>("grok-key").value);
            
            EditorPrefs.SetInt("Omnisense_OpenAI_MaxTokens", rootVisualElement.Q<SliderInt>("openai-max-tokens-slider").value);
            EditorPrefs.SetInt("Omnisense_Anthropic_MaxTokens", rootVisualElement.Q<SliderInt>("anthropic-max-tokens-slider").value);
            EditorPrefs.SetInt("Omnisense_Gemini_MaxTokens", rootVisualElement.Q<SliderInt>("gemini-max-tokens-slider").value);
            EditorPrefs.SetInt("Omnisense_Grok_MaxTokens", rootVisualElement.Q<SliderInt>("grok-max-tokens-slider").value);

            Debug.Log("[Omnisense] Settings saved.");
        }

        private void LoadSettings()
        {
            rootVisualElement.Q<TextField>("openai-key").value = EditorPrefs.GetString("Omnisense_OpenAI_Key", "");
            rootVisualElement.Q<TextField>("anthropic-key").value = EditorPrefs.GetString("Omnisense_Anthropic_Key", "");
            rootVisualElement.Q<TextField>("gemini-key").value = EditorPrefs.GetString("Omnisense_Gemini_Key", "");
            rootVisualElement.Q<TextField>("grok-key").value = EditorPrefs.GetString("Omnisense_Grok_Key", "");

            BindTokenControls("openai", 4096, 1, 16384);
            BindTokenControls("anthropic", 4096, 1, 8192);
            BindTokenControls("gemini", 4096, 1, 8192);
            BindTokenControls("grok", 4096, 1, 8192);
        }

        private void BindTokenControls(string prefix, int defaultVal, int min, int max)
        {
            var slider = rootVisualElement.Q<SliderInt>($"{prefix}-max-tokens-slider");
            var field = rootVisualElement.Q<IntegerField>($"{prefix}-max-tokens-field");
            
            // Fix naming mismatch: openai -> OpenAI, anthropic -> Anthropic, etc.
            string keyPart = prefix;
            if (prefix == "openai") keyPart = "OpenAI";
            else keyPart = char.ToUpper(prefix[0]) + prefix.Substring(1);

            int saved = EditorPrefs.GetInt($"Omnisense_{keyPart}_MaxTokens", defaultVal);
            slider.value = saved;
            field.value = saved;

            slider.RegisterValueChangedCallback(evt => {
                field.value = evt.newValue;
                SaveSettings();
            });
            field.RegisterValueChangedCallback(evt => {
                int val = Mathf.Clamp(evt.newValue, min, max);
                if (val != evt.newValue) field.value = val;
                slider.value = val;
                SaveSettings();
            });
        }

        private void ToggleStopButton(bool isProcessing)
        {
            var btnStop = rootVisualElement.Q<Button>("btn-stop");
            var btnSend = rootVisualElement.Q<Button>("send-button");
            if (btnStop != null && btnSend != null)
            {
                if (isProcessing)
                {
                    btnStop.RemoveFromClassList("hidden");
                    btnSend.AddToClassList("hidden");
                }
                else
                {
                    btnStop.AddToClassList("hidden");
                    btnSend.RemoveFromClassList("hidden");
                }
            }
        }

        private void SendMessage()
        {
            string text = _chatInput.value.Trim();
            if (string.IsNullOrEmpty(text)) return;
            Debug.Log("[Omnisense] User sent a message.");

            // Gather context from chips
            string contextText = "";
            foreach (var chip in _contextChips.Children())
            {
                if (chip.userData is string fullPath) contextText += $"[Context: {fullPath}]\n";
            }
            _contextChips.Clear(); // Ephemeral context - clear after sending

            string turnId = Guid.NewGuid().ToString();

            AddMessageToChat("User", contextText + text, true, turnId);
            _chatInput.value = "";
            _currentTurnAIContent = "";
            
            // Add Loading Spinner
            _loadingIndicator = new Label("⠋ Thinking...");
            _loadingIndicator.AddToClassList("system-message");
            _chatHistory.Add(_loadingIndicator);
            SafeScrollTo(_loadingIndicator);
            EditorApplication.update += UpdateSpinner;
            
            ToggleStopButton(true);

            AIOrchestrator.Instance.ProcessPrompt(contextText + text, _modelSelector.value, turnId, (response, isFinal) => {
                if (isFinal) ToggleStopButton(false);
                if (isFinal && _loadingIndicator != null)
                {
                    if (_chatHistory.Contains(_loadingIndicator)) _chatHistory.Remove(_loadingIndicator);
                    EditorApplication.update -= UpdateSpinner;
                    _loadingIndicator = null;
                }
                
                _currentTurnAIContent += response + "\n\n";
                AddMessageToChat("AI", response, isFinal, turnId);
            });
        }

        private void HandlePendingAction(string diffSummary, Action<bool> callback)
        {
            var container = new VisualElement();
            container.AddToClassList("chat-message");
            container.AddToClassList("ai-message");
            container.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
            container.style.borderLeftColor = new Color(1f, 0.8f, 0.2f);
            container.style.borderLeftWidth = 4;

            var header = new Label("⚠️ Pending Action Approval");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.color = new Color(1f, 0.8f, 0.2f);
            container.Add(header);

            var diffText = new Label(diffSummary);
            diffText.enableRichText = true;
            diffText.style.marginTop = 5;
            diffText.style.marginBottom = 10;
            diffText.style.whiteSpace = WhiteSpace.Normal;
            container.Add(diffText);

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;

            var btnAccept = new Button(() => {
                container.RemoveFromHierarchy();
                callback(true);
            }) { text = "✓ Accept" };
            btnAccept.style.backgroundColor = new Color(0.2f, 0.5f, 0.2f);
            btnAccept.style.color = Color.white;
            btnAccept.style.flexGrow = 1;

            var btnReject = new Button(() => {
                container.RemoveFromHierarchy();
                callback(false);
            }) { text = "✗ Reject" };
            btnReject.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f);
            btnReject.style.color = Color.white;
            btnReject.style.flexGrow = 1;

            buttonRow.Add(btnAccept);
            buttonRow.Add(btnReject);
            container.Add(buttonRow);

            _chatHistory.Add(container);
            SafeScrollTo(container);
        }

        private void UpdateSpinner()
        {
            if (_loadingIndicator == null) return;
            if (EditorApplication.timeSinceStartup - _lastSpinnerTime > 0.1)
            {
                _spinnerIndex = (_spinnerIndex + 1) % _spinnerFrames.Length;
                _loadingIndicator.text = $"{_spinnerFrames[_spinnerIndex]} Thinking...";
                _lastSpinnerTime = EditorApplication.timeSinceStartup;
            }
        }

        private void OnDragEnter(DragEnterEvent evt) { }
        private void OnDragLeave(DragLeaveEvent evt) { }
        private void OnDragUpdated(DragUpdatedEvent evt) 
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        }

        private void OnDragPerform(DragPerformEvent evt)
        {
            if (_contextChips == null) return;

            DragAndDrop.AcceptDrag();
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj == null) continue;

                // Check if it's a file or a scene object
                string path = AssetDatabase.GetAssetPath(obj);
                bool isSceneObj = string.IsNullOrEmpty(path);
                string fullPath = isSceneObj ? GetGameObjectPath((GameObject)obj) : path;
                AddContextChipByPath(fullPath, isSceneObj ? obj.name : Path.GetFileName(path));
            }
        }

        private void OnAttachClicked()
        {
            Debug.Log("[Omnisense] Attachment button clicked.");
            string path = EditorUtility.OpenFilePanel("Attach File", "Assets", "");
            if (!string.IsNullOrEmpty(path))
            {
                // Make path relative to project folder if inside it
                if (path.StartsWith(Application.dataPath)) {
                    path = "Assets" + path.Substring(Application.dataPath.Length);
                }
                AddContextChipByPath(path, Path.GetFileName(path));
            }
        }

        private void AddContextChipByPath(string fullPath, string displayName = null)
        {
            if (string.IsNullOrEmpty(displayName)) displayName = Path.GetFileName(fullPath);
            Debug.Log($"[Omnisense] Adding context chip: {fullPath}");

            var chip = new VisualElement();
            chip.style.flexDirection = FlexDirection.Row;
            chip.AddToClassList("context-chip");
            chip.userData = fullPath;
            chip.tooltip = fullPath;

            var btnPing = new Button { text = displayName };
            btnPing.style.backgroundColor = Color.clear;
            btnPing.style.color = Color.white;
            btnPing.clicked += () => PingObjectByPath(fullPath);

            var btnClose = new Button { text = "x" };
            btnClose.style.backgroundColor = Color.clear;
            btnClose.style.color = Color.white;
            btnClose.clicked += () => _contextChips.Remove(chip);

            chip.Add(btnPing);
            chip.Add(btnClose);
            _contextChips.Add(chip);
        }

        private string GetGameObjectPath(GameObject obj)
        {
            string path = obj.name;
            Transform parent = obj.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return "/" + path;
        }

        private void PingObjectByPath(string path)
        {
            if (path.StartsWith("Assets/"))
            {
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (obj != null) { EditorGUIUtility.PingObject(obj); Selection.activeObject = obj; }
            }
            else
            {
                var obj = GameObject.Find(path);
                if (obj != null) { EditorGUIUtility.PingObject(obj); Selection.activeGameObject = obj; }
            }
        }

        public void AddMessageToChat(string sender, string content, bool showCopyButton = false, string turnId = "")
        {
            Debug.Log($"[Omnisense] Adding message from {sender} to chat UI. CopyButton: {showCopyButton}");
            var msgContainer = CreateMessageElement(sender, content, showCopyButton, turnId);
            _chatHistory.Add(msgContainer);

            // Save to current session
            if (_currentSession != null)
            {
                _currentSession.messages.Add(new ChatMessage { 
                    sender = sender, 
                    content = content, 
                    timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    turnId = turnId
                });
                OmnisenseSessionManager.SaveSession(_currentSession);
            }
            
            // Auto-scroll
            SafeScrollTo(msgContainer);
        }

        private void SafeScrollTo(VisualElement target)
        {
            if (target == null || _chatHistory == null) return;
            EditorApplication.delayCall += () => {
                if (_chatHistory != null && target != null && _chatHistory.Contains(target))
                {
                    try { _chatHistory.ScrollTo(target); } catch { }
                }
            };
        }

        private VisualElement CreateMessageElement(string sender, string content, bool showCopyButton = false, string turnId = "")
        {
            var msgContainer = new VisualElement();
            msgContainer.AddToClassList("message-container");
            msgContainer.AddToClassList(sender == "User" ? "user-message" : "ai-message");

            // Parse for special blocks (thought, observation, code)
            if (content.Contains("<thought>"))
            {
                var match = Regex.Match(content, "<thought>(.*?)</thought>", RegexOptions.Singleline);
                if (match.Success)
                {
                    var thought = new Label(match.Groups[1].Value.Trim());
                    thought.AddToClassList("thought-block");
                    msgContainer.Add(thought);
                    content = content.Replace(match.Value, "").Trim();
                }
            }

            if (content.Contains("[Observation]"))
            {
                var parts = content.Split(new[] { "[Observation]" }, StringSplitOptions.None);
                if (parts.Length > 1)
                {
                    var observation = new Label(parts[1].Trim());
                    observation.AddToClassList("observation-block");
                    msgContainer.Add(observation);
                    content = parts[0].Trim();
                }
            }

            // Parse for Context links
            var contextMatches = Regex.Matches(content, @"\[Context: (.*?)\]");
            if (contextMatches.Count > 0)
            {
                var contextRow = new VisualElement();
                contextRow.style.flexDirection = FlexDirection.Row;
                contextRow.style.flexWrap = Wrap.Wrap;
                contextRow.style.marginBottom = 5;

                foreach (Match match in contextMatches)
                {
                    string fullPath = match.Groups[1].Value;
                    string displayName = fullPath.StartsWith("/") ? fullPath.Substring(fullPath.LastIndexOf('/') + 1) : Path.GetFileName(fullPath);

                    var chip = new Button { text = "📎 " + displayName };
                    chip.AddToClassList("context-chip");
                    chip.tooltip = fullPath;
                    chip.clicked += () => PingObjectByPath(fullPath);
                    contextRow.Add(chip);

                    content = content.Replace(match.Value, "").Trim();
                }
                msgContainer.Add(contextRow);
            }

            if (!string.IsNullOrEmpty(content))
            {
                var textField = new TextField { value = content, isReadOnly = true, multiline = true };
                textField.AddToClassList("selectable-message-text");
                msgContainer.Add(textField);
            }

            // Append Undo button for User messages (representing the start of a turn)
            if (sender == "User" && !string.IsNullOrEmpty(turnId))
            {
                // Note: since the AI hasn't executed yet when this is first created,
                // the undo button will always appear. But it will only undo things if they exist.
                var undoBtn = new Button { text = "↺ Undo Actions for this Prompt" };
                undoBtn.AddToClassList("undo-turn-btn");
                undoBtn.clicked += () => {
                    Debug.Log($"[Omnisense] Undo button clicked for turn: {turnId}");
                    bool success = OmnisenseUndoManager.UndoTurn(turnId);
                    if (success) {
                        undoBtn.text = "✓ Undone";
                        undoBtn.SetEnabled(false);
                    }
                };
                msgContainer.Add(undoBtn);
            }

            if (showCopyButton)
            {
                var footer = new VisualElement();
                footer.style.flexDirection = FlexDirection.Row;
                footer.style.justifyContent = Justify.FlexEnd;
                footer.style.marginTop = 5;

                string copyText = (sender == "User") ? content : _currentTurnAIContent;
                string btnText = (sender == "User") ? "Copy" : "Copy Full Response";
                
                var btnCopy = new Button(() => {
                    EditorGUIUtility.systemCopyBuffer = copyText.Trim();
                    Debug.Log("[Omnisense] Content copied to clipboard.");
                }) { text = btnText };
                btnCopy.AddToClassList("copy-button-small");

                if (sender == "AI")
                {
                    // For AI, we wrap the message and place the button BELOW the bubble
                    var wrapper = new VisualElement();
                    wrapper.AddToClassList("ai-turn-wrapper");
                    // Move current msgContainer into wrapper
                    wrapper.Add(msgContainer);
                    
                    var aiFooter = new VisualElement();
                    aiFooter.style.flexDirection = FlexDirection.Row;
                    aiFooter.style.justifyContent = Justify.FlexStart;
                    aiFooter.style.marginTop = 2;
                    aiFooter.Add(btnCopy);
                    wrapper.Add(aiFooter);
                    
                    return wrapper;
                }
                else
                {
                    // For User, keep it inside at the bottom right
                    footer.Add(btnCopy);
                    msgContainer.Add(footer);
                }
            }

            return msgContainer;
        }

        private void ShowHistory()
        {
            var sessions = OmnisenseSessionManager.GetAllSessions();
            if (sessions.Count == 0)
            {
                Debug.Log("[Omnisense] No chat history found.");
                return;
            }

            GenericMenu menu = new GenericMenu();
            foreach (var session in sessions)
            {
                menu.AddItem(new GUIContent($"{session.name}"), false, () => LoadSession(session));
            }
            menu.ShowAsContext();
        }

        private void LoadSession(ChatSession session)
        {
            _currentSession = session;
            EditorPrefs.SetString("Omnisense_LastSessionId", session.id);
            _chatHistory.Clear();
            
            VisualElement last = null;
            for (int i = 0; i < session.messages.Count; i++)
            {
                var msg = session.messages[i];
                bool showCopy = false;

                if (msg.sender == "User")
                {
                    showCopy = true;
                }
                else if (msg.sender == "AI")
                {
                    // For AI, show copy button only on the last message of the turn
                    if (i == session.messages.Count - 1 || session.messages[i + 1].sender == "User")
                    {
                        showCopy = true;
                        // Accumulate the entire AI turn for the copy buffer
                        _currentTurnAIContent = "";
                        for (int j = i; j >= 0 && session.messages[j].sender == "AI"; j--)
                        {
                            _currentTurnAIContent = session.messages[j].content + "\n\n" + _currentTurnAIContent;
                        }
                    }
                }

                last = CreateMessageElement(msg.sender, msg.content, showCopy, msg.turnId);
                _chatHistory.Add(last);
            }

            if (last != null) 
            {
                EditorApplication.delayCall += () => _chatHistory.ScrollTo(last);
            }
        }

        private void OnDestroy()
        {
            MCPServer.StopServer();
        }
    }
}
