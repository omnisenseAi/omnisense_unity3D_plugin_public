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
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEngine.Networking;

namespace Omnisense
{
    /// <summary>
    /// CORE PHILOSOPHY & DESIGN DECISION:
    /// The ImageGenerationPopup serves as a persistent, multi-turn AI Image Generation Chat Workspace.
    /// 
    /// WHY:
    /// Standard single-shot image generators are stateless, which forces developers to start from scratch
    /// when iterating on textures or sprites. By structuring this as a Visual Chat Workspace:
    ///   1. Multi-turn Iteration: Developers can reference previous creations in natural language (e.g. "make that chest blue").
    ///   2. Multimodal Vision Context: Integrates dragging & dropping reference files from the project to guide style and content.
    ///   3. Isolated Workspace Database: Saves chats in image_generation_chat_history to prevent bloat of the text chat.
    /// </summary>
    public class ImageGenerationPopup : EditorWindow
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

        // Settings elements
        private DropdownField _styleField;
        private DropdownField _providerField;
        private DropdownField _llmModelField;
        private DropdownField _dimensionField;
        private TextField _pathField;
        private string _lastGeneratedPath = "";

        // Networking requests
        private UnityWebRequest _activeRequest;
        private UnityWebRequest _activeGenRequest;
        private double _requestStartTime;
        private string _pendingExplanation = "";

        // Chat session state
        private ImageChatSession _activeSession;
        private List<ImageChatSession> _sessions = new List<ImageChatSession>();

        [MenuItem("Window/Omnisense/Image Generator")]
        public static void Open()
        {
            var window = GetWindow<ImageGenerationPopup>(true, "🎨 AI Image Chat Workspace", true);
            window.minSize = new Vector2(650, 580);
            window.Show();
        }

        private void OnEnable()
        {
            BuildUI();
            LoadLastOrNewSession();
        }

        private void OnDisable()
        {
            EditorApplication.update -= CheckLlmOrchestratorProgress;
            EditorApplication.update -= CheckGenRequestProgress;
        }

        private void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.backgroundColor = new StyleColor(new Color(0.17f, 0.18f, 0.2f));
            root.style.flexDirection = FlexDirection.Row;

            // 1. LEFT SIDEBAR (Chat History Threads)
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

            var newChatBtn = new Button(CreateNewChat) { text = "➕ New Image Chat" };
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

            var historyLabel = new Label("Recent Image Chats:");
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

            // Chat title header
            var chatHeader = new Label("🎨 AI Image Chat Workspace");
            chatHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            chatHeader.style.fontSize = 15;
            chatHeader.style.color = new StyleColor(new Color(0.9f, 0.9f, 0.9f));
            chatHeader.style.marginBottom = 8;
            workspace.Add(chatHeader);

            // Chat viewport for rendering bubbles
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

            // Settings drawer (Foldout)
            var settingsFoldout = new Foldout();
            settingsFoldout.value = true;
            settingsFoldout.text = "⚙ Generation Parameters";
            settingsFoldout.style.flexShrink = 0;
            settingsFoldout.style.backgroundColor = new StyleColor(new Color(0.14f, 0.15f, 0.17f));
            settingsFoldout.style.paddingLeft = 6;
            settingsFoldout.style.paddingRight = 6;
            settingsFoldout.style.paddingTop = 6;
            settingsFoldout.style.paddingBottom = 6;
            settingsFoldout.style.marginBottom = 6;
            settingsFoldout.style.borderTopLeftRadius = 4;
            settingsFoldout.style.borderTopRightRadius = 4;
            settingsFoldout.style.borderBottomLeftRadius = 4;
            settingsFoldout.style.borderBottomRightRadius = 4;

            // Dropdowns (Style, Provider, LLM Model)
            var dropdownsRow = new VisualElement();
            dropdownsRow.style.flexDirection = FlexDirection.Row;
            dropdownsRow.style.justifyContent = Justify.SpaceBetween;
            dropdownsRow.style.marginBottom = 6;

            var styleContainer = new VisualElement { style = { width = Length.Percent(32) } };
            styleContainer.Add(new Label("Style Preset:") { style = { unityFontStyleAndWeight = FontStyle.Bold, color = new StyleColor(new Color(0.8f, 0.8f, 0.8f)), fontSize = 10 } });
            _styleField = new DropdownField { choices = new List<string> { "No Style", "Pixel Art", "Stylized", "2D Platformer", "Realistic / Photo", "Water Color", "Sci-Fi / Cyberpunk" }, value = "No Style" };
            styleContainer.Add(_styleField);
            dropdownsRow.Add(styleContainer);

            var providerContainer = new VisualElement { style = { width = Length.Percent(32) } };
            providerContainer.Add(new Label("AI Generator:") { style = { unityFontStyleAndWeight = FontStyle.Bold, color = new StyleColor(new Color(0.8f, 0.8f, 0.8f)), fontSize = 10 } });
            _providerField = new DropdownField { choices = new List<string> { "OpenAI Image", "Google Imagen" } };
            providerContainer.Add(_providerField);
            dropdownsRow.Add(providerContainer);

            var llmContainer = new VisualElement { style = { width = Length.Percent(32) } };
            llmContainer.Add(new Label("Prompt Orchestrator:") { style = { unityFontStyleAndWeight = FontStyle.Bold, color = new StyleColor(new Color(0.8f, 0.8f, 0.8f)), fontSize = 10 } });
            _llmModelField = new DropdownField {
                choices = new List<string> {
                    "None",
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
                }
            };
            string savedModel = EditorPrefs.GetString("Omnisense_SelectedModel", "gpt-5.4-mini");
            if (_llmModelField.choices.Contains(savedModel)) _llmModelField.value = savedModel;
            else _llmModelField.value = "gpt-5.4-mini";

            _llmModelField.RegisterValueChangedCallback(evt => {
                EditorPrefs.SetString("Omnisense_SelectedModel", evt.newValue);
            });

            _llmModelField.RegisterCallback<PointerDownEvent>(evt => {
                evt.StopPropagation();
                ShowModelMenu();
            }, TrickleDown.TrickleDown);

            _llmModelField.RegisterCallback<MouseDownEvent>(evt => {
                evt.StopPropagation();
                ShowModelMenu();
            }, TrickleDown.TrickleDown);

            llmContainer.Add(_llmModelField);
            dropdownsRow.Add(llmContainer);

            settingsFoldout.Add(dropdownsRow);

            // Size / Save Path Row
            var sizePathRow = new VisualElement();
            sizePathRow.style.flexDirection = FlexDirection.Row;
            sizePathRow.style.justifyContent = Justify.SpaceBetween;

            var sizeContainer = new VisualElement { style = { width = Length.Percent(32) } };
            sizeContainer.Add(new Label("Dimensions:") { style = { unityFontStyleAndWeight = FontStyle.Bold, color = new StyleColor(new Color(0.8f, 0.8f, 0.8f)), fontSize = 10 } });
            _dimensionField = new DropdownField();
            _dimensionField.RegisterValueChangedCallback(evt => {
                if (evt.newValue == null) return;
                string currentProvider = _providerField.value;
                if (currentProvider == "OpenAI Image")
                {
                    EditorPrefs.SetString("Omnisense_ImgGen_DimOpenAI", evt.newValue);
                }
                else
                {
                    EditorPrefs.SetString("Omnisense_ImgGen_DimImagen", evt.newValue);
                }
            });
            sizeContainer.Add(_dimensionField);
            sizePathRow.Add(sizeContainer);

            var pathContainer = new VisualElement { style = { width = Length.Percent(65) } };
            pathContainer.Add(new Label("Save Location:") { style = { unityFontStyleAndWeight = FontStyle.Bold, color = new StyleColor(new Color(0.8f, 0.8f, 0.8f)), fontSize = 10 } });
            var pathRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            _pathField = new TextField { value = PlayerPrefs.GetString("Omnisense_ImgGen_Path", "Assets/"), style = { flexGrow = 1 } };
            _pathField.RegisterValueChangedCallback(evt => {
                PlayerPrefs.SetString("Omnisense_ImgGen_Path", evt.newValue);
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
            sizePathRow.Add(pathContainer);

            settingsFoldout.Add(sizePathRow);

            // Register provider value callback and initialize selections
            _providerField.RegisterValueChangedCallback(evt => {
                EditorPrefs.SetString("Omnisense_ImgGen_Provider", evt.newValue);
                UpdateDimensionChoices(evt.newValue);
            });

            string savedProvider = EditorPrefs.GetString("Omnisense_ImgGen_Provider", "OpenAI Image");
            _providerField.value = savedProvider;
            UpdateDimensionChoices(savedProvider);

            workspace.Add(settingsFoldout);

            // Drag-and-drop reference image thumbnail overlay
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
            _attachmentLabel = new Label("attachment.png") { style = { flexGrow = 1, fontSize = 10, color = new StyleColor(new Color(0.8f, 0.8f, 0.8f)) } };
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

            // Status notification block
            _statusLabel = new Label("Ready to design assets...");
            _statusLabel.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.marginBottom = 4;
            _statusLabel.style.flexShrink = 0;
            workspace.Add(_statusLabel);

            // Bottom Chat/Prompt Input Row
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

            // Register Drag & Drop events globally on the workspace panel
            workspace.RegisterCallback<DragUpdatedEvent>(evt => {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            });
            workspace.RegisterCallback<DragPerformEvent>(OnDragPerform);
        }

        private void RemoveAttachment()
        {
            _selectedReferencePath = "";
            if (_attachmentContainer != null)
            {
                _attachmentContainer.style.display = DisplayStyle.None;
            }
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
            _sessions = OmnisenseImageSessionManager.GetAllSessions();
            string lastSessionId = EditorPrefs.GetString("Omnisense_ImgChat_ActiveSessionId", "");

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
                _activeSession = OmnisenseImageSessionManager.CreateNewSession();
            }

            EditorPrefs.SetString("Omnisense_ImgChat_ActiveSessionId", _activeSession.id);
            PopulateSessionsList();
            RenderActiveSessionMessages();
        }

        private void CreateNewChat()
        {
            _activeSession = OmnisenseImageSessionManager.CreateNewSession();
            EditorPrefs.SetString("Omnisense_ImgChat_ActiveSessionId", _activeSession.id);
            PopulateSessionsList();
            RenderActiveSessionMessages();
        }

        private void LoadSession(string id)
        {
            var match = OmnisenseImageSessionManager.GetSessionById(id);
            if (match != null)
            {
                _activeSession = match;
                EditorPrefs.SetString("Omnisense_ImgChat_ActiveSessionId", id);
                PopulateSessionsList();
                RenderActiveSessionMessages();
            }
        }

        private void DeleteSession(string id)
        {
            if (EditorUtility.DisplayDialog("Delete Image Chat", "Delete this image chat history permanently?", "Delete", "Cancel"))
            {
                OmnisenseImageSessionManager.DeleteSession(id);
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

            _sessions = OmnisenseImageSessionManager.GetAllSessions();
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

            // Deferred scroll to bottom to ensure elements layout completes first
            EditorApplication.delayCall += () => {
                if (_chatViewport != null)
                {
                    _chatViewport.scrollOffset = new Vector2(0, float.MaxValue);
                }
            };
        }

        private VisualElement CreateMessageBubble(ImageChatMessage msg)
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
                        var attachmentLabel = new Label($"📎 Reference: {msg.referenceImageName}");
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

                if (!string.IsNullOrEmpty(msg.generatedImagePath))
                {
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(msg.generatedImagePath);
                    if (texture != null)
                    {
                        var img = new Image { image = texture };
                        img.style.width = 200;
                        img.style.height = 200;
                        img.style.marginTop = 8;
                        img.style.marginBottom = 6;
                        bubble.Add(img);

                        var buttonRow = new VisualElement();
                        buttonRow.style.flexDirection = FlexDirection.Row;
                        buttonRow.style.marginTop = 4;

                        var editorBtn = new Button(() => OmnisenseImageEditorWindow.OpenWithAsset(msg.generatedImagePath)) { text = "✂ Open in Editor" };
                        editorBtn.style.fontSize = 10;
                        editorBtn.style.backgroundColor = new StyleColor(new Color(0.2f, 0.44f, 0.68f));
                        editorBtn.style.color = new StyleColor(Color.white);
                        editorBtn.style.marginRight = 5;

                        var selectBtn = new Button(() => {
                            var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(msg.generatedImagePath);
                            if (asset != null)
                            {
                                Selection.activeObject = asset;
                                EditorGUIUtility.PingObject(asset);
                            }
                        }) { text = "🔍 Select Asset" };
                        selectBtn.style.fontSize = 10;
                        selectBtn.style.backgroundColor = new StyleColor(new Color(0.25f, 0.27f, 0.3f));
                        selectBtn.style.color = new StyleColor(Color.white);

                        buttonRow.Add(editorBtn);
                        buttonRow.Add(selectBtn);
                        bubble.Add(buttonRow);
                    }
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

        private void OnSendClicked()
        {
            string promptText = _promptField.value.Trim();
            OmnisenseLogger.Log($"[Image Generator] Send button clicked. Prompt: \"{promptText}\", reference art: \"{_selectedReferencePath}\"", "IMAGE_GEN");

            if (string.IsNullOrEmpty(promptText) && string.IsNullOrEmpty(_selectedReferencePath))
            {
                return;
            }

            if (_activeSession == null)
            {
                _activeSession = OmnisenseImageSessionManager.CreateNewSession();
            }

            var userMsg = new ImageChatMessage {
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

            if (_activeSession.name.StartsWith("Image Chat ") && !string.IsNullOrEmpty(promptText))
            {
                _activeSession.name = TruncateString(promptText, 25);
            }

            OmnisenseImageSessionManager.SaveSession(_activeSession);

            _promptField.value = "";
            string attachedPath = _selectedReferencePath;
            RemoveAttachment();

            PopulateSessionsList();
            RenderActiveSessionMessages();

            if (_llmModelField.value == "None")
            {
                if (!string.IsNullOrEmpty(attachedPath))
                {
                    OmnisenseLogger.LogWarning("[Image Generator] Reference image attached but Orchestrator is set to None. Reference image content will not be processed.", "IMAGE_GEN");
                }
                TriggerImageGeneration("Direct prompt execution (No Orchestration).", promptText);
            }
            else
            {
                StartLlmOrchestrator(promptText, attachedPath);
            }
        }

        private void StartLlmOrchestrator(string userPrompt, string attachedPath)
        {
            string model = _llmModelField.value;
            string apiKey = GetApiKey(model);
            if (string.IsNullOrEmpty(apiKey) && model != "self-hosted")
            {
                ShowError("Orchestrator API Key is missing. Please configure it in Omnisense Window -> Settings.");
                return;
            }

            SetLoadingState(true);
            ShowStatus("Thinking & refining prompt details (30-45 seconds)...");
            OmnisenseLogger.Log($"[Image Generator] Starting LLM orchestration request using model '{model}'. User prompt: \"{userPrompt}\"", "IMAGE_GEN");

            ILLMProvider providerImpl = LLMProviderFactory.GetProvider(model);
            if (providerImpl == null)
            {
                SetLoadingState(false);
                ShowError($"Unsupported LLM model: {model}");
                return;
            }

            string providerMode = _providerField.value;
            var messages = new List<LLMMessage>();
            messages.Add(new LLMMessage {
                role = "system",
                content = "You are the Omnisense AI Image Orchestrator. Your role is to help the user refine, describe, and iterate on visual assets, textures, and sprites inside the Unity editor.\n" +
                          $"CRITICAL: The active Image Generation Provider is currently set to: '{providerMode}'.\n" +
                          "You MUST generate the required image asset in the first go. Do not just chat, ask questions, or propose ideas. Proceed to generate immediately:\n" +
                          "You MUST synthesize the context (including any attached reference image) and output an optimized, highly-descriptive prompt for the single-shot image generator and populate it in 'imagePrompt'. Keep it extremely detailed, focusing on art style, dimensions, quality tags, and materials.\n\n" +
                          "Format your entire response strictly as a JSON block with no other markdown wrap, matching this schema:\n" +
                          "{\n  \"explanation\": \"your explanation here\",\n  \"imagePrompt\": \"your optimized image generation prompt here\"\n}"
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
            _activeRequest = providerImpl.BuildRequest(apiKey, model, messages, maxTokens);
            _requestStartTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += CheckLlmOrchestratorProgress;
            _activeRequest.SendWebRequest();
        }

        private void CheckLlmOrchestratorProgress()
        {
            if (_activeRequest == null)
            {
                EditorApplication.update -= CheckLlmOrchestratorProgress;
                return;
            }

            double elapsed = EditorApplication.timeSinceStartup - _requestStartTime;

            if (_activeRequest.isDone)
            {
                EditorApplication.update -= CheckLlmOrchestratorProgress;
                SetLoadingState(false);

                if (_activeRequest.result == UnityWebRequest.Result.Success)
                {
                    string rawResponse = _activeRequest.downloadHandler.text;
                    string model = _llmModelField.value;
                    ILLMProvider providerImpl = LLMProviderFactory.GetProvider(model);
                    string parsedContent = providerImpl.ParseResponseContent(rawResponse);
                    OmnisenseLogger.Log($"[Image Generator] LLM orchestration request succeeded. Response content: {parsedContent}", "IMAGE_GEN");

                    ProcessOrchestratorReply(parsedContent);
                }
                else
                {
                    string err = _activeRequest.downloadHandler?.text ?? _activeRequest.error;
                    OmnisenseLogger.LogError($"[Image Generator] LLM orchestration request failed. Error: {_activeRequest.error}, Details: {err}", "IMAGE_GEN");
                    ShowError($"LLM Request Failed: {_activeRequest.error}");
                }
                _activeRequest.Dispose();
                _activeRequest = null;
            }
            else if (elapsed > 180)
            {
                EditorApplication.update -= CheckLlmOrchestratorProgress;
                _activeRequest.Abort();
                _activeRequest.Dispose();
                _activeRequest = null;
                SetLoadingState(false);
                OmnisenseLogger.LogError($"[Image Generator] LLM orchestration request timed out after {elapsed:F0} seconds.", "IMAGE_GEN");
                ShowError($"LLM Request timed out after {elapsed:F0} seconds.");
            }
        }

        [Serializable]
        private class ImageResponseDTO
        {
            public string explanation;
            public string imagePrompt;
        }

        private void ProcessOrchestratorReply(string rawResponse)
        {
            string explanation = "";
            string imagePrompt = "";

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
                    var dto = JsonUtility.FromJson<ImageResponseDTO>(cleanResponse);
                    explanation = dto.explanation;
                    imagePrompt = dto.imagePrompt;
                }
                else
                {
                    explanation = "Refining prompt...";
                    imagePrompt = cleanResponse;
                }
            }
            catch
            {
                explanation = "Refining prompt...";
                imagePrompt = cleanResponse;
            }

            if (string.IsNullOrEmpty(explanation)) explanation = "Generated image successfully.";

            if (!string.IsNullOrEmpty(imagePrompt))
            {
                TriggerImageGeneration(explanation, imagePrompt);
            }
            else
            {
                var assistantMsg = new ImageChatMessage {
                    sender = "assistant",
                    content = explanation,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };
                _activeSession.messages.Add(assistantMsg);
                OmnisenseImageSessionManager.SaveSession(_activeSession);
                RenderActiveSessionMessages();
            }
        }

        private void TriggerImageGeneration(string explanation, string prompt)
        {
            _pendingExplanation = explanation;

            string provider = _providerField.value;
            string apiKey = "";

            if (provider == "OpenAI Image")
            {
                apiKey = EditorPrefs.GetString("Omnisense_OpenAI_Key", "");
                if (string.IsNullOrEmpty(apiKey))
                {
                    AddSystemErrorMessage(explanation, "OpenAI API Key is missing. Please configure it in Settings.");
                    return;
                }
            }
            else if (provider == "Google Imagen")
            {
                apiKey = EditorPrefs.GetString("Omnisense_Gemini_Key", "");
                if (string.IsNullOrEmpty(apiKey))
                {
                    AddSystemErrorMessage(explanation, "Google Gemini API Key is missing. Please configure it in Settings.");
                    return;
                }
            }

            SetLoadingState(true);
            ShowStatus($"Generating image using {provider}...");

            string styleSuffix = "";
            string selectedStyle = _styleField.value;
            if (selectedStyle == "Pixel Art") styleSuffix = ", pixel art style, 2d game sprite, clean background";
            else if (selectedStyle == "Stylized") styleSuffix = ", stylized digital art, vibrant colors, game asset";
            else if (selectedStyle == "2D Platformer") styleSuffix = ", 2d platformer asset, side view, clean background, game graphic";
            else if (selectedStyle == "Realistic / Photo") styleSuffix = ", photorealistic, highly detailed, 8k resolution";
            else if (selectedStyle == "Water Color") styleSuffix = ", watercolor painting, artistic, soft lighting";
            else if (selectedStyle == "Sci-Fi / Cyberpunk") styleSuffix = ", cyberpunk style, neon lighting, sci-fi concept art";

            string finalPrompt = prompt + styleSuffix;
            int width = 1024;
            int height = 1024;

            string dimVal = _dimensionField.value;
            if (provider == "OpenAI Image")
            {
                if (dimVal == "1792x1024 (Landscape 16:9)")
                {
                    width = 1792;
                    height = 1024;
                }
                else if (dimVal == "1024x1792 (Portrait 9:16)")
                {
                    width = 1024;
                    height = 1792;
                }
                else
                {
                    width = 1024;
                    height = 1024;
                }
            }
            else // Google Imagen
            {
                if (dimVal == "4:3 (Landscape)")
                {
                    width = 1024;
                    height = 768;
                }
                else if (dimVal == "16:9 (Widescreen)")
                {
                    width = 1024;
                    height = 576;
                }
                else if (dimVal == "3:4 (Portrait)")
                {
                    width = 768;
                    height = 1024;
                }
                else if (dimVal == "9:16 (Tall)")
                {
                    width = 576;
                    height = 1024;
                }
                else
                {
                    width = 1024;
                    height = 1024;
                }
            }

            if (provider == "OpenAI Image")
            {
                SendOpenAIImageRequest(finalPrompt, width, height, apiKey);
            }
            else
            {
                SendImagenImageRequest(finalPrompt, width, height, apiKey);
            }
        }

        private void SendOpenAIImageRequest(string finalPrompt, int width, int height, string apiKey)
        {
            string url = "https://api.openai.com/v1/images/generations";
            string model = "gpt-image-1";

            // GPT Image 1 only supports 1024x1024, 1792x1024, or 1024x1792
            string sizeStr = "1024x1024";
            double aspect = (double)width / height;
            if (aspect > 1.3)
            {
                sizeStr = "1792x1024";
                OmnisenseLogger.Log($"[Image Generator] GPT-Image-1 selected. Snapping dimensions to landscape: {sizeStr} (Requested: {width}x{height})", "IMAGE_GEN");
            }
            else if (aspect < 0.77)
            {
                sizeStr = "1024x1792";
                OmnisenseLogger.Log($"[Image Generator] GPT-Image-1 selected. Snapping dimensions to portrait: {sizeStr} (Requested: {width}x{height})", "IMAGE_GEN");
            }
            else
            {
                sizeStr = "1024x1024";
                OmnisenseLogger.Log($"[Image Generator] GPT-Image-1 selected. Snapping dimensions to square: {sizeStr} (Requested: {width}x{height})", "IMAGE_GEN");
            }

            string body = "{" +
                $"\"model\":\"{model}\"," +
                $"\"prompt\":\"{JsonEscape(finalPrompt)}\"," +
                $"\"n\":1," +
                $"\"size\":\"{sizeStr}\"" +
                "}";

            OmnisenseLogger.Log($"[Image Generator] Sending OpenAI Image Request. URL: {url}\nPayload: {body}", "IMAGE_GEN");

            var req = new UnityWebRequest(url, "POST");
            req.timeout = 180;
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(body);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);

            _activeGenRequest = req;
            _requestStartTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += CheckGenRequestProgress;
            _activeGenRequest.SendWebRequest();
        }

        private void SendImagenImageRequest(string finalPrompt, int width, int height, string apiKey)
        {
            string aspect = GetImagenAspectRatio(width, height);
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/imagen-3.0-generate-002:generateImages?key={apiKey}";

            string body = "{" +
                $"\"prompt\":{{\"text\":\"{JsonEscape(finalPrompt)}\"}}," +
                $"\"numberOfImages\":1," +
                $"\"outputMimeType\":\"image/png\"," +
                $"\"aspectRatio\":\"{aspect}\"," +
                $"\"personGeneration\":\"ALLOW_ADULT\"" +
                "}";

            OmnisenseLogger.Log($"[Image Generator] Sending Google Imagen Image Request. URL: {url}\nPayload: {body}", "IMAGE_GEN");

            var req = new UnityWebRequest(url, "POST");
            req.timeout = 180;
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(body);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            _activeGenRequest = req;
            _requestStartTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += CheckGenRequestProgress;
            _activeGenRequest.SendWebRequest();
        }

        private void CheckGenRequestProgress()
        {
            if (_activeGenRequest == null)
            {
                EditorApplication.update -= CheckGenRequestProgress;
                return;
            }

            double elapsed = EditorApplication.timeSinceStartup - _requestStartTime;

            if (_activeGenRequest.isDone)
            {
                EditorApplication.update -= CheckGenRequestProgress;
                SetLoadingState(false);

                if (_activeGenRequest.result == UnityWebRequest.Result.Success)
                {
                    string text = _activeGenRequest.downloadHandler.text;
                    OmnisenseLogger.Log($"[Image Generator] Image API response succeeded. Response: {text}", "IMAGE_GEN");
                    ProcessFinishedGenRequest(_activeGenRequest);
                }
                else
                {
                    string errDetail = "";
                    try { errDetail = _activeGenRequest.downloadHandler?.text ?? ""; } catch { }
                    OmnisenseLogger.LogError($"[Image Generator] Image API request failed. Status: {_activeGenRequest.responseCode}, Error: {_activeGenRequest.error}, Details: {errDetail}", "IMAGE_GEN");
                    AddSystemErrorMessage(_pendingExplanation, $"API Generation Failed: {_activeGenRequest.error}\nDetails: {errDetail}");
                }
                _activeGenRequest.Dispose();
                _activeGenRequest = null;
            }
            else if (elapsed > 180)
            {
                EditorApplication.update -= CheckGenRequestProgress;
                _activeGenRequest.Abort();
                _activeGenRequest.Dispose();
                _activeGenRequest = null;
                SetLoadingState(false);
                OmnisenseLogger.LogError($"[Image Generator] Image request timed out after {elapsed:F0} seconds.", "IMAGE_GEN");
                AddSystemErrorMessage(_pendingExplanation, $"Generation timed out after {elapsed:F0} seconds.");
            }
        }

        private void ProcessFinishedGenRequest(UnityWebRequest req)
        {
            string responseText = req.downloadHandler.text;
            string provider = _providerField.value;

            if (provider == "OpenAI Image")
            {
                var b64Match = Regex.Match(responseText, @"""b64_json""\s*:\s*""([^""]+)""");
                if (b64Match.Success)
                {
                    string base64Data = b64Match.Groups[1].Value;
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(base64Data);
                        SaveAndImportGenImage(bytes);
                    }
                    catch (Exception ex)
                    {
                        AddSystemErrorMessage(_pendingExplanation, $"Failed to parse base64 bytes: {ex.Message}");
                    }
                }
                else
                {
                    var urlMatch = Regex.Match(responseText, @"""url""\s*:\s*""([^""]+)""");
                    if (urlMatch.Success)
                    {
                        string imageUrl = urlMatch.Groups[1].Value.Replace("\\/", "/");
                        ShowStatus("Downloading generated image payload...");
                        DownloadGenImageBytes(imageUrl);
                    }
                    else
                    {
                        AddSystemErrorMessage(_pendingExplanation, "Failed to parse DALL-E response JSON.");
                    }
                }
            }
            else // Google Imagen
            {
                var match = Regex.Match(responseText, @"""imageBytes""\s*:\s*""([^""]+)""");
                if (match.Success)
                {
                    string base64Data = match.Groups[1].Value;
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(base64Data);
                        SaveAndImportGenImage(bytes);
                    }
                    catch (Exception ex)
                    {
                        AddSystemErrorMessage(_pendingExplanation, $"Failed to parse Imagen bytes: {ex.Message}");
                    }
                }
                else
                {
                    AddSystemErrorMessage(_pendingExplanation, "Failed to parse Google Imagen response JSON.");
                }
            }
        }

        private void DownloadGenImageBytes(string url)
        {
            SetLoadingState(true);
            OmnisenseLogger.Log($"[Image Generator] Downloading generated image payload from URL: {url}", "IMAGE_GEN");
            var req = UnityWebRequest.Get(url);
            req.timeout = 180;
            double startTime = EditorApplication.timeSinceStartup;

            EditorApplication.update += () => {
                if (req == null) return;
                if (req.isDone)
                {
                    SetLoadingState(false);
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        byte[] bytes = req.downloadHandler.data;
                        OmnisenseLogger.Log($"[Image Generator] Downloaded image payload successfully. Size: {bytes.Length} bytes.", "IMAGE_GEN");
                        SaveAndImportGenImage(bytes);
                    }
                    else
                    {
                        OmnisenseLogger.LogError($"[Image Generator] Failed to download image payload. Error: {req.error}, Status Code: {req.responseCode}", "IMAGE_GEN");
                        AddSystemErrorMessage(_pendingExplanation, $"Failed to download image payload: {req.error}");
                    }
                    req.Dispose();
                    req = null;
                }
                else if (EditorApplication.timeSinceStartup - startTime > 180)
                {
                    req.Abort();
                    req.Dispose();
                    req = null;
                    SetLoadingState(false);
                    OmnisenseLogger.LogError($"[Image Generator] Image payload download timed out.", "IMAGE_GEN");
                    AddSystemErrorMessage(_pendingExplanation, "Download timed out.");
                }
            };
            req.SendWebRequest();
        }

        private void SaveAndImportGenImage(byte[] imageBytes)
        {
            string rawPath = _pathField.value.Trim();
            if (string.IsNullOrEmpty(rawPath)) rawPath = "Assets/";

            string targetDir = rawPath;
            string finalPath = rawPath;

            if (rawPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                rawPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                rawPath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
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
                finalPath = targetDir + "image_" + timestamp + ".png";
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
                if (string.IsNullOrEmpty(ext)) ext = ".png";
                string baseName = Path.GetFileNameWithoutExtension(finalPath);
                string absoluteFilePath = Path.Combine(absoluteDir, baseName + ext);

                int counter = 1;
                while (File.Exists(absoluteFilePath))
                {
                    absoluteFilePath = Path.Combine(absoluteDir, $"{baseName}_{counter}{ext}");
                    finalPath = Path.Combine(targetDir, $"{baseName}_{counter}{ext}").Replace("\\", "/");
                    counter++;
                }

                File.WriteAllBytes(absoluteFilePath, imageBytes);
                AssetDatabase.ImportAsset(finalPath);
                AssetDatabase.Refresh();
                OmnisenseLogger.Log($"[Image Generator] Successfully saved and imported generated image file at: {finalPath}", "IMAGE_GEN");

                _lastGeneratedPath = finalPath;

                var assistantMsg = new ImageChatMessage {
                    sender = "assistant",
                    content = _pendingExplanation,
                    generatedImagePath = finalPath,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };
                _activeSession.messages.Add(assistantMsg);
                OmnisenseImageSessionManager.SaveSession(_activeSession);

                ShowSuccess($"Image saved and imported successfully at:\n{finalPath}");
                RenderActiveSessionMessages();
            }
            catch (Exception ex)
            {
                OmnisenseLogger.LogError($"[Image Generator] Failed to write/import generated image file. Path: {finalPath}, Error: {ex.Message}", "IMAGE_GEN");
                AddSystemErrorMessage(_pendingExplanation, $"Failed to write image to file or import asset: {ex.Message}");
            }
        }

        private void AddSystemErrorMessage(string explanation, string error)
        {
            var assistantMsg = new ImageChatMessage {
                sender = "assistant",
                content = $"{explanation}\n\n[System Notice: {error}]",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            _activeSession.messages.Add(assistantMsg);
            OmnisenseImageSessionManager.SaveSession(_activeSession);
            RenderActiveSessionMessages();
            ShowError(error);
        }

        private string GetImagenAspectRatio(int w, int h)
        {
            float ratio = (float)w / h;
            if (Mathf.Abs(ratio - 1f) < 0.15f) return "1:1";
            if (ratio > 1f)
            {
                if (Mathf.Abs(ratio - 1.333f) < Mathf.Abs(ratio - 1.777f)) return "4:3";
                return "16:9";
            }
            else
            {
                if (Mathf.Abs(ratio - 0.75f) < Mathf.Abs(ratio - 0.5625f)) return "3:4";
                return "9:16";
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
            Debug.LogError("[Omnisense-ImageGen] " + msg);
        }

        private void ShowSuccess(string msg)
        {
            _statusLabel.style.color = new StyleColor(new Color(0.3f, 0.9f, 0.3f));
            _statusLabel.text = msg;
            Debug.Log("[Omnisense-ImageGen] " + msg);
        }

        private void SelectSelfHostedModel(string modelName)
        {
            EditorPrefs.SetString("Omnisense_SelfHosted_Model", modelName);
            _llmModelField.value = "self-hosted";
        }

        private void ShowModelMenu()
        {
            var menu = new GenericMenu();
            string currentModel = _llmModelField.value;

            menu.AddItem(new GUIContent("None"), currentModel == "None", () => _llmModelField.value = "None");
            menu.AddSeparator("");

            // OpenAI
            menu.AddItem(new GUIContent("open ai/gpt-5.5-thinking"), currentModel == "gpt-5.5-thinking", () => _llmModelField.value = "gpt-5.5-thinking");
            menu.AddItem(new GUIContent("open ai/gpt-5.5-pro"), currentModel == "gpt-5.5-pro", () => _llmModelField.value = "gpt-5.5-pro");
            menu.AddItem(new GUIContent("open ai/gpt-5.5"), currentModel == "gpt-5.5", () => _llmModelField.value = "gpt-5.5");
            menu.AddItem(new GUIContent("open ai/gpt-5.5-instant"), currentModel == "gpt-5.5-instant", () => _llmModelField.value = "gpt-5.5-instant");
            menu.AddItem(new GUIContent("open ai/gpt-5.4"), currentModel == "gpt-5.4", () => _llmModelField.value = "gpt-5.4");
            menu.AddItem(new GUIContent("open ai/gpt-5.4-mini"), currentModel == "gpt-5.4-mini", () => _llmModelField.value = "gpt-5.4-mini");
            menu.AddItem(new GUIContent("open ai/gpt-5.4-nano"), currentModel == "gpt-5.4-nano", () => _llmModelField.value = "gpt-5.4-nano");
            menu.AddItem(new GUIContent("open ai/o3-mini"), currentModel == "o3-mini", () => _llmModelField.value = "o3-mini");

            // Claude
            menu.AddItem(new GUIContent("claude/claude-fable-5"), currentModel == "claude-fable-5", () => _llmModelField.value = "claude-fable-5");
            menu.AddItem(new GUIContent("claude/claude-mythos-5"), currentModel == "claude-mythos-5", () => _llmModelField.value = "claude-mythos-5");
            menu.AddItem(new GUIContent("claude/claude-opus-4.8"), currentModel == "claude-opus-4.8", () => _llmModelField.value = "claude-opus-4.8");
            menu.AddItem(new GUIContent("claude/claude-sonnet-4.6"), currentModel == "claude-sonnet-4.6", () => _llmModelField.value = "claude-sonnet-4.6");
            menu.AddItem(new GUIContent("claude/claude-haiku-4.5"), currentModel == "claude-haiku-4.5", () => _llmModelField.value = "claude-haiku-4.5");
            menu.AddItem(new GUIContent("claude/claude-4.7-opus"), currentModel == "claude-4.7-opus", () => _llmModelField.value = "claude-4.7-opus");
            menu.AddItem(new GUIContent("claude/claude-4.6-sonnet"), currentModel == "claude-4.6-sonnet", () => _llmModelField.value = "claude-4.6-sonnet");
            menu.AddItem(new GUIContent("claude/claude-4.5-haiku"), currentModel == "claude-4.5-haiku", () => _llmModelField.value = "claude-4.5-haiku");

            // Gemini
            menu.AddItem(new GUIContent("gemini/gemini-3.1-pro"), currentModel == "gemini-3.1-pro", () => _llmModelField.value = "gemini-3.1-pro");
            menu.AddItem(new GUIContent("gemini/gemini-3.5-flash"), currentModel == "gemini-3.5-flash", () => _llmModelField.value = "gemini-3.5-flash");
            menu.AddItem(new GUIContent("gemini/gemini-3-flash"), currentModel == "gemini-3-flash", () => _llmModelField.value = "gemini-3-flash");
            menu.AddItem(new GUIContent("gemini/gemini-3.1-flash"), currentModel == "gemini-3.1-flash", () => _llmModelField.value = "gemini-3.1-flash");
            menu.AddItem(new GUIContent("gemini/gemini-3.1-flash-lite"), currentModel == "gemini-3.1-flash-lite", () => _llmModelField.value = "gemini-3.1-flash-lite");
            menu.AddItem(new GUIContent("gemini/gemini-2.5-pro"), currentModel == "gemini-2.5-pro", () => _llmModelField.value = "gemini-2.5-pro");
            menu.AddItem(new GUIContent("gemini/gemini-2.5-flash"), currentModel == "gemini-2.5-flash", () => _llmModelField.value = "gemini-2.5-flash");
            menu.AddItem(new GUIContent("gemini/gemini-2.5-flash-lite"), currentModel == "gemini-2.5-flash-lite", () => _llmModelField.value = "gemini-2.5-flash-lite");

            // Grok
            menu.AddItem(new GUIContent("grok/grok-4.3"), currentModel == "grok-4.3", () => _llmModelField.value = "grok-4.3");
            menu.AddItem(new GUIContent("grok/grok-build-0.1"), currentModel == "grok-build-0.1", () => _llmModelField.value = "grok-build-0.1");
            menu.AddItem(new GUIContent("grok/grok-latest"), currentModel == "grok-latest", () => _llmModelField.value = "grok-latest");
            menu.AddItem(new GUIContent("grok/grok-4.3-beta"), currentModel == "grok-4.3-beta", () => _llmModelField.value = "grok-4.3-beta");
            menu.AddItem(new GUIContent("grok/grok-4.20-beta-2"), currentModel == "grok-4.20-beta-2", () => _llmModelField.value = "grok-4.20-beta-2");
            menu.AddItem(new GUIContent("grok/grok-4.20-fast"), currentModel == "grok-4.20-fast", () => _llmModelField.value = "grok-4.20-fast");

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
            menu.AddItem(new GUIContent("other/deepseek-chat"), currentModel == "deepseek-chat", () => _llmModelField.value = "deepseek-chat");
            menu.AddItem(new GUIContent("other/deepseek-reasoner"), currentModel == "deepseek-reasoner", () => _llmModelField.value = "deepseek-reasoner");
            menu.AddItem(new GUIContent("other/qwen-2.5-coder"), currentModel == "qwen-2.5-coder", () => _llmModelField.value = "qwen-2.5-coder");
            menu.AddItem(new GUIContent("other/qwen-2.5-instruct"), currentModel == "qwen-2.5-instruct", () => _llmModelField.value = "qwen-2.5-instruct");
            menu.AddItem(new GUIContent("other/glm-4"), currentModel == "glm-4", () => _llmModelField.value = "glm-4");
            menu.AddItem(new GUIContent("other/kimi-k2"), currentModel == "kimi-k2", () => _llmModelField.value = "kimi-k2");
            menu.AddItem(new GUIContent("other/self-hosted"), currentModel == "self-hosted", () => _llmModelField.value = "self-hosted");

            menu.DropDown(_llmModelField.worldBound);
        }

        private void SetLoadingState(bool loading)
        {
            _generateBtn.SetEnabled(!loading);
            _promptField.SetEnabled(!loading);
            _styleField.SetEnabled(!loading);
            _providerField.SetEnabled(!loading);
            _llmModelField.SetEnabled(!loading);
            _dimensionField.SetEnabled(!loading);
            _pathField.SetEnabled(!loading);
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

        private void UpdateDimensionChoices(string provider)
        {
            if (_dimensionField == null) return;

            if (provider == "OpenAI Image")
            {
                _dimensionField.choices = new List<string> {
                    "1024x1024 (Square 1:1)",
                    "1792x1024 (Landscape 16:9)",
                    "1024x1792 (Portrait 9:16)"
                };
                string savedVal = EditorPrefs.GetString("Omnisense_ImgGen_DimOpenAI", "1024x1024 (Square 1:1)");
                if (_dimensionField.choices.Contains(savedVal)) _dimensionField.value = savedVal;
                else _dimensionField.value = "1024x1024 (Square 1:1)";
            }
            else // Google Imagen
            {
                _dimensionField.choices = new List<string> {
                    "1:1 (Square)",
                    "4:3 (Landscape)",
                    "16:9 (Widescreen)",
                    "3:4 (Portrait)",
                    "9:16 (Tall)"
                };
                string savedVal = EditorPrefs.GetString("Omnisense_ImgGen_DimImagen", "1:1 (Square)");
                if (_dimensionField.choices.Contains(savedVal)) _dimensionField.value = savedVal;
                else _dimensionField.value = "1:1 (Square)";
            }
        }
    }

    [Serializable]
    public class ImageChatMessage
    {
        public string sender; // "user" or "assistant"
        public string content; // prompt text
        public string timestamp;
        public string referenceImageName;
        public string referenceImageBase64;
        public string generatedImagePath;
    }

    [Serializable]
    public class ImageChatSession
    {
        public string id;
        public string name;
        public List<ImageChatMessage> messages = new List<ImageChatMessage>();
        public string lastUpdated;
    }

    public static class OmnisenseImageSessionManager
    {
        private static readonly string HistoryPath;

        static OmnisenseImageSessionManager()
        {
            try
            {
                HistoryPath = Path.Combine(Application.dataPath, "..", "UserSettings", "OmnisenseImageHistory");
            }
            catch (Exception)
            {
                HistoryPath = Path.Combine(Directory.GetCurrentDirectory(), "UserSettings", "OmnisenseImageHistory");
            }
        }

        public static void SaveSession(ImageChatSession session)
        {
            if (!Directory.Exists(HistoryPath)) Directory.CreateDirectory(HistoryPath);
            
            session.lastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string json = JsonUtility.ToJson(session, true);
            File.WriteAllText(Path.Combine(HistoryPath, $"{session.id}.json"), json);
        }

        public static List<ImageChatSession> GetAllSessions()
        {
            var sessions = new List<ImageChatSession>();
            if (!Directory.Exists(HistoryPath)) return sessions;

            foreach (var file in Directory.GetFiles(HistoryPath, "*.json"))
            {
                try {
                    string json = File.ReadAllText(file);
                    sessions.Add(JsonUtility.FromJson<ImageChatSession>(json));
                } catch { /* Corrupt file */ }
            }

            sessions.Sort((a, b) => string.Compare(b.lastUpdated, a.lastUpdated)); // Newest first
            return sessions;
        }

        public static ImageChatSession GetSessionById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            string path = Path.Combine(HistoryPath, $"{id}.json");
            if (!File.Exists(path)) return null;
            
            try {
                string json = File.ReadAllText(path);
                return JsonUtility.FromJson<ImageChatSession>(json);
            } catch { return null; }
        }

        public static ImageChatSession CreateNewSession()
        {
            var session = new ImageChatSession {
                id = Guid.NewGuid().ToString(),
                name = $"Image Chat {DateTime.Now:MMM dd, HH:mm}",
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
