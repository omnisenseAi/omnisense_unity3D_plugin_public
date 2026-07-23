// =================================================================================================
// PROJECT: Omnisense AI (Unity3D Integration Plugin)
// AUTHOR:  Rahul Bhardwaj
// COMPANY: Omnisense AI
// YEAR:    2026
//
// COPYRIGHT NOTICE:
// Copyright (c) 2026 Rahul Bhardwaj / Omnisense AI. All rights reserved.
// This software and associated documentation files (the "Software") are proprietary and confidential.
// Unauthorized copying, distribution, or modification of this file is strictly prohibited.
// =================================================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEngine.Networking;

namespace Omnisense
{
    /// <summary>
    /// CORE PHILOSOPHY & DESIGN DECISION:
    /// The ModelGenerationPopup serves as a stateful, interactive AI 3D Model Chat Workspace.
    /// 
    /// WHY:
    /// Creating low-poly models, textures, or procedural geometry usually requires complex external pipelines.
    /// Structuring this as a Visual Chat Workspace with persistent thread history:
    ///   1. Dialogue Context: Enables iterating on previous geometries and materials in natural language (e.g. "Add a lock to the chest").
    ///   2. Multimodal References: Let developers drop concept sketches to guide both cloud text-to-3d prompts and Three.js logic.
    ///   3. Instantiate Scene Helper: Directly injects generated glTF/glb prefabs into the Unity Editor's open scene hierarchy.
    /// </summary>
    public class ModelGenerationPopup : EditorWindow
    {
        private VisualElement _sidebarPanel;
        private ScrollView _chatHistoryContainer;
        private ScrollView _chatViewport;
        private TextField _promptField;
        private Button _generateBtn;
        private Button _attachBtn;
        private Label _statusLabel;

        // Reference image attachment fields
        private VisualElement _attachmentContainer;
        private Image _attachmentThumbnail;
        private Label _attachmentLabel;
        private string _selectedReferencePath = "";
        private string _lastAttachedPath = "";

        // Settings and converter controls
        private DropdownField _providerField;
        private DropdownField _modelSelector;
        private DropdownField _tripoVersionSelector;
        private VisualElement _tripoVersionRow;
        private TextField _pathField;
        private TextField _jsFileField;
        private Button _convertBtn;
        private Toggle _orchestratorToggle;

        // Networking requests
        private UnityWebRequest _activeLlmRequest;
        private double _llmRequestStartTime;
        private UnityWebRequest _activeCloudRequest;
        private double _cloudRequestStartTime;
        private string _pendingExplanation = "";

        // Polling states for 3D APIs
        private string _taskId = "";
        private string _pollingProvider = "";
        private double _lastPollTime = 0;
        private int _pollAttempts = 0;

        // Chat session state
        private ModelChatSession _activeSession;
        private List<ModelChatSession> _sessions = new List<ModelChatSession>();

        [MenuItem("Window/Omnisense/3D Model Generator")]
        public static void Open()
        {
            var window = GetWindow<ModelGenerationPopup>(true, "🧊 AI 3D Model Chat Workspace", true);
            window.minSize = new Vector2(650, 580);
            window.Show();
        }

        private void OnEnable()
        {
            BuildUI();
            EnsureNodeDependencies();
            LoadLastOrNewSession();
        }

        private void OnDisable()
        {
            EditorApplication.update -= CheckThreeJsRequestProgress;
            EditorApplication.update -= CheckMeshyRequestProgress;
            EditorApplication.update -= CheckTripoRequestProgress;
            EditorApplication.update -= CheckLlmOrchestratorProgress;
            EditorApplication.update -= PollTaskStatus;
        }

        private void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.backgroundColor = new StyleColor(new Color(0.17f, 0.18f, 0.2f));
            root.style.flexDirection = FlexDirection.Row;

            // 1. LEFT SIDEBAR (Model Chat History Threads)
            _sidebarPanel = new VisualElement();
            _sidebarPanel.style.width = 180;
            _sidebarPanel.style.minWidth = 180;
            _sidebarPanel.style.backgroundColor = new StyleColor(new Color(0.12f, 0.13f, 0.15f));
            _sidebarPanel.style.borderRightWidth = 1;
            _sidebarPanel.style.borderRightColor = new StyleColor(new Color(0.25f, 0.25f, 0.25f));
            _sidebarPanel.style.paddingLeft = 8;
            _sidebarPanel.style.paddingRight = 8;
            _sidebarPanel.style.paddingTop = 8;
            _sidebarPanel.style.paddingBottom = 8;

            var newChatBtn = new Button(CreateNewChat) { text = "➕ New Model Chat" };
            newChatBtn.style.height = 28;
            newChatBtn.style.marginBottom = 10;
            newChatBtn.style.backgroundColor = new StyleColor(new Color(0.15f, 0.45f, 0.25f));
            newChatBtn.style.color = new StyleColor(Color.white);
            newChatBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            newChatBtn.style.borderTopLeftRadius = 4;
            newChatBtn.style.borderTopRightRadius = 4;
            newChatBtn.style.borderBottomLeftRadius = 4;
            newChatBtn.style.borderBottomRightRadius = 4;
            _sidebarPanel.Add(newChatBtn);

            var historyLabel = new Label("Recent Model Chats:");
            historyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            historyLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            historyLabel.style.fontSize = 11;
            historyLabel.style.marginBottom = 5;
            _sidebarPanel.Add(historyLabel);

            _chatHistoryContainer = new ScrollView();
            _chatHistoryContainer.style.flexGrow = 1;
            _sidebarPanel.Add(_chatHistoryContainer);

            root.Add(_sidebarPanel);

            // 2. RIGHT WORKSPACE (Chat Panel & Output Settings)
            var workspace = new VisualElement();
            workspace.style.flexGrow = 1;
            workspace.style.paddingLeft = 12;
            workspace.style.paddingRight = 12;
            workspace.style.paddingTop = 12;
            workspace.style.paddingBottom = 12;
            workspace.style.flexDirection = FlexDirection.Column;

            var chatHeader = new Label("🧊 AI 3D Model Chat Workspace");
            chatHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            chatHeader.style.fontSize = 15;
            chatHeader.style.color = new StyleColor(new Color(0.9f, 0.9f, 0.9f));
            chatHeader.style.marginBottom = 8;
            workspace.Add(chatHeader);

            _chatViewport = new ScrollView();
            _chatViewport.style.flexGrow = 1;
            _chatViewport.style.marginBottom = 8;
            _chatViewport.style.backgroundColor = new StyleColor(new Color(0.14f, 0.15f, 0.17f));
            _chatViewport.style.paddingLeft = 6;
            _chatViewport.style.paddingRight = 6;
            _chatViewport.style.paddingTop = 6;
            _chatViewport.style.paddingBottom = 6;
            _chatViewport.style.borderTopLeftRadius = 6;
            _chatViewport.style.borderTopRightRadius = 6;
            _chatViewport.style.borderBottomLeftRadius = 6;
            _chatViewport.style.borderBottomRightRadius = 6;
            workspace.Add(_chatViewport);

            // Settings Foldout (Drawer)
            var settingsFoldout = new Foldout();
            settingsFoldout.value = true;
            settingsFoldout.text = "⚙ Generation Parameters";
            settingsFoldout.style.flexShrink = 0;

            var settingsContent = new VisualElement();
            settingsContent.style.backgroundColor = new StyleColor(new Color(0.14f, 0.15f, 0.17f));
            settingsContent.style.paddingLeft = 6;
            settingsContent.style.paddingRight = 6;
            settingsContent.style.paddingTop = 6;
            settingsContent.style.paddingBottom = 6;
            settingsContent.style.marginBottom = 6;
            settingsContent.style.borderTopLeftRadius = 4;
            settingsContent.style.borderTopRightRadius = 4;
            settingsContent.style.borderBottomLeftRadius = 4;
            settingsContent.style.borderBottomRightRadius = 4;

            var dropdownsRow = new VisualElement();
            dropdownsRow.style.flexDirection = FlexDirection.Row;
            dropdownsRow.style.justifyContent = Justify.SpaceBetween;
            dropdownsRow.style.marginBottom = 6;

            var providerContainer = new VisualElement { style = { width = Length.Percent(48) } };
            providerContainer.Add(new Label("AI Provider:") { style = { unityFontStyleAndWeight = FontStyle.Bold, color = new StyleColor(new Color(0.8f, 0.8f, 0.8f)), fontSize = 10 } });
            _providerField = new DropdownField { choices = new List<string> { "Three.js Code Generator", "Meshy AI", "Tripo3D" } };
            providerContainer.Add(_providerField);
            dropdownsRow.Add(providerContainer);

            var modelContainer = new VisualElement { style = { width = Length.Percent(48) } };
            var modelLabel = new Label("LLM Model:") { style = { unityFontStyleAndWeight = FontStyle.Bold, color = new StyleColor(new Color(0.8f, 0.8f, 0.8f)), fontSize = 10 } };
            _modelSelector = new DropdownField();

            _providerField.RegisterValueChangedCallback(evt => {
                EditorPrefs.SetString("Omnisense_ModelGen_Provider", evt.newValue);
                UpdateModelSelectorOptions(evt.newValue);
                if (_orchestratorToggle != null)
                {
                    _orchestratorToggle.style.display = evt.newValue == "Three.js Code Generator" ? DisplayStyle.None : DisplayStyle.Flex;
                }
            });

            _modelSelector.RegisterValueChangedCallback(evt => {
                if (evt.newValue == null) return;
                string currentProvider = _providerField.value;
                if (currentProvider == "Three.js Code Generator")
                {
                    EditorPrefs.SetString("Omnisense_ModelGen_LLMModel", evt.newValue);
                    EditorPrefs.SetString("Omnisense_SelectedModel", evt.newValue);
                }
                else if (currentProvider == "Meshy AI")
                {
                    EditorPrefs.SetString("Omnisense_ModelGen_MeshyStyle", evt.newValue);
                }
                else if (currentProvider == "Tripo3D")
                {
                    EditorPrefs.SetString("Omnisense_ModelGen_TripoVersion", evt.newValue);
                    UpdateTripoVersionSelector(evt.newValue);
                }
            });

            _modelSelector.RegisterCallback<PointerDownEvent>(evt => {
                if (_providerField.value == "Three.js Code Generator")
                {
                    evt.StopPropagation();
                    ShowModelMenu();
                }
            }, TrickleDown.TrickleDown);

            _modelSelector.RegisterCallback<MouseDownEvent>(evt => {
                if (_providerField.value == "Three.js Code Generator")
                {
                    evt.StopPropagation();
                    ShowModelMenu();
                }
            }, TrickleDown.TrickleDown);

            modelContainer.Add(modelLabel);
            modelContainer.Add(_modelSelector);
            dropdownsRow.Add(modelContainer);

            // Create Tripo model version row
            _tripoVersionRow = new VisualElement();
            _tripoVersionRow.style.flexDirection = FlexDirection.Row;
            _tripoVersionRow.style.justifyContent = Justify.SpaceBetween;
            _tripoVersionRow.style.marginBottom = 6;
            _tripoVersionRow.style.display = DisplayStyle.None; // Hidden by default

            var tripoVersionContainer = new VisualElement { style = { width = Length.Percent(100) } };
            tripoVersionContainer.Add(new Label("Tripo Model Version:") { style = { unityFontStyleAndWeight = FontStyle.Bold, color = new StyleColor(new Color(0.8f, 0.8f, 0.8f)), fontSize = 10 } });
            _tripoVersionSelector = new DropdownField();
            _tripoVersionSelector.RegisterValueChangedCallback(evt => {
                if (evt.newValue == null) return;
                EditorPrefs.SetString("Omnisense_ModelGen_TripoSpecificVersion", evt.newValue);
            });
            tripoVersionContainer.Add(_tripoVersionSelector);
            _tripoVersionRow.Add(tripoVersionContainer);

            string savedProvider = EditorPrefs.GetString("Omnisense_ModelGen_Provider", "Three.js Code Generator");
            _providerField.value = savedProvider;
            UpdateModelSelectorOptions(savedProvider);

            settingsContent.Add(dropdownsRow);
            settingsContent.Add(_tripoVersionRow);

            _orchestratorToggle = new Toggle("Use LLM Prompt Orchestration") { value = EditorPrefs.GetBool("Omnisense_ModelGen_UseOrchestrator", true) };
            _orchestratorToggle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _orchestratorToggle.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f));
            _orchestratorToggle.style.fontSize = 10;
            _orchestratorToggle.style.marginBottom = 6;
            _orchestratorToggle.style.flexShrink = 0;
            _orchestratorToggle.RegisterValueChangedCallback(evt => {
                EditorPrefs.SetBool("Omnisense_ModelGen_UseOrchestrator", evt.newValue);
            });
            _orchestratorToggle.style.display = savedProvider == "Three.js Code Generator" ? DisplayStyle.None : DisplayStyle.Flex;
            settingsContent.Add(_orchestratorToggle);

            // Path & Conversion settings row
            var pathContainer = new VisualElement();
            pathContainer.Add(new Label("Save Location:") { style = { unityFontStyleAndWeight = FontStyle.Bold, color = new StyleColor(new Color(0.8f, 0.8f, 0.8f)), fontSize = 10 } });
            var pathRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            _pathField = new TextField { value = PlayerPrefs.GetString("model_generation_save_location", "Assets/"), style = { flexGrow = 1 } };
            _pathField.RegisterValueChangedCallback(evt => {
                PlayerPrefs.SetString("model_generation_save_location", evt.newValue);
                PlayerPrefs.Save();
            });
            var browseBtn = new Button(() => {
                string selectedFolder = EditorUtility.OpenFolderPanel("Select Output Directory", "Assets", "");
                if (!string.IsNullOrEmpty(selectedFolder))
                {
                    if (selectedFolder.StartsWith(Application.dataPath))
                    {
                        selectedFolder = "Assets" + selectedFolder.Substring(Application.dataPath.Length);
                    }
                    _pathField.value = selectedFolder;
                }
            }) { text = "..." };
            browseBtn.style.width = 22;
            browseBtn.style.marginLeft = 3;
            pathRow.Add(_pathField);
            pathRow.Add(browseBtn);
            pathContainer.Add(pathRow);
            settingsContent.Add(pathContainer);

            // Manual THREE.js to glTF Converter section inside parameters folder
            var manualSection = new Foldout { text = "Manual THREE.js to glTF Converter", value = false, style = { marginTop = 8 } };
            var manualRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 4 } };
            _jsFileField = new TextField { style = { flexGrow = 1 } };
            var manualBrowse = new Button(() => {
                string file = EditorUtility.OpenFilePanel("Select THREE.js Script", "Assets", "js");
                if (!string.IsNullOrEmpty(file))
                {
                    if (file.StartsWith(Application.dataPath)) file = "Assets" + file.Substring(Application.dataPath.Length);
                    _jsFileField.value = file;
                }
            }) { text = "..." };
            manualBrowse.style.width = 22;
            manualBrowse.style.marginLeft = 3;
            manualRow.Add(_jsFileField);
            manualRow.Add(manualBrowse);
            manualSection.Add(manualRow);

            _convertBtn = new Button(OnConvertClicked) { text = "Convert JS file" };
            _convertBtn.style.marginTop = 4;
            _convertBtn.style.backgroundColor = new StyleColor(new Color(0.2f, 0.44f, 0.68f));
            _convertBtn.style.color = new StyleColor(Color.white);
            manualSection.Add(_convertBtn);

            settingsContent.Add(manualSection);
            settingsFoldout.Add(settingsContent);
            workspace.Add(settingsFoldout);

            // Reference attachment zone
            _attachmentContainer = new VisualElement();
            _attachmentContainer.style.flexDirection = FlexDirection.Row;
            _attachmentContainer.style.alignItems = Align.Center;
            _attachmentContainer.style.paddingLeft = 6;
            _attachmentContainer.style.paddingRight = 6;
            _attachmentContainer.style.paddingTop = 4;
            _attachmentContainer.style.paddingBottom = 4;
            _attachmentContainer.style.marginBottom = 6;
            _attachmentContainer.style.backgroundColor = new StyleColor(new Color(0.14f, 0.15f, 0.17f));
            _attachmentContainer.style.borderTopLeftRadius = 4;
            _attachmentContainer.style.borderTopRightRadius = 4;
            _attachmentContainer.style.borderBottomLeftRadius = 4;
            _attachmentContainer.style.borderBottomRightRadius = 4;
            _attachmentContainer.style.display = DisplayStyle.None;
            _attachmentContainer.style.flexShrink = 0;

            _attachmentThumbnail = new Image { style = { width = 30, height = 30, marginRight = 6 } };
            _attachmentLabel = new Label("") { style = { flexGrow = 1, fontSize = 10, color = new StyleColor(new Color(0.8f, 0.8f, 0.8f)) } };
            var removeAttachmentBtn = new Button(RemoveAttachment) { text = "❌" };
            removeAttachmentBtn.style.width = 20;
            removeAttachmentBtn.style.height = 20;
            removeAttachmentBtn.style.backgroundColor = new StyleColor(Color.clear);
            removeAttachmentBtn.style.borderLeftWidth = 0;
            removeAttachmentBtn.style.borderRightWidth = 0;
            removeAttachmentBtn.style.borderTopWidth = 0;
            removeAttachmentBtn.style.borderBottomWidth = 0;
            removeAttachmentBtn.style.color = new StyleColor(new Color(0.9f, 0.4f, 0.4f));

            _attachmentContainer.Add(_attachmentThumbnail);
            _attachmentContainer.Add(_attachmentLabel);
            _attachmentContainer.Add(removeAttachmentBtn);
            workspace.Add(_attachmentContainer);

            _statusLabel = new Label("Ready to design 3D models...");
            _statusLabel.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.marginBottom = 4;
            _statusLabel.style.flexShrink = 0;
            workspace.Add(_statusLabel);

            // Bottom Input row
            var chatInputRow = new VisualElement();
            chatInputRow.style.flexDirection = FlexDirection.Row;
            chatInputRow.style.alignItems = Align.Center;
            chatInputRow.style.flexShrink = 0;

            _attachBtn = new Button(OnAttachClicked) { text = "📎" };
            _attachBtn.style.height = 35;
            _attachBtn.style.width = 30;
            _attachBtn.style.marginRight = 6;
            _attachBtn.style.fontSize = 14;
            _attachBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            _attachBtn.style.backgroundColor = new StyleColor(new Color(0.25f, 0.27f, 0.3f));
            _attachBtn.style.color = new StyleColor(Color.white);
            _attachBtn.style.borderTopLeftRadius = 4;
            _attachBtn.style.borderTopRightRadius = 4;
            _attachBtn.style.borderBottomLeftRadius = 4;
            _attachBtn.style.borderBottomRightRadius = 4;
            _attachBtn.style.borderLeftWidth = 0;
            _attachBtn.style.borderRightWidth = 0;
            _attachBtn.style.borderTopWidth = 0;
            _attachBtn.style.borderBottomWidth = 0;
            chatInputRow.Add(_attachBtn);

            _promptField = new TextField { multiline = true };
            _promptField.style.flexGrow = 1;
            _promptField.style.minHeight = 35;
            _promptField.style.maxHeight = 60;
            var promptInputEl = _promptField.Q("unity-text-input");
            if (promptInputEl != null)
            {
                promptInputEl.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.12f));
                promptInputEl.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
            }
            chatInputRow.Add(_promptField);

            _generateBtn = new Button(OnSendClicked) { text = "Send" };
            _generateBtn.style.height = 35;
            _generateBtn.style.marginLeft = 6;
            _generateBtn.style.width = 65;
            _generateBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            _generateBtn.style.backgroundColor = new StyleColor(new Color(0.0f, 0.48f, 0.8f));
            _generateBtn.style.color = new StyleColor(Color.white);
            _generateBtn.style.borderTopLeftRadius = 4;
            _generateBtn.style.borderTopRightRadius = 4;
            _generateBtn.style.borderBottomLeftRadius = 4;
            _generateBtn.style.borderBottomRightRadius = 4;
            _generateBtn.style.borderLeftWidth = 0;
            _generateBtn.style.borderRightWidth = 0;
            _generateBtn.style.borderTopWidth = 0;
            _generateBtn.style.borderBottomWidth = 0;
            chatInputRow.Add(_generateBtn);

            workspace.Add(chatInputRow);

            root.Add(workspace);

            // Register drag & drop globally on the right panel
            workspace.RegisterCallback<DragUpdatedEvent>(evt => {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            });
            workspace.RegisterCallback<DragPerformEvent>(OnDragPerform);
        }

        private void RemoveAttachment()
        {
            _selectedReferencePath = "";
            if (_attachmentContainer != null) _attachmentContainer.style.display = DisplayStyle.None;
        }

        private void AttachReferenceImage(string path)
        {
            _selectedReferencePath = path;
            if (_attachmentContainer != null)
            {
                _attachmentContainer.style.display = DisplayStyle.Flex;
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null && _attachmentThumbnail != null)
                {
                    _attachmentThumbnail.image = texture;
                    _attachmentThumbnail.style.width = 30;
                    _attachmentThumbnail.style.height = 30;
                }
                if (_attachmentLabel != null)
                {
                    _attachmentLabel.text = Path.GetFileName(path);
                }
            }
        }

        private void OnDragPerform(DragPerformEvent evt)
        {
            DragAndDrop.AcceptDrag();
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is Texture2D tex)
                {
                    string path = AssetDatabase.GetAssetPath(tex);
                    if (!string.IsNullOrEmpty(path))
                    {
                        AttachReferenceImage(path);
                        return;
                    }
                }
            }

            if (DragAndDrop.paths != null && DragAndDrop.paths.Length > 0)
            {
                foreach (string file in DragAndDrop.paths)
                {
                    string ext = Path.GetExtension(file).ToLower();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                    {
                        ProcessReferenceFile(file);
                        break;
                    }
                }
            }
        }

        private void OnAttachClicked()
        {
            string file = EditorUtility.OpenFilePanel("Select Reference Image", "Assets", "png,jpg,jpeg");
            if (!string.IsNullOrEmpty(file))
            {
                ProcessReferenceFile(file);
            }
        }

        private void ProcessReferenceFile(string file)
        {
            string projectPath = Application.dataPath;
            string cleanFile = file.Replace("\\", "/");
            string cleanProject = projectPath.Replace("\\", "/");

            if (cleanFile.StartsWith(cleanProject))
            {
                string relativePath = "Assets" + cleanFile.Substring(cleanProject.Length);
                AttachReferenceImage(relativePath);
            }
            else
            {
                try
                {
                    string cacheDir = Path.Combine(projectPath, "Omnisense_Cache");
                    if (!Directory.Exists(cacheDir))
                    {
                        Directory.CreateDirectory(cacheDir);
                    }

                    string fileName = Path.GetFileName(cleanFile);
                    string destFile = Path.Combine(cacheDir, fileName);

                    int counter = 1;
                    string baseName = Path.GetFileNameWithoutExtension(fileName);
                    string ext = Path.GetExtension(fileName);
                    while (File.Exists(destFile))
                    {
                        destFile = Path.Combine(cacheDir, $"{baseName}_{counter}{ext}");
                        counter++;
                    }

                    File.Copy(cleanFile, destFile);
                    string relativePath = "Assets/Omnisense_Cache/" + Path.GetFileName(destFile);

                    AssetDatabase.ImportAsset(relativePath);
                    AssetDatabase.Refresh();
                    AttachReferenceImage(relativePath);
                }
                catch (Exception ex)
                {
                    ShowError($"Failed to attach external image: {ex.Message}");
                }
            }
        }

        private void LoadLastOrNewSession()
        {
            _sessions = OmnisenseModelSessionManager.GetAllSessions();
            string lastSessionId = EditorPrefs.GetString("Omnisense_ModelChat_ActiveSessionId", "");

            if (_sessions.Count > 0)
            {
                var match = _sessions.Find(s => s.id == lastSessionId);
                if (match != null)
                {
                    _activeSession = match;
                }
                else
                {
                    _activeSession = _sessions[0];
                }
            }
            else
            {
                _activeSession = OmnisenseModelSessionManager.CreateNewSession();
            }

            EditorPrefs.SetString("Omnisense_ModelChat_ActiveSessionId", _activeSession.id);
            PopulateSessionsList();
            RenderActiveSessionMessages();
        }

        private void CreateNewChat()
        {
            _activeSession = OmnisenseModelSessionManager.CreateNewSession();
            EditorPrefs.SetString("Omnisense_ModelChat_ActiveSessionId", _activeSession.id);
            PopulateSessionsList();
            RenderActiveSessionMessages();
        }

        private void LoadSession(string id)
        {
            var match = OmnisenseModelSessionManager.GetSessionById(id);
            if (match != null)
            {
                _activeSession = match;
                EditorPrefs.SetString("Omnisense_ModelChat_ActiveSessionId", id);
                PopulateSessionsList();
                RenderActiveSessionMessages();
            }
        }

        private void DeleteSession(string id)
        {
            if (EditorUtility.DisplayDialog("Delete Model Chat", "Delete this model chat history permanently?", "Delete", "Cancel"))
            {
                OmnisenseModelSessionManager.DeleteSession(id);
                if (_activeSession != null && _activeSession.id == id)
                {
                    _activeSession = null;
                }
                LoadLastOrNewSession();
            }
        }

        private void PopulateSessionsList()
        {
            if (_chatHistoryContainer == null) return;
            _chatHistoryContainer.Clear();

            _sessions = OmnisenseModelSessionManager.GetAllSessions();
            foreach (var session in _sessions)
            {
                var item = new VisualElement();
                item.style.flexDirection = FlexDirection.Row;
                item.style.justifyContent = Justify.SpaceBetween;
                item.style.alignItems = Align.Center;
                item.style.marginBottom = 4;
                item.style.paddingLeft = 4;
                item.style.paddingRight = 4;
                item.style.paddingTop = 3;
                item.style.paddingBottom = 3;
                item.style.borderBottomWidth = 1;
                item.style.borderBottomColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.3f));
                item.style.borderTopLeftRadius = 4;
                item.style.borderTopRightRadius = 4;
                item.style.borderBottomLeftRadius = 4;
                item.style.borderBottomRightRadius = 4;

                if (_activeSession != null && _activeSession.id == session.id)
                {
                    item.style.backgroundColor = new StyleColor(new Color(0.22f, 0.25f, 0.28f));
                }

                var titleBtn = new Button(() => LoadSession(session.id)) { text = TruncateString(session.name, 18) };
                titleBtn.style.flexGrow = 1;
                titleBtn.style.backgroundColor = new StyleColor(Color.clear);
                titleBtn.style.color = new StyleColor(Color.white);
                titleBtn.style.unityTextAlign = TextAnchor.MiddleLeft;
                titleBtn.style.fontSize = 11;
                titleBtn.style.borderLeftWidth = 0;
                titleBtn.style.borderRightWidth = 0;
                titleBtn.style.borderTopWidth = 0;
                titleBtn.style.borderBottomWidth = 0;

                var delBtn = new Button(() => DeleteSession(session.id)) { text = "🗑" };
                delBtn.style.width = 22;
                delBtn.style.height = 20;
                delBtn.style.backgroundColor = new StyleColor(Color.clear);
                delBtn.style.color = new StyleColor(new Color(0.85f, 0.35f, 0.35f));
                delBtn.style.borderLeftWidth = 0;
                delBtn.style.borderRightWidth = 0;
                delBtn.style.borderTopWidth = 0;
                delBtn.style.borderBottomWidth = 0;

                item.Add(titleBtn);
                item.Add(delBtn);
                _chatHistoryContainer.Add(item);
            }
        }

        private void RenderActiveSessionMessages()
        {
            if (_chatViewport == null) return;
            _chatViewport.Clear();

            if (_activeSession == null) return;

            foreach (var msg in _activeSession.messages)
            {
                _chatViewport.Add(CreateMessageBubble(msg));
            }

            EditorApplication.delayCall += () => {
                if (_chatViewport != null)
                {
                    _chatViewport.scrollOffset = new Vector2(0, float.MaxValue);
                }
            };
        }

        private VisualElement CreateMessageBubble(ModelChatMessage msg)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Column;
            container.style.marginBottom = 10;

            var bubble = new VisualElement();
            bubble.style.paddingLeft = 10;
            bubble.style.paddingRight = 10;
            bubble.style.paddingTop = 8;
            bubble.style.paddingBottom = 8;
            bubble.style.borderTopLeftRadius = 6;
            bubble.style.borderTopRightRadius = 6;
            bubble.style.borderBottomLeftRadius = 6;
            bubble.style.borderBottomRightRadius = 6;
            bubble.style.maxWidth = Length.Percent(80);

            if (msg.sender == "user")
            {
                bubble.style.alignSelf = Align.FlexEnd;
                bubble.style.backgroundColor = new StyleColor(new Color(0.20f, 0.35f, 0.50f));
                bubble.style.marginLeft = 40;

                var label = CreateSelectableLabel(msg.content, Color.white);
                bubble.Add(label);

                if (!string.IsNullOrEmpty(msg.referenceImageBase64))
                {
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(msg.referenceImageBase64);
                    if (texture != null)
                    {
                        var attachmentLabel = new Label($"📎 Reference Art: {msg.referenceImageName}");
                        attachmentLabel.style.fontSize = 9;
                        attachmentLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                        attachmentLabel.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
                        attachmentLabel.style.marginTop = 4;
                        bubble.Add(attachmentLabel);

                        var img = new Image { image = texture };
                        img.style.width = 120;
                        img.style.height = 120;
                        img.style.marginTop = 4;
                        bubble.Add(img);
                    }
                }
            }
            else // assistant
            {
                bubble.style.alignSelf = Align.FlexStart;
                bubble.style.backgroundColor = new StyleColor(new Color(0.12f, 0.13f, 0.15f));
                bubble.style.marginRight = 40;

                var label = CreateSelectableLabel(msg.content, new Color(0.9f, 0.9f, 0.9f));
                bubble.Add(label);

                if (!string.IsNullOrEmpty(msg.generatedScriptPath) || !string.IsNullOrEmpty(msg.generatedModelPath))
                {
                    var detailsBox = new VisualElement();
                    detailsBox.style.backgroundColor = new StyleColor(new Color(0.18f, 0.19f, 0.21f));
                    detailsBox.style.paddingLeft = 6;
                    detailsBox.style.paddingRight = 6;
                    detailsBox.style.paddingTop = 6;
                    detailsBox.style.paddingBottom = 6;
                    detailsBox.style.marginTop = 6;
                    detailsBox.style.borderTopLeftRadius = 4;
                    detailsBox.style.borderTopRightRadius = 4;
                    detailsBox.style.borderBottomLeftRadius = 4;
                    detailsBox.style.borderBottomRightRadius = 4;

                    if (!string.IsNullOrEmpty(msg.generatedScriptPath))
                    {
                        detailsBox.Add(new Label($"THREE.js Script: {Path.GetFileName(msg.generatedScriptPath)}") { style = { fontSize = 10, color = new StyleColor(new Color(0.8f, 0.8f, 0.8f)) } });
                    }
                    if (!string.IsNullOrEmpty(msg.generatedModelPath))
                    {
                        detailsBox.Add(new Label($"Model glTF: {Path.GetFileName(msg.generatedModelPath)}") { style = { fontSize = 10, color = new StyleColor(new Color(0.8f, 0.8f, 0.8f)), marginTop = 2 } });
                    }
                    bubble.Add(detailsBox);

                    var buttonRow = new VisualElement();
                    buttonRow.style.flexDirection = FlexDirection.Row;
                    buttonRow.style.marginTop = 6;

                    if (!string.IsNullOrEmpty(msg.generatedModelPath) && File.Exists(msg.generatedModelPath))
                    {
                        var instantiateBtn = new Button(() => InstantiateModelInScene(msg.generatedModelPath)) { text = "🏠 Instantiate in Scene" };
                        instantiateBtn.style.fontSize = 10;
                        instantiateBtn.style.backgroundColor = new StyleColor(new Color(0.15f, 0.45f, 0.25f));
                        instantiateBtn.style.color = new StyleColor(Color.white);
                        instantiateBtn.style.marginRight = 5;
                        buttonRow.Add(instantiateBtn);

                        var selectBtn = new Button(() => {
                            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(msg.generatedModelPath);
                            if (asset != null)
                            {
                                Selection.activeObject = asset;
                                EditorGUIUtility.PingObject(asset);
                            }
                        }) { text = "🔍 Select Asset" };
                        selectBtn.style.fontSize = 10;
                        selectBtn.style.backgroundColor = new StyleColor(new Color(0.25f, 0.27f, 0.3f));
                        selectBtn.style.color = new StyleColor(Color.white);
                        selectBtn.style.marginRight = 5;
                        buttonRow.Add(selectBtn);
                    }

                    if (!string.IsNullOrEmpty(msg.generatedScriptPath) && File.Exists(msg.generatedScriptPath))
                    {
                        var manualConvertBtn = new Button(() => {
                            _jsFileField.value = msg.generatedScriptPath;
                            OnConvertClicked();
                        }) { text = "🛠 Convert JS" };
                        manualConvertBtn.style.fontSize = 10;
                        manualConvertBtn.style.backgroundColor = new StyleColor(new Color(0.2f, 0.44f, 0.68f));
                        manualConvertBtn.style.color = new StyleColor(Color.white);
                        buttonRow.Add(manualConvertBtn);
                    }

                    bubble.Add(buttonRow);
                }
            }

            container.Add(bubble);
            return container;
        }

        private TextField CreateSelectableLabel(string text, Color textColor)
        {
            var field = new TextField { value = text, isReadOnly = true, multiline = true };
            field.style.backgroundColor = Color.clear;
            field.style.borderLeftWidth = 0;
            field.style.borderRightWidth = 0;
            field.style.borderTopWidth = 0;
            field.style.borderBottomWidth = 0;
            field.style.paddingLeft = 0;
            field.style.paddingRight = 0;
            field.style.paddingTop = 0;
            field.style.paddingBottom = 0;
            field.style.marginTop = 0;
            field.style.marginBottom = 0;
            field.style.marginLeft = 0;
            field.style.marginRight = 0;
            field.style.whiteSpace = WhiteSpace.Normal;
            field.style.flexGrow = 1;

            var input = field.Q("unity-text-input");
            if (input != null)
            {
                input.style.backgroundColor = Color.clear;
                input.style.borderLeftWidth = 0;
                input.style.borderRightWidth = 0;
                input.style.borderTopWidth = 0;
                input.style.borderBottomWidth = 0;
                input.style.paddingLeft = 0;
                input.style.paddingRight = 0;
                input.style.paddingTop = 0;
                input.style.paddingBottom = 0;
                input.style.color = new StyleColor(textColor);
                input.style.whiteSpace = WhiteSpace.Normal;
            }
            return field;
        }

        private void InstantiateModelInScene(string assetPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset != null)
            {
                var go = PrefabUtility.InstantiatePrefab(asset) as GameObject;
                if (go != null)
                {
                    go.name = Path.GetFileNameWithoutExtension(assetPath);
                    Undo.RegisterCreatedObjectUndo(go, "Instantiate AI Model");
                    Selection.activeGameObject = go;
                    Debug.Log($"[Omnisense-3D] Instantiated model in scene: {assetPath}");
                }
            }
            else
            {
                Debug.LogWarning($"[Omnisense-3D] Unable to load asset as GameObject: {assetPath}");
            }
        }

        private void OnSendClicked()
        {
            string promptText = _promptField.value.Trim();
            OmnisenseLogger.Log($"[3D Model Generator] Send button clicked. Prompt: \"{promptText}\", reference art: \"{_selectedReferencePath}\"", "3D_MODEL_GEN");

            if (string.IsNullOrEmpty(promptText) && string.IsNullOrEmpty(_selectedReferencePath))
            {
                return;
            }

            if (_activeSession == null)
            {
                _activeSession = OmnisenseModelSessionManager.CreateNewSession();
            }

            var userMsg = new ModelChatMessage {
                sender = "user",
                content = promptText,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            if (!string.IsNullOrEmpty(_selectedReferencePath))
            {
                userMsg.referenceImageName = Path.GetFileName(_selectedReferencePath);
                userMsg.referenceImageBase64 = _selectedReferencePath;
            }

            _activeSession.messages.Add(userMsg);

            if (_activeSession.name.StartsWith("Model Chat ") && !string.IsNullOrEmpty(promptText))
            {
                _activeSession.name = TruncateString(promptText, 25);
            }

            OmnisenseModelSessionManager.SaveSession(_activeSession);

            _promptField.value = "";
            string attachedPath = _selectedReferencePath;
            _lastAttachedPath = attachedPath;
            RemoveAttachment();

            PopulateSessionsList();
            RenderActiveSessionMessages();

            string providerMode = _providerField.value;
            bool useOrchestrator = providerMode == "Three.js Code Generator" || _orchestratorToggle.value;

            if (!useOrchestrator)
            {
                if (!string.IsNullOrEmpty(attachedPath) && providerMode != "Tripo3D")
                {
                    OmnisenseLogger.LogWarning("[3D Model Generator] Reference image attached but Orchestrator is disabled. Reference image content will not be processed.", "3D_MODEL_GEN");
                }
                TriggerCloudModelGeneration("Direct prompt execution (No Orchestration).", promptText);
            }
            else
            {
                StartLlmOrchestrator(promptText, attachedPath);
            }
        }

        private void StartLlmOrchestrator(string userPrompt, string attachedPath)
        {
            string providerMode = _providerField.value;
            string model = providerMode == "Three.js Code Generator" ? _modelSelector.value : EditorPrefs.GetString("Omnisense_ModelGen_LLMModel", "gpt-5.4-mini");
            string apiKey = GetApiKey(model);
            if (string.IsNullOrEmpty(apiKey) && model != "self-hosted")
            {
                ShowError("Orchestrator API Key is missing. Please configure it in Settings.");
                return;
            }

            SetLoadingState(true);
            ShowStatus("Thinking and designing 3D assets (30-45 seconds)...");

            ILLMProvider providerImpl = LLMProviderFactory.GetProvider(model);
            if (providerImpl == null)
            {
                SetLoadingState(false);
                ShowError($"Unsupported LLM model: {model}");
                return;
            }
            var messages = new List<LLMMessage>();
            messages.Add(new LLMMessage {
                role = "system",
                content = "You are the Omnisense AI 3D Model Orchestrator. Your role is to help the user refine, describe, design, and compile 3D assets inside the Unity editor.\n" +
                          $"CRITICAL: The active 3D Generation Provider is currently set to: '{providerMode}'.\n" +
                          "You MUST generate the required 3D asset in the first go. Do not just chat, ask questions, or propose ideas. Proceed to generate immediately:\n" +
                          "1. If the provider is 'Three.js Code Generator', you MUST write the complete, valid, executable Three.js JavaScript code that constructs the requested 3D model and populate it in 'threeJsCode'. Add all constructed meshes directly to the pre-provided 'scene' object (e.g. scene.add(mesh)). Do NOT instantiate a new scene, write window headers, or html wraps. Set 'optimizedPrompt' to \"\".\n" +
                          "2. If the provider is 'Meshy AI' or 'Tripo3D', you MUST output a highly-detailed, optimized prompt describing the 3D asset and populate it in 'optimizedPrompt'. Set 'threeJsCode' to \"\". IMPORTANT: The 'optimizedPrompt' MUST be concise and strictly UNDER 800 characters total. Focus on high-impact keywords, main features, style, and materials to stay within this limit.\n\n" +
                          "Format your entire response strictly as a JSON block with no other markdown wrap, matching this schema:\n" +
                          "{\n  \"explanation\": \"your explanation here\",\n  \"threeJsCode\": \"your Three.js script here\",\n  \"optimizedPrompt\": \"your optimized 3D prompt here\"\n}"
            });

            foreach (var msg in _activeSession.messages)
            {
                string body = msg.content;
                if (msg.sender == "user" && !string.IsNullOrEmpty(msg.referenceImageBase64) && File.Exists(msg.referenceImageBase64))
                {
                    body += $"\n{{\"screenshot_path\":\"{msg.referenceImageBase64}\"}}";
                }
                messages.Add(new LLMMessage {
                    role = msg.sender,
                    content = body
                });
            }

            int maxTokens = GetMaxTokens(model);
            _activeLlmRequest = providerImpl.BuildRequest(apiKey, model, messages, maxTokens);
            _llmRequestStartTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += CheckLlmOrchestratorProgress;
            _activeLlmRequest.SendWebRequest();
        }

        private void CheckLlmOrchestratorProgress()
        {
            if (_activeLlmRequest == null)
            {
                EditorApplication.update -= CheckLlmOrchestratorProgress;
                return;
            }

            double elapsed = EditorApplication.timeSinceStartup - _llmRequestStartTime;

            if (_activeLlmRequest.isDone)
            {
                EditorApplication.update -= CheckLlmOrchestratorProgress;
                SetLoadingState(false);

                if (_activeLlmRequest.result == UnityWebRequest.Result.Success)
                {
                    string rawResponse = _activeLlmRequest.downloadHandler.text;
                    string providerMode = _providerField.value;
                    string model = providerMode == "Three.js Code Generator" ? _modelSelector.value : EditorPrefs.GetString("Omnisense_ModelGen_LLMModel", "gpt-5.4-mini");
                    ILLMProvider providerImpl = LLMProviderFactory.GetProvider(model);
                    string parsedContent = providerImpl.ParseResponseContent(rawResponse);
                    OmnisenseLogger.Log($"[3D Model Generator] LLM orchestration request succeeded. Response content: {parsedContent}", "3D_MODEL_GEN");

                    ProcessOrchestratorReply(parsedContent);
                }
                else
                {
                    string err = _activeLlmRequest.downloadHandler?.text ?? _activeLlmRequest.error;
                    OmnisenseLogger.LogError($"[3D Model Generator] LLM orchestration request failed. Error: {_activeLlmRequest.error}, Details: {err}", "3D_MODEL_GEN");
                    ShowError($"LLM Request Failed: {_activeLlmRequest.error}");
                }
                _activeLlmRequest.Dispose();
                _activeLlmRequest = null;
            }
            else if (elapsed > 180)
            {
                EditorApplication.update -= CheckLlmOrchestratorProgress;
                _activeLlmRequest.Abort();
                _activeLlmRequest.Dispose();
                _activeLlmRequest = null;
                SetLoadingState(false);
                OmnisenseLogger.LogError($"[3D Model Generator] LLM orchestration request timed out after {elapsed:F0} seconds.", "3D_MODEL_GEN");
                ShowError($"LLM Request timed out after {elapsed:F0} seconds.");
            }
        }

        [Serializable]
        private class ModelResponseDTO
        {
            public string explanation;
            public string threeJsCode;
            public string optimizedPrompt;
        }

        private void ProcessOrchestratorReply(string rawResponse)
        {
            string explanation = "";
            string threeJsCode = "";
            string optimizedPrompt = "";

            string cleanResponse = rawResponse.Trim();
            var match = Regex.Match(cleanResponse, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                cleanResponse = match.Groups[1].Value.Trim();
            }

            try
            {
                if (cleanResponse.StartsWith("{"))
                {
                    var dto = JsonUtility.FromJson<ModelResponseDTO>(cleanResponse);
                    explanation = dto.explanation;
                    threeJsCode = dto.threeJsCode;
                    optimizedPrompt = dto.optimizedPrompt;
                }
                else
                {
                    explanation = "Processing output...";
                    string provider = _providerField.value;
                    if (provider == "Three.js Code Generator") threeJsCode = cleanResponse;
                    else optimizedPrompt = cleanResponse;
                }
            }
            catch
            {
                explanation = "Processing output...";
                string provider = _providerField.value;
                if (provider == "Three.js Code Generator") threeJsCode = cleanResponse;
                else optimizedPrompt = cleanResponse;
            }

            if (string.IsNullOrEmpty(explanation)) explanation = "Generated 3D asset successfully.";

            string providerMode = _providerField.value;
            if (providerMode == "Three.js Code Generator" && !string.IsNullOrEmpty(threeJsCode))
            {
                SaveThreeJsAndConvert(explanation, threeJsCode);
            }
            else if ((providerMode == "Meshy AI" || providerMode == "Tripo3D") && !string.IsNullOrEmpty(optimizedPrompt))
            {
                TriggerCloudModelGeneration(explanation, optimizedPrompt);
            }
            else
            {
                var assistantMsg = new ModelChatMessage {
                    sender = "assistant",
                    content = explanation,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };
                _activeSession.messages.Add(assistantMsg);
                OmnisenseModelSessionManager.SaveSession(_activeSession);
                RenderActiveSessionMessages();
            }
        }

        private void SaveThreeJsAndConvert(string explanation, string code)
        {
            _pendingExplanation = explanation;

            var jsMatch = Regex.Match(code, @"```(?:javascript|js)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
            if (jsMatch.Success)
            {
                code = jsMatch.Groups[1].Value;
            }
            code = code.Trim();

            string rawPath = _pathField.value.Trim();
            if (string.IsNullOrEmpty(rawPath)) rawPath = "Assets/";

            string targetDir = rawPath;
            string finalPath = rawPath;

            if (rawPath.EndsWith(".js"))
            {
                targetDir = Path.GetDirectoryName(rawPath);
            }
            else
            {
                if (!targetDir.EndsWith("/") && !targetDir.EndsWith("\\"))
                {
                    targetDir += "/";
                }
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                finalPath = targetDir + "model_" + timestamp + ".js";
            }

            try
            {
                string absoluteDir = targetDir;
                if (targetDir.StartsWith("Assets"))
                {
                    absoluteDir = Path.Combine(Application.dataPath, "..", targetDir);
                }

                if (!Directory.Exists(absoluteDir))
                {
                    Directory.CreateDirectory(absoluteDir);
                }

                string ext = Path.GetExtension(finalPath);
                if (string.IsNullOrEmpty(ext)) ext = ".js";
                string baseName = Path.GetFileNameWithoutExtension(finalPath);
                string absoluteFilePath = Path.Combine(absoluteDir, baseName + ext);

                int counter = 1;
                while (File.Exists(absoluteFilePath))
                {
                    absoluteFilePath = Path.Combine(absoluteDir, $"{baseName}_{counter}{ext}");
                    finalPath = Path.Combine(targetDir, $"{baseName}_{counter}{ext}").Replace("\\", "/");
                    counter++;
                }

                File.WriteAllText(absoluteFilePath, code);
                AssetDatabase.ImportAsset(finalPath);
                AssetDatabase.Refresh();

                _jsFileField.value = finalPath;

                string absoluteGltfPath = Path.ChangeExtension(absoluteFilePath, ".gltf");
                string gltfPath = Path.ChangeExtension(finalPath, ".gltf").Replace("\\", "/");

                SetLoadingState(true);
                ShowStatus("Three.js code saved. Converting to glTF...");
                EnsureNodeDependencies();

                ConvertThreeJsToGltf(absoluteFilePath, absoluteGltfPath, (success, result) => {
                    SetLoadingState(false);
                    if (success)
                    {
                        AssetDatabase.ImportAsset(gltfPath);
                        AssetDatabase.Refresh();

                        var assistantMsg = new ModelChatMessage {
                            sender = "assistant",
                            content = _pendingExplanation,
                            generatedScriptPath = finalPath,
                            generatedModelPath = gltfPath,
                            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        };
                        _activeSession.messages.Add(assistantMsg);
                        OmnisenseModelSessionManager.SaveSession(_activeSession);
                        ShowSuccess($"Three.js model generated and converted to glTF successfully at: {gltfPath}");
                        RenderActiveSessionMessages();
                    }
                    else
                    {
                        AddSystemErrorMessage(_pendingExplanation, $"Three.js script saved at {finalPath}, but glTF Conversion Failed: {result}");
                    }
                });
            }
            catch (Exception ex)
            {
                AddSystemErrorMessage(explanation, $"Failed to write Three.js file: {ex.Message}");
            }
        }

        private void TriggerCloudModelGeneration(string explanation, string prompt)
        {
            _pendingExplanation = explanation;
            string provider = _providerField.value;

            // Enforce prompt limits to prevent API payload errors
            string cleanPrompt = prompt;
            if (cleanPrompt.Length > 1000)
            {
                cleanPrompt = cleanPrompt.Substring(0, 1000);
                OmnisenseLogger.LogWarning($"[3D Model Generator] Warning: Optimized prompt exceeded 1000 characters ({prompt.Length}). Truncated to stay within API limit.", "3D_MODEL_GEN");
            }

            if (provider == "Meshy AI")
            {
                string apiKey = EditorPrefs.GetString("Omnisense_Meshy_Key", "");
                if (string.IsNullOrEmpty(apiKey))
                {
                    AddSystemErrorMessage(explanation, "Meshy API Key is missing. Please configure it in Settings.");
                    return;
                }

                SetLoadingState(true);
                ShowStatus("Contacting Meshy AI to queue model generation...");

                string artStyle = _modelSelector.value;
                string url = "https://api.meshy.ai/openapi/v2/text-to-3d";
                string body = "{" +
                    $"\"mode\":\"preview\"," +
                    $"\"prompt\":\"{JsonEscape(cleanPrompt)}\"," +
                    $"\"art_style\":\"{artStyle}\"" +
                    "}";

                OmnisenseLogger.Log($"[3D Model Generator] Triggering Meshy AI task. URL: {url}\nPayload: {body}", "3D_MODEL_GEN");

                var req = new UnityWebRequest(url, "POST");
                req.timeout = 60;
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(body);
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", "Bearer " + apiKey);

                _activeCloudRequest = req;
                _cloudRequestStartTime = EditorApplication.timeSinceStartup;
                EditorApplication.update += CheckMeshyRequestProgress;
                _activeCloudRequest.SendWebRequest();
            }
            else if (provider == "Tripo3D")
            {
                string apiKey = EditorPrefs.GetString("Omnisense_Tripo3D_Key", "");
                if (string.IsNullOrEmpty(apiKey))
                {
                    AddSystemErrorMessage(explanation, "Tripo3D API Key is missing. Please configure it in Settings.");
                    return;
                }

                SetLoadingState(true);

                string tripoMode = _modelSelector.value;
                bool needsImage = tripoMode == "Standard Image to Model" ||
                                  tripoMode == "P Image to Model" ||
                                  tripoMode == "Standard Multiview to Model" ||
                                  tripoMode == "P Multiview to Model" ||
                                  tripoMode == "Image to Gaussian Splat";

                if (needsImage && string.IsNullOrEmpty(_lastAttachedPath))
                {
                    SetLoadingState(false);
                    ShowStatus("Ready to design 3D models...");
                    AddSystemErrorMessage(explanation, "This mode requires an attached reference image. Please drag & drop or click the paperclip to attach one.");
                    return;
                }

                if (needsImage)
                {
                    UploadImageToTripo(_lastAttachedPath, apiKey, (fileToken) => {
                        SubmitTripoGenerationTask(apiKey, tripoMode, fileToken, cleanPrompt);
                    });
                }
                else
                {
                    SubmitTripoGenerationTask(apiKey, tripoMode, null, cleanPrompt);
                }
            }
        }

        private void UploadImageToTripo(string localImagePath, string apiKey, Action<string> onUploadComplete)
        {
            ShowStatus("Requesting upload ticket from Tripo3D...");
            string format = Path.GetExtension(localImagePath).TrimStart('.').ToLower();
            if (string.IsNullOrEmpty(format)) format = "png";

            string presignUrl = "https://openapi.tripo3d.ai/v3/files/presign";
            string body = "{\"format\":\"" + format + "\"}";

            var req = new UnityWebRequest(presignUrl, "POST");
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(body);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);

            req.SendWebRequest().completed += (op) =>
            {
                if (req.result != UnityWebRequest.Result.Success)
                {
                    string err = req.downloadHandler?.text ?? req.error;
                    OmnisenseLogger.LogError($"[3D Model Generator] Tripo3D Presign request failed: {err}", "3D_MODEL_GEN");
                    SetLoadingState(false);
                    AddSystemErrorMessage(_pendingExplanation, $"Tripo3D upload ticket request failed: {err}");
                    req.Dispose();
                    return;
                }

                string json = req.downloadHandler.text;
                req.Dispose();

                string presignedUrl = "";
                string fileToken = "";

                var urlMatch = Regex.Match(json, @"""presigned_url""\s*:\s*""([^""]+)""");
                if (urlMatch.Success) presignedUrl = urlMatch.Groups[1].Value.Replace("\\/", "/");

                var tokenMatch = Regex.Match(json, @"""file_token""\s*:\s*""([^""]+)""");
                if (tokenMatch.Success) fileToken = tokenMatch.Groups[1].Value;

                if (string.IsNullOrEmpty(presignedUrl) || string.IsNullOrEmpty(fileToken))
                {
                    OmnisenseLogger.LogError($"[3D Model Generator] Failed to parse presigned_url or file_token from: {json}", "3D_MODEL_GEN");
                    SetLoadingState(false);
                    AddSystemErrorMessage(_pendingExplanation, "Failed to parse upload ticket from Tripo3D.");
                    return;
                }

                UploadBytesToPresignedUrl(localImagePath, presignedUrl, fileToken, onUploadComplete);
            };
        }

        private void UploadBytesToPresignedUrl(string localImagePath, string presignedUrl, string fileToken, Action<string> onUploadComplete)
        {
            ShowStatus("Uploading reference image to Tripo3D...");
            string absolutePath = localImagePath;
            if (localImagePath.StartsWith("Assets"))
            {
                absolutePath = Path.Combine(Application.dataPath, "..", localImagePath);
            }

            if (!File.Exists(absolutePath))
            {
                SetLoadingState(false);
                AddSystemErrorMessage(_pendingExplanation, $"Reference image not found at: {localImagePath}");
                return;
            }

            byte[] bytes = File.ReadAllBytes(absolutePath);
            var req = new UnityWebRequest(presignedUrl, "PUT");
            req.uploadHandler = new UploadHandlerRaw(bytes);
            req.downloadHandler = new DownloadHandlerBuffer();

            req.SendWebRequest().completed += (op) =>
            {
                if (req.result != UnityWebRequest.Result.Success)
                {
                    string err = req.downloadHandler?.text ?? req.error;
                    OmnisenseLogger.LogError($"[3D Model Generator] Tripo3D image upload PUT failed: {err}", "3D_MODEL_GEN");
                    SetLoadingState(false);
                    AddSystemErrorMessage(_pendingExplanation, $"Tripo3D image upload failed: {err}");
                    req.Dispose();
                    return;
                }

                req.Dispose();
                OmnisenseLogger.Log($"[3D Model Generator] Successfully uploaded image to Tripo3D. File Token: {fileToken}", "3D_MODEL_GEN");
                onUploadComplete?.Invoke(fileToken);
            };
        }

        private void SubmitTripoGenerationTask(string apiKey, string tripoMode, string fileToken, string prompt)
        {
            string url = "https://openapi.tripo3d.ai/v3/generation/text-to-model";
            string body = "";

            string specificVersion = _tripoVersionSelector != null ? _tripoVersionSelector.value : "v3.1";
            string actualVersion = GetMappedTripoVersion(specificVersion);

            if (tripoMode == "Standard Text to Model")
            {
                url = "https://openapi.tripo3d.ai/v3/generation/text-to-model";
                body = "{" +
                    $"\"prompt\":\"{JsonEscape(prompt)}\"," +
                    $"\"model\":\"{actualVersion}\"" +
                    "}";
            }
            else if (tripoMode == "P Text to Model")
            {
                url = "https://openapi.tripo3d.ai/v3/generation/text-to-model";
                body = "{" +
                    $"\"prompt\":\"{JsonEscape(prompt)}\"," +
                    $"\"model\":\"{actualVersion}\"" +
                    "}";
            }
            else if (tripoMode == "Standard Image to Model")
            {
                url = "https://openapi.tripo3d.ai/v3/generation/image-to-model";
                body = "{" +
                    $"\"input\":\"{fileToken}\"," +
                    $"\"model\":\"{actualVersion}\"" +
                    "}";
            }
            else if (tripoMode == "P Image to Model")
            {
                url = "https://openapi.tripo3d.ai/v3/generation/image-to-model";
                body = "{" +
                    $"\"input\":\"{fileToken}\"," +
                    $"\"model\":\"{actualVersion}\"" +
                    "}";
            }
            else if (tripoMode == "Standard Multiview to Model")
            {
                url = "https://openapi.tripo3d.ai/v3/generation/multiview-to-model";
                body = "{" +
                    "\"inputs\":[" +
                    "{\"front\":\"" + fileToken + "\"}" +
                    "]," +
                    $"\"model\":\"{actualVersion}\"" +
                    "}";
            }
            else if (tripoMode == "P Multiview to Model")
            {
                url = "https://openapi.tripo3d.ai/v3/generation/multiview-to-model";
                body = "{" +
                    "\"inputs\":[" +
                    "{\"front\":\"" + fileToken + "\"}" +
                    "]," +
                    $"\"model\":\"{actualVersion}\"" +
                    "}";
            }
            else if (tripoMode == "Image to Gaussian Splat")
            {
                url = "https://openapi.tripo3d.ai/v3/generation/image-to-splat";
                body = "{" +
                    $"\"input\":\"{fileToken}\"" +
                    "}";
            }
            else
            {
                url = "https://openapi.tripo3d.ai/v3/generation/text-to-model";
                body = "{" +
                    $"\"prompt\":\"{JsonEscape(prompt)}\"," +
                    "\"model\":\"v3.1-20260211\"" +
                    "}";
            }

            ShowStatus("Contacting Tripo3D to queue generation task...");
            OmnisenseLogger.Log($"[3D Model Generator] Submitting task to Tripo3D. Mode: {tripoMode}, URL: {url}\nPayload: {body}", "3D_MODEL_GEN");

            var req = new UnityWebRequest(url, "POST");
            req.timeout = 60;
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(body);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);

            _activeCloudRequest = req;
            _cloudRequestStartTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += CheckTripoRequestProgress;
            _activeCloudRequest.SendWebRequest();
        }

        private void CheckThreeJsRequestProgress()
        {
            // Obsolete in chat mode, but preserved as fallback signature
        }

        private void CheckMeshyRequestProgress()
        {
            if (_activeCloudRequest == null)
            {
                EditorApplication.update -= CheckMeshyRequestProgress;
                return;
            }

            double elapsed = EditorApplication.timeSinceStartup - _cloudRequestStartTime;

            if (_activeCloudRequest.isDone)
            {
                EditorApplication.update -= CheckMeshyRequestProgress;
                if (_activeCloudRequest.result == UnityWebRequest.Result.Success)
                {
                    string json = _activeCloudRequest.downloadHandler.text;
                    OmnisenseLogger.Log($"[3D Model Generator] Meshy AI task creation request succeeded. Response JSON: {json}", "3D_MODEL_GEN");
                    var match = Regex.Match(json, @"""id""\s*:\s*""([^""]+)""");
                    if (match.Success)
                    {
                        string taskId = match.Groups[1].Value;
                        OmnisenseLogger.Log($"[3D Model Generator] Extracted Meshy Task ID: {taskId}. Starting polling loop.", "3D_MODEL_GEN");
                        StartPolling(taskId, "Meshy AI");
                    }
                    else
                    {
                        OmnisenseLogger.LogError($"[3D Model Generator] Failed to parse task ID from Meshy response JSON: {json}", "3D_MODEL_GEN");
                        SetLoadingState(false);
                        AddSystemErrorMessage(_pendingExplanation, $"Failed to parse task ID from Meshy response:\n{json}");
                    }
                }
                else
                {
                    string err = _activeCloudRequest.downloadHandler?.text ?? _activeCloudRequest.error;
                    OmnisenseLogger.LogError($"[3D Model Generator] Meshy Task creation request failed. Status: {_activeCloudRequest.responseCode}, Error: {_activeCloudRequest.error}, Details: {err}", "3D_MODEL_GEN");
                    SetLoadingState(false);
                    AddSystemErrorMessage(_pendingExplanation, $"Meshy Task creation failed: {err}");
                }
                _activeCloudRequest.Dispose();
                _activeCloudRequest = null;
            }
            else if (elapsed > 60)
            {
                EditorApplication.update -= CheckMeshyRequestProgress;
                _activeCloudRequest.Abort();
                _activeCloudRequest.Dispose();
                _activeCloudRequest = null;
                SetLoadingState(false);
                OmnisenseLogger.LogError($"[3D Model Generator] Meshy Task creation request timed out after {elapsed:F0} seconds.", "3D_MODEL_GEN");
                AddSystemErrorMessage(_pendingExplanation, $"Meshy task creation request timed out after {elapsed:F0} seconds.");
            }
        }

        private void CheckTripoRequestProgress()
        {
            if (_activeCloudRequest == null)
            {
                EditorApplication.update -= CheckTripoRequestProgress;
                return;
            }

            double elapsed = EditorApplication.timeSinceStartup - _cloudRequestStartTime;

            if (_activeCloudRequest.isDone)
            {
                EditorApplication.update -= CheckTripoRequestProgress;
                if (_activeCloudRequest.result == UnityWebRequest.Result.Success)
                {
                    string json = _activeCloudRequest.downloadHandler.text;
                    OmnisenseLogger.Log($"[3D Model Generator] Tripo3D task creation request succeeded. Response JSON: {json}", "3D_MODEL_GEN");
                    var match = Regex.Match(json, @"""task_id""\s*:\s*""([^""]+)""");
                    if (match.Success)
                    {
                        string taskId = match.Groups[1].Value;
                        OmnisenseLogger.Log($"[3D Model Generator] Extracted Tripo3D Task ID: {taskId}. Starting polling loop.", "3D_MODEL_GEN");
                        StartPolling(taskId, "Tripo3D");
                    }
                    else
                    {
                        OmnisenseLogger.LogError($"[3D Model Generator] Failed to parse task ID from Tripo3D response JSON: {json}", "3D_MODEL_GEN");
                        SetLoadingState(false);
                        AddSystemErrorMessage(_pendingExplanation, $"Failed to parse task ID from Tripo3D response:\n{json}");
                    }
                }
                else
                {
                    string err = _activeCloudRequest.downloadHandler?.text ?? _activeCloudRequest.error;
                    OmnisenseLogger.LogError($"[3D Model Generator] Tripo3D Task creation request failed. Status: {_activeCloudRequest.responseCode}, Error: {_activeCloudRequest.error}, Details: {err}", "3D_MODEL_GEN");
                    SetLoadingState(false);
                    AddSystemErrorMessage(_pendingExplanation, $"Tripo3D Task creation failed: {err}");
                }
                _activeCloudRequest.Dispose();
                _activeCloudRequest = null;
            }
            else if (elapsed > 60)
            {
                EditorApplication.update -= CheckTripoRequestProgress;
                _activeCloudRequest.Abort();
                _activeCloudRequest.Dispose();
                _activeCloudRequest = null;
                SetLoadingState(false);
                OmnisenseLogger.LogError($"[3D Model Generator] Tripo3D Task creation request timed out after {elapsed:F0} seconds.", "3D_MODEL_GEN");
                AddSystemErrorMessage(_pendingExplanation, $"Tripo3D task creation request timed out after {elapsed:F0} seconds.");
            }
        }

        private void StartPolling(string taskId, string provider)
        {
            _taskId = taskId;
            _pollingProvider = provider;
            _lastPollTime = EditorApplication.timeSinceStartup;
            _pollAttempts = 0;
            SetLoadingState(true);
            ShowStatus("Task queued. Generating model (1-3 minutes)...");
            OmnisenseLogger.Log($"[3D Model Generator] Initializing task polling loop for provider '{provider}'. Task ID: {taskId}", "3D_MODEL_GEN");
            EditorApplication.update += PollTaskStatus;
        }

        private void PollTaskStatus()
        {
            double time = EditorApplication.timeSinceStartup;
            if (time - _lastPollTime < 3.0f) return;
            _lastPollTime = time;

            _pollAttempts++;
            if (_pollAttempts > 60)
            {
                OmnisenseLogger.LogError($"[3D Model Generator] Task polling timed out after {_pollAttempts} attempts.", "3D_MODEL_GEN");
                EditorApplication.update -= PollTaskStatus;
                SetLoadingState(false);
                AddSystemErrorMessage(_pendingExplanation, "Task timed out during remote generation.");
                return;
            }

            string url = "";
            string apiKey = "";
            if (_pollingProvider == "Meshy AI")
            {
                url = $"https://api.meshy.ai/openapi/v2/text-to-3d/{_taskId}";
                apiKey = EditorPrefs.GetString("Omnisense_Meshy_Key", "");
            }
            else if (_pollingProvider == "Tripo3D")
            {
                url = $"https://openapi.tripo3d.ai/v3/tasks/{_taskId}";
                apiKey = EditorPrefs.GetString("Omnisense_Tripo3D_Key", "");
            }

            OmnisenseLogger.Log($"[3D Model Generator] Polling {_pollingProvider} (Attempt {_pollAttempts}). URL: {url}", "3D_MODEL_GEN");

            var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            var operation = req.SendWebRequest();
            operation.completed += (op) =>
            {
                if (req.result != UnityWebRequest.Result.Success)
                {
                    OmnisenseLogger.LogWarning($"[3D Model Generator] Polling request error. Status: {req.responseCode}, Error: {req.error}", "3D_MODEL_GEN");
                    req.Dispose();
                    return;
                }

                string json = req.downloadHandler.text;
                req.Dispose();

                if (_pollingProvider == "Meshy AI")
                {
                    var statusMatch = Regex.Match(json, @"""status""\s*:\s*""([^""]+)""");
                    var progressMatch = Regex.Match(json, @"""progress""\s*:\s*(\d+)");
                    if (statusMatch.Success)
                    {
                        string status = statusMatch.Groups[1].Value;
                        string progress = progressMatch.Success ? progressMatch.Groups[1].Value : "0";
                        OmnisenseLogger.Log($"[3D Model Generator] Meshy AI Task state: {status}, Progress: {progress}%", "3D_MODEL_GEN");
                        ShowStatus($"Generating: {progress}% ({status})... (Attempt {_pollAttempts})");

                        if (status == "SUCCEEDED")
                        {
                            EditorApplication.update -= PollTaskStatus;
                            var glbMatch = Regex.Match(json, @"""glb""\s*:\s*""([^""]+)""");
                            if (glbMatch.Success)
                            {
                                string glbUrl = glbMatch.Groups[1].Value.Replace("\\/", "/");
                                OmnisenseLogger.Log($"[3D Model Generator] Meshy AI task completed. GLB URL: {glbUrl}", "3D_MODEL_GEN");
                                DownloadModelBytes(glbUrl, "glb");
                            }
                            else
                            {
                                OmnisenseLogger.LogError($"[3D Model Generator] Completed Meshy response is missing glb URL: {json}", "3D_MODEL_GEN");
                                SetLoadingState(false);
                                AddSystemErrorMessage(_pendingExplanation, "Failed to find GLB file URL in completed task response.");
                            }
                        }
                        else if (status == "FAILED")
                        {
                            OmnisenseLogger.LogError($"[3D Model Generator] Meshy AI task failed on server side. Response JSON: {json}", "3D_MODEL_GEN");
                            EditorApplication.update -= PollTaskStatus;
                            SetLoadingState(false);
                            AddSystemErrorMessage(_pendingExplanation, "Generation failed on Meshy AI server.");
                        }
                    }
                }
                else if (_pollingProvider == "Tripo3D")
                {
                    int dataIndex = json.IndexOf("\"data\"");
                    if (dataIndex >= 0)
                    {
                        var statusMatch = Regex.Match(json.Substring(dataIndex), @"""status""\s*:\s*""([^""]+)""");
                        if (statusMatch.Success)
                        {
                            string status = statusMatch.Groups[1].Value;
                            OmnisenseLogger.Log($"[3D Model Generator] Tripo3D Task state: {status}", "3D_MODEL_GEN");
                            ShowStatus($"Generating: {status}... (Attempt {_pollAttempts})");

                            if (status == "success")
                            {
                                EditorApplication.update -= PollTaskStatus;
                                var modelMatch = Regex.Match(json, @"""model_url""\s*:\s*""([^""]+)""");
                                if (modelMatch.Success)
                                {
                                    string modelUrl = modelMatch.Groups[1].Value.Replace("\\/", "/");
                                    string pathOnly = modelUrl;
                                    int qMark = modelUrl.IndexOf('?');
                                    if (qMark != -1) pathOnly = modelUrl.Substring(0, qMark);

                                    string fmt = "glb";
                                    if (pathOnly.EndsWith(".splat", StringComparison.OrdinalIgnoreCase)) fmt = "splat";
                                    else if (pathOnly.EndsWith(".ply", StringComparison.OrdinalIgnoreCase)) fmt = "ply";

                                    OmnisenseLogger.Log($"[3D Model Generator] Tripo3D task completed. URL: {modelUrl}, Format: {fmt}", "3D_MODEL_GEN");
                                    DownloadModelBytes(modelUrl, fmt);
                                }
                                else
                                {
                                    OmnisenseLogger.LogError($"[3D Model Generator] Completed Tripo3D response is missing model_url link: {json}", "3D_MODEL_GEN");
                                    SetLoadingState(false);
                                    AddSystemErrorMessage(_pendingExplanation, "Failed to find model_url in completed task response.");
                                }
                            }
                            else if (status == "failed")
                            {
                                OmnisenseLogger.LogError($"[3D Model Generator] Tripo3D task failed on server side. Response JSON: {json}", "3D_MODEL_GEN");
                                EditorApplication.update -= PollTaskStatus;
                                SetLoadingState(false);
                                AddSystemErrorMessage(_pendingExplanation, "Generation failed on Tripo3D server.");
                            }
                        }
                    }
                    else
                    {
                        OmnisenseLogger.LogError($"[3D Model Generator] Tripo3D polling response lacks 'data' property: {json}", "3D_MODEL_GEN");
                    }
                }
            };
        }

        private void DownloadModelBytes(string url, string format)
        {
            ShowStatus("Downloading model file...");
            OmnisenseLogger.Log($"[3D Model Generator] Downloading model file from URL: {url}", "3D_MODEL_GEN");
            var req = UnityWebRequest.Get(url);
            var op = req.SendWebRequest();
            op.completed += (o) =>
            {
                SetLoadingState(false);
                if (req.result == UnityWebRequest.Result.Success)
                {
                    byte[] bytes = req.downloadHandler.data;
                    OmnisenseLogger.Log($"[3D Model Generator] Downloaded model payload successfully. Size: {bytes.Length} bytes.", "3D_MODEL_GEN");
                    SaveAndImportModel(bytes, format);
                }
                else
                {
                    OmnisenseLogger.LogError($"[3D Model Generator] Model download failed. Error: {req.error}, Status Code: {req.responseCode}", "3D_MODEL_GEN");
                    AddSystemErrorMessage(_pendingExplanation, $"Failed to download model: {req.error}");
                }
                req.Dispose();
            };
        }

        private void SaveAndImportModel(byte[] bytes, string format)
        {
            string rawPath = _pathField.value.Trim();
            if (string.IsNullOrEmpty(rawPath)) rawPath = "Assets/";

            string targetDir = rawPath;
            string finalPath = rawPath;

            if (rawPath.EndsWith(".glb") || rawPath.EndsWith(".gltf") || rawPath.EndsWith(".splat") || rawPath.EndsWith(".ply"))
            {
                targetDir = Path.GetDirectoryName(rawPath);
            }
            else
            {
                if (!targetDir.EndsWith("/") && !targetDir.EndsWith("\\"))
                {
                    targetDir += "/";
                }
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                finalPath = targetDir + "model_" + timestamp + "." + format;
            }

            try
            {
                string absoluteDir = targetDir;
                if (targetDir.StartsWith("Assets"))
                {
                    absoluteDir = Path.Combine(Application.dataPath, "..", targetDir);
                }

                if (!Directory.Exists(absoluteDir))
                {
                    Directory.CreateDirectory(absoluteDir);
                }

                string ext = Path.GetExtension(finalPath);
                if (string.IsNullOrEmpty(ext)) ext = "." + format;
                string baseName = Path.GetFileNameWithoutExtension(finalPath);
                string absoluteFilePath = Path.Combine(absoluteDir, baseName + ext);

                int counter = 1;
                while (File.Exists(absoluteFilePath))
                {
                    absoluteFilePath = Path.Combine(absoluteDir, $"{baseName}_{counter}{ext}");
                    finalPath = Path.Combine(targetDir, $"{baseName}_{counter}{ext}").Replace("\\", "/");
                    counter++;
                }

                File.WriteAllBytes(absoluteFilePath, bytes);
                AssetDatabase.ImportAsset(finalPath);
                AssetDatabase.Refresh();
                OmnisenseLogger.Log($"[3D Model Generator] Successfully saved and imported generated model file at: {finalPath}", "3D_MODEL_GEN");

                var assistantMsg = new ModelChatMessage {
                    sender = "assistant",
                    content = _pendingExplanation,
                    generatedModelPath = finalPath,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };
                _activeSession.messages.Add(assistantMsg);
                OmnisenseModelSessionManager.SaveSession(_activeSession);

                ShowSuccess($"3D model saved and imported successfully at:\n{finalPath}");
                RenderActiveSessionMessages();
            }
            catch (Exception ex)
            {
                OmnisenseLogger.LogError($"[3D Model Generator] Failed to write/import generated model file. Path: {finalPath}, Error: {ex.Message}", "3D_MODEL_GEN");
                AddSystemErrorMessage(_pendingExplanation, $"Failed to save model file: {ex.Message}");
            }
        }

        private void AddSystemErrorMessage(string explanation, string error)
        {
            var assistantMsg = new ModelChatMessage {
                sender = "assistant",
                content = $"{explanation}\n\n[System Notice: {error}]",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            _activeSession.messages.Add(assistantMsg);
            OmnisenseModelSessionManager.SaveSession(_activeSession);
            RenderActiveSessionMessages();
            ShowError(error);
        }

        private void OnConvertClicked()
        {
            string jsPath = _jsFileField.value.Trim();
            if (string.IsNullOrEmpty(jsPath) || !File.Exists(jsPath.StartsWith("Assets") ? Path.Combine(Application.dataPath, "..", jsPath) : jsPath))
            {
                ShowError("Please select a valid Three.js JavaScript file.");
                return;
            }

            string targetDir = _pathField.value.Trim();
            if (string.IsNullOrEmpty(targetDir)) targetDir = "Assets/";

            if (targetDir.EndsWith(".glb") || targetDir.EndsWith(".gltf"))
            {
                targetDir = Path.GetDirectoryName(targetDir);
            }
            if (!targetDir.EndsWith("/") && !targetDir.EndsWith("\\"))
            {
                targetDir += "/";
            }

            string filename = Path.GetFileNameWithoutExtension(jsPath);
            string gltfPath = targetDir + filename + ".gltf";

            string absoluteDir = targetDir;
            if (targetDir.StartsWith("Assets"))
            {
                absoluteDir = Path.Combine(Application.dataPath, "..", targetDir);
            }

            if (!Directory.Exists(absoluteDir))
            {
                Directory.CreateDirectory(absoluteDir);
            }

            string ext = ".gltf";
            string baseName = filename;
            string absoluteGltfPath = Path.Combine(absoluteDir, baseName + ext);

            int counter = 1;
            while (File.Exists(absoluteGltfPath))
            {
                absoluteGltfPath = Path.Combine(absoluteDir, $"{baseName}_{counter}{ext}");
                gltfPath = Path.Combine(targetDir, $"{baseName}_{counter}{ext}").Replace("\\", "/");
                counter++;
            }

            SetLoadingState(true);
            _convertBtn.SetEnabled(false);
            ShowStatus("Converting Three.js model to glTF...");

            EnsureNodeDependencies();

            string absoluteJsPath = jsPath.StartsWith("Assets") ? Path.Combine(Application.dataPath, "..", jsPath) : jsPath;

            ConvertThreeJsToGltf(absoluteJsPath, absoluteGltfPath, (success, result) => {
                SetLoadingState(false);
                _convertBtn.SetEnabled(true);
                if (success)
                {
                    AssetDatabase.ImportAsset(gltfPath);
                    AssetDatabase.Refresh();
                    ShowSuccess($"Model successfully converted and imported at:\n{gltfPath}");
                }
                else
                {
                    ShowError($"Conversion Failed:\n{result}");
                }
            });
        }

        private void EnsureNodeDependencies()
        {
            string helperDir = Path.Combine(Directory.GetCurrentDirectory(), "UserSettings", "Omnisense_3D_Helpers");
            if (!Directory.Exists(helperDir))
            {
                Directory.CreateDirectory(helperDir);
            }

            string packageJsonPath = Path.Combine(helperDir, "package.json");
            bool writePackageJson = !File.Exists(packageJsonPath);
            if (!writePackageJson)
            {
                try
                {
                    string existingContent = File.ReadAllText(packageJsonPath);
                    if (!existingContent.Contains("\"type\": \"module\""))
                    {
                        writePackageJson = true;
                    }
                }
                catch
                {
                    writePackageJson = true;
                }
            }

            if (writePackageJson)
            {
                string packageJsonContent = @"{" +
                    "\"name\": \"omnisense-3d-helpers\"," +
                    "\"version\": \"1.0.0\"," +
                    "\"description\": \"Three.js to glTF exporter helper\"," +
                    "\"main\": \"three2gltf.js\"," +
                    "\"type\": \"module\"," +
                    "\"dependencies\": {" +
                    "\"three\": \"^0.160.0\"" +
                    "}" +
                    "}";
                File.WriteAllText(packageJsonPath, packageJsonContent);
            }

            string jsHelperPath = Path.Combine(helperDir, "three2gltf.js");
            bool writeJsHelper = !File.Exists(jsHelperPath);
            if (!writeJsHelper)
            {
                try
                {
                    string existingContent = File.ReadAllText(jsHelperPath);
                    if (!existingContent.Contains("MockFileReader"))
                    {
                        writeJsHelper = true;
                    }
                }
                catch
                {
                    writeJsHelper = true;
                }
            }

            if (writeJsHelper)
            {
                string jsContent = @"import * as THREE from 'three';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import { Blob } from 'buffer';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

if (!global.Blob) {
    global.Blob = Blob;
}

class MockFileReader {
    constructor() {
        this.onloadend = null;
        this.result = null;
    }

    readAsDataURL(blob) {
        blob.arrayBuffer().then(arrayBuffer => {
            const buffer = Buffer.from(arrayBuffer);
            const base64 = buffer.toString('base64');
            this.result = `data:${blob.type || 'application/octet-stream'};base64,${base64}`;
            if (typeof this.onloadend === 'function') {
                process.nextTick(() => this.onloadend());
            }
        }).catch(err => {
            console.error(""FileReader mock error:"", err);
        });
    }

    readAsArrayBuffer(blob) {
        blob.arrayBuffer().then(arrayBuffer => {
            this.result = arrayBuffer;
            if (typeof this.onloadend === 'function') {
                process.nextTick(() => this.onloadend());
            }
        }).catch(err => {
            console.error(""FileReader mock error:"", err);
        });
    }
}

global.window = global;
global.self = global;
global.FileReader = MockFileReader;
global.document = {
    createElement: function() { return {}; }
};

import { GLTFExporter } from 'three/examples/jsm/exporters/GLTFExporter.js';

const userScriptPath = path.resolve(process.argv[2]);
const outputPath = path.resolve(process.argv[3]);

if (!fs.existsSync(userScriptPath)) {
    console.error('User script not found: ' + userScriptPath);
    process.exit(1);
}

const userCode = fs.readFileSync(userScriptPath, 'utf8');

const scene = new THREE.Scene();

try {
    const runUserCode = new Function('THREE', 'scene', userCode);
    runUserCode(THREE, scene);

    const exporter = new GLTFExporter();
    exporter.parse(scene, function(gltf) {
        fs.writeFileSync(outputPath, JSON.stringify(gltf, null, 2));
        console.log('SUCCESS');
        process.exit(0);
    }, function(err) {
        console.error('Export error: ', err);
        process.exit(1);
    }, { binary: false });
} catch (ex) {
    console.error('Error executing user JS code: ', ex.message);
    process.exit(1);
}
";
                File.WriteAllText(jsHelperPath, jsContent);
            }

            string nodeModulesPath = Path.Combine(helperDir, "node_modules", "three");
            if (!Directory.Exists(nodeModulesPath))
            {
                Debug.Log("[Omnisense-3D] Installing Node dependencies in UserSettings/Omnisense_3D_Helpers...");
                RunNpmInstall(helperDir);
            }
        }

        private void RunNpmInstall(string workingDir)
        {
            try
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c npm install",
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var process = new System.Diagnostics.Process { StartInfo = processInfo };
                process.EnableRaisingEvents = true;
                process.Exited += (sender, args) =>
                {
                    if (process.ExitCode == 0)
                    {
                        Debug.Log("[Omnisense-3D] Node dependencies installed successfully.");
                    }
                    else
                    {
                        string err = process.StandardError.ReadToEnd();
                        Debug.LogError($"[Omnisense-3D] Failed to install node dependencies: {err}");
                    }
                    process.Dispose();
                };
                process.Start();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Omnisense-3D] Error launching npm install: {ex.Message}");
            }
        }

        private void ConvertThreeJsToGltf(string jsFilePath, string gltfOutputPath, Action<bool, string> onComplete)
        {
            try
            {
                string helperDir = Path.Combine(Directory.GetCurrentDirectory(), "UserSettings", "Omnisense_3D_Helpers");
                string jsHelperPath = Path.Combine(helperDir, "three2gltf.js");

                OmnisenseLogger.Log($"[3D Model Generator] Launching Node process to convert Three.js script to glTF. Helper script: {jsHelperPath}", "3D_MODEL_GEN");

                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = $"\"{jsHelperPath}\" \"{jsFilePath}\" \"{gltfOutputPath}\"",
                    WorkingDirectory = helperDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var process = new System.Diagnostics.Process { StartInfo = processInfo };
                process.EnableRaisingEvents = true;
                process.Exited += (sender, args) =>
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    bool success = process.ExitCode == 0 && output.Contains("SUCCESS");

                    EditorApplication.delayCall += () =>
                    {
                        if (success)
                        {
                            OmnisenseLogger.Log($"[3D Model Generator] Three.js to glTF conversion succeeded. Saved output to: {gltfOutputPath}", "3D_MODEL_GEN");
                            onComplete?.Invoke(true, gltfOutputPath);
                        }
                        else
                        {
                            string detail = string.IsNullOrEmpty(error) ? output : error;
                            OmnisenseLogger.LogError($"[3D Model Generator] Three.js to glTF conversion failed. Exit Code: {process.ExitCode}, Output: {output}, Error: {error}", "3D_MODEL_GEN");
                            onComplete?.Invoke(false, detail);
                        }
                    };
                    process.Dispose();
                };
                process.Start();
            }
            catch (Exception ex)
            {
                OmnisenseLogger.LogError($"[3D Model Generator] Exception during Three.js to glTF conversion: {ex.Message}", "3D_MODEL_GEN");
                onComplete?.Invoke(false, ex.Message);
            }
        }

        private string GetApiKey(string model)
        {
            if (model.Contains("gpt") || model.Contains("o3")) return EditorPrefs.GetString("Omnisense_OpenAI_Key", "");
            if (model.Contains("claude")) return EditorPrefs.GetString("Omnisense_Anthropic_Key", "");
            if (model.Contains("gemini")) return EditorPrefs.GetString("Omnisense_Gemini_Key", "");
            if (model.Contains("grok")) return EditorPrefs.GetString("Omnisense_Grok_Key", "");
            if (model.Contains("deepseek")) return EditorPrefs.GetString("Omnisense_DeepSeek_Key", "");
            if (model.Contains("qwen")) return EditorPrefs.GetString("Omnisense_Qwen_Key", "");
            if (model.Contains("glm")) return EditorPrefs.GetString("Omnisense_GLM_Key", "");
            if (model.Contains("kimi")) return EditorPrefs.GetString("Omnisense_Kimi_Key", "");
            if (model == "self-hosted") return EditorPrefs.GetString("Omnisense_SelfHosted_Key", "");
            return "";
        }

        private int GetMaxTokens(string model)
        {
            if (model.Contains("gpt") || model.Contains("o3")) return EditorPrefs.GetInt("Omnisense_OpenAI_MaxTokens", 4096);
            if (model.Contains("claude")) return EditorPrefs.GetInt("Omnisense_Anthropic_MaxTokens", 4096);
            if (model.Contains("gemini")) return EditorPrefs.GetInt("Omnisense_Gemini_MaxTokens", 4096);
            if (model.Contains("grok")) return EditorPrefs.GetInt("Omnisense_Grok_MaxTokens", 4096);
            if (model.Contains("deepseek")) return EditorPrefs.GetInt("Omnisense_DeepSeek_MaxTokens", 4096);
            if (model.Contains("qwen")) return EditorPrefs.GetInt("Omnisense_Qwen_MaxTokens", 4096);
            if (model.Contains("glm")) return EditorPrefs.GetInt("Omnisense_GLM_MaxTokens", 4096);
            if (model.Contains("kimi")) return EditorPrefs.GetInt("Omnisense_Kimi_MaxTokens", 4096);
            if (model == "self-hosted") return EditorPrefs.GetInt("Omnisense_SelfHosted_MaxTokens", 4096);
            return 4096;
        }

        private void ShowStatus(string msg)
        {
            _statusLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _statusLabel.text = msg;
        }

        private void ShowError(string msg)
        {
            _statusLabel.style.color = new StyleColor(new Color(1f, 0.3f, 0.3f));
            _statusLabel.text = msg;
            Debug.LogError("[Omnisense-3D] " + msg);
        }

        private void ShowSuccess(string msg)
        {
            _statusLabel.style.color = new StyleColor(new Color(0.3f, 0.9f, 0.3f));
            _statusLabel.text = msg;
            Debug.Log("[Omnisense-3D] " + msg);
        }

        private void SelectSelfHostedModel(string modelName)
        {
            EditorPrefs.SetString("Omnisense_SelfHosted_Model", modelName);
            _modelSelector.value = "self-hosted";
        }

        private void UpdateModelSelectorOptions(string provider)
        {
            if (_modelSelector == null) return;

            var container = _modelSelector.parent;
            if (container != null)
            {
                var label = container.Q<Label>();
                if (label != null)
                {
                    if (provider == "Three.js Code Generator") label.text = "LLM Model:";
                    else if (provider == "Meshy AI") label.text = "Meshy Style:";
                    else if (provider == "Tripo3D") label.text = "Tripo Mode:";
                }
            }

            if (provider == "Three.js Code Generator")
            {
                _modelSelector.choices = new List<string> {
                    "gpt-5.5-thinking", "gpt-5.5-pro", "gpt-5.5", "gpt-5.5-instant",
                    "gpt-5.4", "gpt-5.4-mini", "gpt-5.4-nano", "o3-mini",
                    "claude-fable-5", "claude-mythos-5", "claude-opus-4.8", "claude-sonnet-4.6", "claude-haiku-4.5",
                    "claude-4.7-opus", "claude-4.6-sonnet", "claude-4.5-haiku",
                    "gemini-3.1-pro", "gemini-3.5-flash", "gemini-3-flash", "gemini-3.1-flash", "gemini-3.1-flash-lite",
                    "gemini-2.5-pro", "gemini-2.5-flash", "gemini-2.5-flash-lite",
                    "grok-4.3", "grok-build-0.1", "grok-latest",
                    "grok-4.3-beta", "grok-4.20-beta-2", "grok-4.20-fast",
                    "deepseek-chat", "deepseek-reasoner", "qwen-2.5-coder", "qwen-2.5-instruct", "glm-4", "kimi-k2",
                    "self-hosted"
                };
                string savedModel = EditorPrefs.GetString("Omnisense_ModelGen_LLMModel", "gpt-5.4-mini");
                if (_modelSelector.choices.Contains(savedModel)) _modelSelector.value = savedModel;
                else _modelSelector.value = "gpt-5.4-mini";
            }
            else if (provider == "Meshy AI")
            {
                _modelSelector.choices = new List<string> {
                    "realistic", "cartoon", "sculpture", "voxel", "poly"
                };
                string savedStyle = EditorPrefs.GetString("Omnisense_ModelGen_MeshyStyle", "realistic");
                if (_modelSelector.choices.Contains(savedStyle)) _modelSelector.value = savedStyle;
                else _modelSelector.value = "realistic";
            }
            else if (provider == "Tripo3D")
            {
                _modelSelector.choices = new List<string> {
                    "Standard Text to Model",
                    "P Text to Model",
                    "Standard Image to Model",
                    "P Image to Model",
                    "Standard Multiview to Model",
                    "P Multiview to Model",
                    "Image to Gaussian Splat"
                };
                string savedVersion = EditorPrefs.GetString("Omnisense_ModelGen_TripoVersion", "Standard Text to Model");
                if (_modelSelector.choices.Contains(savedVersion)) _modelSelector.value = savedVersion;
                else _modelSelector.value = "Standard Text to Model";
            }

            UpdateTripoVersionSelector(_modelSelector.value);
        }

        private void UpdateTripoVersionSelector(string tripoMode)
        {
            if (_tripoVersionRow == null || _tripoVersionSelector == null) return;

            if (_providerField.value != "Tripo3D")
            {
                _tripoVersionRow.style.display = DisplayStyle.None;
                return;
            }

            if (tripoMode == "Image to Gaussian Splat")
            {
                _tripoVersionRow.style.display = DisplayStyle.None;
                return;
            }

            _tripoVersionRow.style.display = DisplayStyle.Flex;

            if (tripoMode == "Standard Text to Model" || tripoMode == "Standard Image to Model" || tripoMode == "Standard Multiview to Model")
            {
                _tripoVersionSelector.choices = new List<string> { "v3.1", "v3.0", "v2.5", "v2.0", "v1.0" };
                string savedSpecific = EditorPrefs.GetString("Omnisense_ModelGen_TripoSpecificVersion", "v3.1");
                if (_tripoVersionSelector.choices.Contains(savedSpecific)) _tripoVersionSelector.value = savedSpecific;
                else _tripoVersionSelector.value = "v3.1";
            }
            else if (tripoMode == "P Text to Model" || tripoMode == "P Image to Model" || tripoMode == "P Multiview to Model")
            {
                _tripoVersionSelector.choices = new List<string> { "P1" };
                _tripoVersionSelector.value = "P1";
            }
        }

        private string GetMappedTripoVersion(string selectedVersion)
        {
            if (selectedVersion == "v3.1") return "v3.1-20260211";
            if (selectedVersion == "v3.0") return "v3.0-20250812";
            if (selectedVersion == "v2.5") return "v2.5-20250123";
            if (selectedVersion == "v2.0") return "v2.0-20240919";
            if (selectedVersion == "v1.0") return "v1.0-20240417";
            if (selectedVersion == "P1") return "P1-20260311";
            return "v3.1-20260211";
        }

        private void ShowModelMenu()
        {
            var menu = new GenericMenu();
            string currentModel = _modelSelector.value;

            // OpenAI
            menu.AddItem(new GUIContent("open ai/gpt-5.5-thinking"), currentModel == "gpt-5.5-thinking", () => _modelSelector.value = "gpt-5.5-thinking");
            menu.AddItem(new GUIContent("open ai/gpt-5.5-pro"), currentModel == "gpt-5.5-pro", () => _modelSelector.value = "gpt-5.5-pro");
            menu.AddItem(new GUIContent("open ai/gpt-5.5"), currentModel == "gpt-5.5", () => _modelSelector.value = "gpt-5.5");
            menu.AddItem(new GUIContent("open ai/gpt-5.5-instant"), currentModel == "gpt-5.5-instant", () => _modelSelector.value = "gpt-5.5-instant");
            menu.AddItem(new GUIContent("open ai/gpt-5.4"), currentModel == "gpt-5.4", () => _modelSelector.value = "gpt-5.4");
            menu.AddItem(new GUIContent("open ai/gpt-5.4-mini"), currentModel == "gpt-5.4-mini", () => _modelSelector.value = "gpt-5.4-mini");
            menu.AddItem(new GUIContent("open ai/gpt-5.4-nano"), currentModel == "gpt-5.4-nano", () => _modelSelector.value = "gpt-5.4-nano");
            menu.AddItem(new GUIContent("open ai/o3-mini"), currentModel == "o3-mini", () => _modelSelector.value = "o3-mini");

            // Claude
            menu.AddItem(new GUIContent("claude/claude-fable-5"), currentModel == "claude-fable-5", () => _modelSelector.value = "claude-fable-5");
            menu.AddItem(new GUIContent("claude/claude-mythos-5"), currentModel == "claude-mythos-5", () => _modelSelector.value = "claude-mythos-5");
            menu.AddItem(new GUIContent("claude/claude-opus-4.8"), currentModel == "claude-opus-4.8", () => _modelSelector.value = "claude-opus-4.8");
            menu.AddItem(new GUIContent("claude/claude-sonnet-4.6"), currentModel == "claude-sonnet-4.6", () => _modelSelector.value = "claude-sonnet-4.6");
            menu.AddItem(new GUIContent("claude/claude-haiku-4.5"), currentModel == "claude-haiku-4.5", () => _modelSelector.value = "claude-haiku-4.5");
            menu.AddItem(new GUIContent("claude/claude-4.7-opus"), currentModel == "claude-4.7-opus", () => _modelSelector.value = "claude-4.7-opus");
            menu.AddItem(new GUIContent("claude/claude-4.6-sonnet"), currentModel == "claude-4.6-sonnet", () => _modelSelector.value = "claude-4.6-sonnet");
            menu.AddItem(new GUIContent("claude/claude-4.5-haiku"), currentModel == "claude-4.5-haiku", () => _modelSelector.value = "claude-4.5-haiku");

            // Gemini
            menu.AddItem(new GUIContent("gemini/gemini-3.1-pro"), currentModel == "gemini-3.1-pro", () => _modelSelector.value = "gemini-3.1-pro");
            menu.AddItem(new GUIContent("gemini/gemini-3.5-flash"), currentModel == "gemini-3.5-flash", () => _modelSelector.value = "gemini-3.5-flash");
            menu.AddItem(new GUIContent("gemini/gemini-3-flash"), currentModel == "gemini-3-flash", () => _modelSelector.value = "gemini-3-flash");
            menu.AddItem(new GUIContent("gemini/gemini-3.1-flash"), currentModel == "gemini-3.1-flash", () => _modelSelector.value = "gemini-3.1-flash");
            menu.AddItem(new GUIContent("gemini/gemini-3.1-flash-lite"), currentModel == "gemini-3.1-flash-lite", () => _modelSelector.value = "gemini-3.1-flash-lite");
            menu.AddItem(new GUIContent("gemini/gemini-2.5-pro"), currentModel == "gemini-2.5-pro", () => _modelSelector.value = "gemini-2.5-pro");
            menu.AddItem(new GUIContent("gemini/gemini-2.5-flash"), currentModel == "gemini-2.5-flash", () => _modelSelector.value = "gemini-2.5-flash");
            menu.AddItem(new GUIContent("gemini/gemini-2.5-flash-lite"), currentModel == "gemini-2.5-flash-lite", () => _modelSelector.value = "gemini-2.5-flash-lite");

            // Grok
            menu.AddItem(new GUIContent("grok/grok-4.3"), currentModel == "grok-4.3", () => _modelSelector.value = "grok-4.3");
            menu.AddItem(new GUIContent("grok/grok-build-0.1"), currentModel == "grok-build-0.1", () => _modelSelector.value = "grok-build-0.1");
            menu.AddItem(new GUIContent("grok/grok-latest"), currentModel == "grok-latest", () => _modelSelector.value = "grok-latest");
            menu.AddItem(new GUIContent("grok/grok-4.3-beta"), currentModel == "grok-4.3-beta", () => _modelSelector.value = "grok-4.3-beta");
            menu.AddItem(new GUIContent("grok/grok-4.20-beta-2"), currentModel == "grok-4.20-beta-2", () => _modelSelector.value = "grok-4.20-beta-2");
            menu.AddItem(new GUIContent("grok/grok-4.20-fast"), currentModel == "grok-4.20-fast", () => _modelSelector.value = "grok-4.20-fast");

            // Self Hosted
            string selfHostedModel = EditorPrefs.GetString("Omnisense_SelfHosted_Model", "llama3:8b");
            menu.AddItem(new GUIContent($"self hosted/{selfHostedModel} (Configured)"), currentModel == "self-hosted", () => SelectSelfHostedModel(selfHostedModel));
            menu.AddSeparator("self hosted/");
            menu.AddItem(new GUIContent("self hosted/llama3:8b"), false, () => SelectSelfHostedModel("llama3:8b"));
            menu.AddItem(new GUIContent("self hosted/llama3.1:8b"), false, () => SelectSelfHostedModel("llama3.1:8b"));
            menu.AddItem(new GUIContent("self hosted/mistral:7b"), false, () => SelectSelfHostedModel("mistral:7b"));
            menu.AddItem(new GUIContent("self hosted/phi3:medium"), false, () => SelectSelfHostedModel("phi3:medium"));
            menu.AddItem(new GUIContent("self hosted/qwen2.5:7b"), false, () => SelectSelfHostedModel("qwen2.5:7b"));
            menu.AddItem(new GUIContent("self hosted/gemma2:9b"), false, () => SelectSelfHostedModel("gemma2:9b"));

            // Other
            menu.AddItem(new GUIContent("other/deepseek-chat"), currentModel == "deepseek-chat", () => _modelSelector.value = "deepseek-chat");
            menu.AddItem(new GUIContent("other/deepseek-reasoner"), currentModel == "deepseek-reasoner", () => _modelSelector.value = "deepseek-reasoner");
            menu.AddItem(new GUIContent("other/qwen-2.5-coder"), currentModel == "qwen-2.5-coder", () => _modelSelector.value = "qwen-2.5-coder");
            menu.AddItem(new GUIContent("other/qwen-2.5-instruct"), currentModel == "qwen-2.5-instruct", () => _modelSelector.value = "qwen-2.5-instruct");
            menu.AddItem(new GUIContent("other/glm-4"), currentModel == "glm-4", () => _modelSelector.value = "glm-4");
            menu.AddItem(new GUIContent("other/kimi-k2"), currentModel == "kimi-k2", () => _modelSelector.value = "kimi-k2");
            menu.AddItem(new GUIContent("other/self-hosted"), currentModel == "self-hosted", () => _modelSelector.value = "self-hosted");

            menu.DropDown(_modelSelector.worldBound);
        }

        private void SetLoadingState(bool loading)
        {
            _generateBtn.SetEnabled(!loading);
            _promptField.SetEnabled(!loading);
            _providerField.SetEnabled(!loading);
            _modelSelector.SetEnabled(!loading);
            _pathField.SetEnabled(!loading);
            if (_convertBtn != null) _convertBtn.SetEnabled(!loading);
            if (_attachBtn != null) _attachBtn.SetEnabled(!loading);
        }

        private string JsonEscape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private string TruncateString(string str, int maxLen)
        {
            if (string.IsNullOrEmpty(str)) return "";
            if (str.Length <= maxLen) return str;
            return str.Substring(0, maxLen - 3) + "...";
        }
    }

    [Serializable]
    public class ModelChatMessage
    {
        public string sender; // "user" or "assistant"
        public string content; // text explanation/prompts
        public string timestamp;
        public string referenceImageName;
        public string referenceImageBase64;
        public string generatedScriptPath; // path to the generated Three.js code (.js)
        public string generatedModelPath; // path to the generated glTF model (.gltf / .glb)
    }

    [Serializable]
    public class ModelChatSession
    {
        public string id;
        public string name;
        public List<ModelChatMessage> messages = new List<ModelChatMessage>();
        public string lastUpdated;
    }

    public static class OmnisenseModelSessionManager
    {
        private static readonly string HistoryPath;

        static OmnisenseModelSessionManager()
        {
            try
            {
                HistoryPath = Path.Combine(Application.dataPath, "..", "UserSettings", "OmnisenseModelHistory");
            }
            catch (Exception)
            {
                HistoryPath = Path.Combine(Directory.GetCurrentDirectory(), "UserSettings", "OmnisenseModelHistory");
            }
        }

        public static void SaveSession(ModelChatSession session)
        {
            if (!Directory.Exists(HistoryPath)) Directory.CreateDirectory(HistoryPath);

            session.lastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string json = JsonUtility.ToJson(session, true);
            File.WriteAllText(Path.Combine(HistoryPath, $"{session.id}.json"), json);
        }

        public static List<ModelChatSession> GetAllSessions()
        {
            var sessions = new List<ModelChatSession>();
            if (!Directory.Exists(HistoryPath)) return sessions;

            foreach (var file in Directory.GetFiles(HistoryPath, "*.json"))
            {
                try {
                    string json = File.ReadAllText(file);
                    sessions.Add(JsonUtility.FromJson<ModelChatSession>(json));
                } catch { /* Corrupt file */ }
            }

            sessions.Sort((a, b) => string.Compare(b.lastUpdated, a.lastUpdated)); // Newest first
            return sessions;
        }

        public static ModelChatSession GetSessionById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            string path = Path.Combine(HistoryPath, $"{id}.json");
            if (!File.Exists(path)) return null;

            try {
                string json = File.ReadAllText(path);
                return JsonUtility.FromJson<ModelChatSession>(json);
            } catch { return null; }
        }

        public static ModelChatSession CreateNewSession()
        {
            var session = new ModelChatSession {
                id = Guid.NewGuid().ToString(),
                name = $"Model Chat {DateTime.Now:MMM dd, HH:mm}",
                lastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            SaveSession(session);
            return session;
        }

        public static void DeleteSession(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            string path = Path.Combine(HistoryPath, $"{id}.json");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
