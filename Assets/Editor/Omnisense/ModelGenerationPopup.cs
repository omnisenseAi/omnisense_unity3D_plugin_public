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
    public class ModelGenerationPopup : EditorWindow
    {
        private TextField _promptField;
        private DropdownField _providerField;
        private DropdownField _modelSelector;
        private TextField _pathField;
        private Button _generateBtn;
        private Label _statusLabel;
        
        // Converter fields
        private TextField _jsFileField;
        private Button _convertBtn;

        private UnityWebRequest _activeRequest;
        private double _requestStartTime;

        // Polling states for 3D APIs
        private string _taskId = "";
        private string _pollingProvider = "";
        private double _lastPollTime = 0;
        private int _pollAttempts = 0;

        [MenuItem("Window/Omnisense 3D Model Generator")]
        public static void Open()
        {
            var window = GetWindow<ModelGenerationPopup>(true, "🧊 AI 3D Model Generator", true);
            window.minSize = new Vector2(420, 640);
            window.maxSize = new Vector2(420, 640);
            window.Show();
        }

        private void OnEnable()
        {
            BuildUI();
            EnsureNodeDependencies();
        }

        private void OnDisable()
        {
            EditorApplication.update -= CheckThreeJsRequestProgress;
            EditorApplication.update -= CheckMeshyRequestProgress;
            EditorApplication.update -= CheckTripoRequestProgress;
            EditorApplication.update -= PollTaskStatus;
        }

        private void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.backgroundColor = new StyleColor(new Color(0.17f, 0.18f, 0.2f));

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            scroll.style.paddingLeft = 15;
            scroll.style.paddingRight = 15;
            scroll.style.paddingTop = 15;
            scroll.style.paddingBottom = 15;
            root.Add(scroll);

            // Title Header
            var header = new Label("🧊 AI 3D Model Generator");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.fontSize = 16;
            header.style.color = new StyleColor(new Color(0.9f, 0.9f, 0.9f));
            header.style.marginBottom = 15;
            header.style.alignSelf = Align.Center;
            scroll.Add(header);

            // Prompt Area
            var promptContainer = new VisualElement();
            promptContainer.style.marginBottom = 12;
            var promptLabel = new Label("Prompt:");
            promptLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            promptLabel.style.marginBottom = 4;
            promptLabel.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f));
            _promptField = new TextField { multiline = true };
            _promptField.style.minHeight = 60;
            _promptField.style.maxHeight = 100;
            _promptField.style.whiteSpace = WhiteSpace.Normal;
            _promptField.value = "";
            var promptInputEl = _promptField.Q("unity-text-input");
            if (promptInputEl != null)
            {
                promptInputEl.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.12f));
                promptInputEl.style.borderLeftColor = new StyleColor(new Color(0.25f, 0.25f, 0.28f));
                promptInputEl.style.borderRightColor = new StyleColor(new Color(0.25f, 0.25f, 0.28f));
                promptInputEl.style.borderTopColor = new StyleColor(new Color(0.25f, 0.25f, 0.28f));
                promptInputEl.style.borderBottomColor = new StyleColor(new Color(0.25f, 0.25f, 0.28f));
                promptInputEl.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
            }
            promptContainer.Add(promptLabel);
            promptContainer.Add(_promptField);
            scroll.Add(promptContainer);

            // Provider
            var providerContainer = new VisualElement();
            providerContainer.style.marginBottom = 12;
            var providerLabel = new Label("AI Provider:");
            providerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            providerLabel.style.marginBottom = 4;
            providerLabel.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f));
            _providerField = new DropdownField();
            _providerField.choices = new List<string> {
                "Three.js Code Generator", "Meshy AI", "Tripo3D"
            };
            _providerField.value = "Three.js Code Generator";
            providerContainer.Add(providerLabel);
            providerContainer.Add(_providerField);
            scroll.Add(providerContainer);

            // Model Selection for Three.js (dynamic display)
            var modelContainer = new VisualElement();
            modelContainer.style.marginBottom = 12;
            var modelLabel = new Label("LLM Model (Three.js only):");
            modelLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            modelLabel.style.marginBottom = 4;
            modelLabel.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f));
            _modelSelector = new DropdownField();
            _modelSelector.choices = new List<string> {
                "gpt-5.5", "gpt-5.4-mini", "o3-mini",
                "claude-4.7-opus", "claude-4.6-sonnet", "claude-4.5-haiku",
                "gemini-3.1-pro", "gemini-3.1-flash", "gemini-3.1-flash-lite",
                "grok-4.3-beta", "grok-4.20-beta-2", "grok-4.20-fast",
                "self-hosted"
            };
            string savedModel = EditorPrefs.GetString("Omnisense_SelectedModel", "gpt-5.5");
            if (_modelSelector.choices.Contains(savedModel)) _modelSelector.value = savedModel;
            else _modelSelector.value = "gpt-5.5";
            _modelSelector.RegisterValueChangedCallback(evt => {
                EditorPrefs.SetString("Omnisense_SelectedModel", evt.newValue);
            });
            modelContainer.Add(modelLabel);
            modelContainer.Add(_modelSelector);
            scroll.Add(modelContainer);

            _providerField.RegisterValueChangedCallback(evt => {
                modelContainer.style.display = (evt.newValue == "Three.js Code Generator") ? DisplayStyle.Flex : DisplayStyle.None;
            });
            modelContainer.style.display = (_providerField.value == "Three.js Code Generator") ? DisplayStyle.Flex : DisplayStyle.None;

            // Target Storage Path
            var pathContainer = new VisualElement();
            pathContainer.style.marginBottom = 12;
            var pathLabel = new Label("Save Location:");
            pathLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            pathLabel.style.marginBottom = 4;
            pathLabel.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f));

            var pathRow = new VisualElement();
            pathRow.style.flexDirection = FlexDirection.Row;
            pathRow.style.alignItems = Align.Center;

            _pathField = new TextField();
            _pathField.value = PlayerPrefs.GetString("model_generation_save_location", "Assets/");
            _pathField.style.flexGrow = 1;
            var pathInput = _pathField.Q("unity-text-input");
            if (pathInput != null) pathInput.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.12f));
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
            browseBtn.style.marginLeft = 5;
            browseBtn.style.width = 30;
            browseBtn.style.backgroundColor = new StyleColor(new Color(0.25f, 0.27f, 0.3f));
            browseBtn.style.color = new StyleColor(Color.white);

            pathRow.Add(_pathField);
            pathRow.Add(browseBtn);
            pathContainer.Add(pathLabel);
            pathContainer.Add(pathRow);
            scroll.Add(pathContainer);

            // Status Label
            _statusLabel = new Label("");
            _statusLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _statusLabel.style.fontSize = 11;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginBottom = 10;
            _statusLabel.style.minHeight = 25;
            _statusLabel.style.alignSelf = Align.Center;
            scroll.Add(_statusLabel);

            // Generate Button
            _generateBtn = new Button(OnGenerateClicked) { text = "Generate 3D Model" };
            _generateBtn.style.height = 36;
            _generateBtn.style.fontSize = 13;
            _generateBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            _generateBtn.style.backgroundColor = new StyleColor(new Color(0.0f, 0.48f, 0.8f));
            _generateBtn.style.color = new StyleColor(Color.white);
            _generateBtn.style.borderTopLeftRadius = 6;
            _generateBtn.style.borderTopRightRadius = 6;
            _generateBtn.style.borderBottomLeftRadius = 6;
            _generateBtn.style.borderBottomRightRadius = 6;
            _generateBtn.style.borderLeftWidth = 0;
            _generateBtn.style.borderRightWidth = 0;
            _generateBtn.style.borderTopWidth = 0;
            _generateBtn.style.borderBottomWidth = 0;
            scroll.Add(_generateBtn);

            // Three.js to glTF Converter Section
            var converterContainer = new VisualElement();
            converterContainer.style.marginTop = 15;
            converterContainer.style.borderTopWidth = 1;
            converterContainer.style.borderTopColor = new StyleColor(new Color(0.3f, 0.3f, 0.3f, 0.5f));
            converterContainer.style.paddingTop = 15;

            var converterTitle = new Label("Three.js to glTF Converter");
            converterTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            converterTitle.style.fontSize = 13;
            converterTitle.style.color = new StyleColor(new Color(0.9f, 0.9f, 0.9f));
            converterTitle.style.marginBottom = 10;
            converterContainer.Add(converterTitle);

            var jsPathLabel = new Label("Select Three.js File:");
            jsPathLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            jsPathLabel.style.marginBottom = 4;
            jsPathLabel.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f));
            converterContainer.Add(jsPathLabel);

            var jsPathRow = new VisualElement();
            jsPathRow.style.flexDirection = FlexDirection.Row;
            jsPathRow.style.alignItems = Align.Center;

            _jsFileField = new TextField();
            _jsFileField.value = "";
            _jsFileField.style.flexGrow = 1;
            var jsFileInput = _jsFileField.Q("unity-text-input");
            if (jsFileInput != null) jsFileInput.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.12f));

            var jsBrowseBtn = new Button(() => {
                string selectedFile = EditorUtility.OpenFilePanel("Select Three.js File", "Assets", "js");
                if (!string.IsNullOrEmpty(selectedFile))
                {
                    if (selectedFile.StartsWith(Application.dataPath))
                    {
                        selectedFile = "Assets" + selectedFile.Substring(Application.dataPath.Length);
                    }
                    _jsFileField.value = selectedFile;
                }
            }) { text = "..." };
            jsBrowseBtn.style.marginLeft = 5;
            jsBrowseBtn.style.width = 30;
            jsBrowseBtn.style.backgroundColor = new StyleColor(new Color(0.25f, 0.27f, 0.3f));
            jsBrowseBtn.style.color = new StyleColor(Color.white);

            jsPathRow.Add(_jsFileField);
            jsPathRow.Add(jsBrowseBtn);
            converterContainer.Add(jsPathRow);

            // Convert Button
            _convertBtn = new Button(OnConvertClicked) { text = "Convert JS to glTF" };
            _convertBtn.style.marginTop = 10;
            _convertBtn.style.height = 30;
            _convertBtn.style.fontSize = 12;
            _convertBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            _convertBtn.style.backgroundColor = new StyleColor(new Color(0.2f, 0.44f, 0.68f));
            _convertBtn.style.color = new StyleColor(Color.white);
            _convertBtn.style.borderTopLeftRadius = 4;
            _convertBtn.style.borderTopRightRadius = 4;
            _convertBtn.style.borderBottomLeftRadius = 4;
            _convertBtn.style.borderBottomRightRadius = 4;
            _convertBtn.style.borderLeftWidth = 0;
            _convertBtn.style.borderRightWidth = 0;
            _convertBtn.style.borderTopWidth = 0;
            _convertBtn.style.borderBottomWidth = 0;
            converterContainer.Add(_convertBtn);

            scroll.Add(converterContainer);
        }

        private void OnGenerateClicked()
        {
            string prompt = _promptField.value.Trim();
            if (string.IsNullOrEmpty(prompt))
            {
                ShowError("Please enter a prompt description.");
                return;
            }

            string provider = _providerField.value;
            if (provider == "Three.js Code Generator")
            {
                string model = _modelSelector.value;
                string apiKey = GetApiKey(model);
                if (string.IsNullOrEmpty(apiKey) && model != "self-hosted")
                {
                    ShowError("API Key for the selected LLM model is missing. Please configure it in Settings.");
                    return;
                }

                SetLoadingState(true);
                ShowStatus("Generating Three.js code (this can take 30-60 seconds)...");

                ILLMProvider providerImpl = LLMProviderFactory.GetProvider(model);
                if (providerImpl == null)
                {
                    SetLoadingState(false);
                    ShowError($"Unsupported LLM model: {model}");
                    return;
                }

                var messages = new List<LLMMessage>
                {
                    new LLMMessage { role = "system", content = "You are a Three.js 3D model generator. Generate ONLY valid, executable Three.js JavaScript code that constructs the requested 3D model. Do not include any HTML, markdown formatting, or explain anything. Use ONLY Three.js primitives (geometries like THREE.BoxGeometry, THREE.ConeGeometry, THREE.CylinderGeometry, THREE.SphereGeometry, etc.), materials, and meshes. A pre-initialized THREE.Scene named 'scene' is already provided in the execution context. Do NOT instantiate a new scene, and do NOT write window or renderer initialization code. Add all your constructed meshes directly to the provided 'scene' object (e.g., scene.add(mesh)). Only output the code inside the js response, without markdown wrap." },
                    new LLMMessage { role = "user", content = prompt }
                };

                int maxTokens = GetMaxTokens(model);
                _activeRequest = providerImpl.BuildRequest(apiKey, model, messages, maxTokens);
                _requestStartTime = EditorApplication.timeSinceStartup;
                EditorApplication.update += CheckThreeJsRequestProgress;
                _activeRequest.SendWebRequest();
            }
            else if (provider == "Meshy AI")
            {
                string apiKey = EditorPrefs.GetString("Omnisense_Meshy_Key", "");
                if (string.IsNullOrEmpty(apiKey))
                {
                    ShowError("Meshy API Key is missing. Please configure it in settings.");
                    return;
                }

                SetLoadingState(true);
                ShowStatus("Contacting Meshy AI to queue model generation...");

                string url = "https://api.meshy.ai/openapi/v2/text-to-3d";
                string body = "{" +
                    $"\"mode\":\"preview\"," +
                    $"\"prompt\":\"{JsonEscape(prompt)}\"," +
                    $"\"model_type\":\"standard\"" +
                    "}";

                var req = new UnityWebRequest(url, "POST");
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(body);
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", "Bearer " + apiKey);

                _activeRequest = req;
                _requestStartTime = EditorApplication.timeSinceStartup;
                EditorApplication.update += CheckMeshyRequestProgress;
                _activeRequest.SendWebRequest();
            }
            else if (provider == "Tripo3D")
            {
                string apiKey = EditorPrefs.GetString("Omnisense_Tripo3D_Key", "");
                if (string.IsNullOrEmpty(apiKey))
                {
                    ShowError("Tripo3D API Key is missing. Please configure it in settings.");
                    return;
                }

                SetLoadingState(true);
                ShowStatus("Contacting Tripo3D to queue model generation...");

                string url = "https://api.tripo3d.ai/v2/openapi/task";
                string body = "{" +
                    $"\"type\":\"text_to_model\"," +
                    $"\"prompt\":\"{JsonEscape(prompt)}\"" +
                    "}";

                var req = new UnityWebRequest(url, "POST");
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(body);
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", "Bearer " + apiKey);

                _activeRequest = req;
                _requestStartTime = EditorApplication.timeSinceStartup;
                EditorApplication.update += CheckTripoRequestProgress;
                _activeRequest.SendWebRequest();
            }
        }

        private void CheckThreeJsRequestProgress()
        {
            if (_activeRequest == null)
            {
                EditorApplication.update -= CheckThreeJsRequestProgress;
                return;
            }

            if (_activeRequest.isDone)
            {
                EditorApplication.update -= CheckThreeJsRequestProgress;
                SetLoadingState(false);

                if (_activeRequest.result == UnityWebRequest.Result.Success)
                {
                    string rawResponse = _activeRequest.downloadHandler.text;
                    string model = _modelSelector.value;
                    ILLMProvider providerImpl = LLMProviderFactory.GetProvider(model);
                    string parsedContent = providerImpl.ParseResponseContent(rawResponse);

                    string cleanCode = parsedContent;
                    var match = Regex.Match(parsedContent, @"```(?:javascript|js)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        cleanCode = match.Groups[1].Value;
                    }
                    cleanCode = cleanCode.Trim();

                    SaveThreeJsFile(cleanCode);
                }
                else
                {
                    ShowError($"LLM Request Failed: {_activeRequest.error}");
                }
                _activeRequest.Dispose();
                _activeRequest = null;
            }
        }

        private void SaveThreeJsFile(string code)
        {
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

                string absoluteFilePath = finalPath;
                if (finalPath.StartsWith("Assets"))
                {
                    absoluteFilePath = Path.Combine(Application.dataPath, "..", finalPath);
                }

                File.WriteAllText(absoluteFilePath, code);
                AssetDatabase.ImportAsset(finalPath);
                AssetDatabase.Refresh();

                _jsFileField.value = finalPath;
                ShowSuccess($"Three.js code generated and saved successfully at:\n{finalPath}");
            }
            catch (Exception ex)
            {
                ShowError($"Failed to save Three.js file: {ex.Message}");
            }
        }

        private void CheckMeshyRequestProgress()
        {
            if (_activeRequest == null)
            {
                EditorApplication.update -= CheckMeshyRequestProgress;
                return;
            }

            if (_activeRequest.isDone)
            {
                EditorApplication.update -= CheckMeshyRequestProgress;
                if (_activeRequest.result == UnityWebRequest.Result.Success)
                {
                    string json = _activeRequest.downloadHandler.text;
                    var match = Regex.Match(json, @"""id""\s*:\s*""([^""]+)""");
                    if (match.Success)
                    {
                        string taskId = match.Groups[1].Value;
                        StartPolling(taskId, "Meshy AI");
                    }
                    else
                    {
                        SetLoadingState(false);
                        ShowError($"Failed to parse task ID from Meshy response:\n{json}");
                    }
                }
                else
                {
                    string err = _activeRequest.downloadHandler?.text ?? _activeRequest.error;
                    SetLoadingState(false);
                    ShowError($"Meshy Task creation failed: {err}");
                }
                _activeRequest.Dispose();
                _activeRequest = null;
            }
        }

        private void CheckTripoRequestProgress()
        {
            if (_activeRequest == null)
            {
                EditorApplication.update -= CheckTripoRequestProgress;
                return;
            }

            if (_activeRequest.isDone)
            {
                EditorApplication.update -= CheckTripoRequestProgress;
                if (_activeRequest.result == UnityWebRequest.Result.Success)
                {
                    string json = _activeRequest.downloadHandler.text;
                    var match = Regex.Match(json, @"""task_id""\s*:\s*""([^""]+)""");
                    if (match.Success)
                    {
                        string taskId = match.Groups[1].Value;
                        StartPolling(taskId, "Tripo3D");
                    }
                    else
                    {
                        SetLoadingState(false);
                        ShowError($"Failed to parse task ID from Tripo3D response:\n{json}");
                    }
                }
                else
                {
                    string err = _activeRequest.downloadHandler?.text ?? _activeRequest.error;
                    SetLoadingState(false);
                    ShowError($"Tripo3D Task creation failed: {err}");
                }
                _activeRequest.Dispose();
                _activeRequest = null;
            }
        }

        private void StartPolling(string taskId, string provider)
        {
            _taskId = taskId;
            _pollingProvider = provider;
            _lastPollTime = EditorApplication.timeSinceStartup;
            _pollAttempts = 0;
            SetLoadingState(true);
            ShowStatus("Task created. Generating 3D model (this can take 1-3 minutes)...");
            EditorApplication.update += PollTaskStatus;
        }

        private void PollTaskStatus()
        {
            double time = EditorApplication.timeSinceStartup;
            if (time - _lastPollTime < 3.0f) return;
            _lastPollTime = time;

            _pollAttempts++;
            if (_pollAttempts > 60) // 3 minutes timeout
            {
                EditorApplication.update -= PollTaskStatus;
                SetLoadingState(false);
                ShowError("Generation timed out during API execution.");
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
                url = $"https://api.tripo3d.ai/v2/openapi/task/{_taskId}";
                apiKey = EditorPrefs.GetString("Omnisense_Tripo3D_Key", "");
            }

            var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            var operation = req.SendWebRequest();
            operation.completed += (op) =>
            {
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[Omnisense-3D] Polling error: {req.error}");
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
                        ShowStatus($"Generating 3D model: {progress}% ({status})... (Attempt {_pollAttempts})");

                        if (status == "SUCCEEDED")
                        {
                            EditorApplication.update -= PollTaskStatus;
                            var glbMatch = Regex.Match(json, @"""glb""\s*:\s*""([^""]+)""");
                            if (glbMatch.Success)
                            {
                                string glbUrl = glbMatch.Groups[1].Value.Replace("\\/", "/");
                                DownloadModelBytes(glbUrl, "glb");
                            }
                            else
                            {
                                SetLoadingState(false);
                                ShowError("Failed to find GLB file URL in completed task response.");
                            }
                        }
                        else if (status == "FAILED")
                        {
                            EditorApplication.update -= PollTaskStatus;
                            SetLoadingState(false);
                            ShowError("Generation failed on Meshy AI server.");
                        }
                    }
                }
                else if (_pollingProvider == "Tripo3D")
                {
                    var statusMatch = Regex.Match(json, @"""status""\s*:\s*""([^""]+)""");
                    if (statusMatch.Success)
                    {
                        string status = statusMatch.Groups[1].Value;
                        ShowStatus($"Generating 3D model: {status}... (Attempt {_pollAttempts})");

                        if (status == "success")
                        {
                            EditorApplication.update -= PollTaskStatus;
                            var modelMatch = Regex.Match(json, @"""model""\s*:\s*""([^""]+)""");
                            if (modelMatch.Success)
                            {
                                string glbUrl = modelMatch.Groups[1].Value.Replace("\\/", "/");
                                DownloadModelBytes(glbUrl, "glb");
                            }
                            else
                            {
                                SetLoadingState(false);
                                ShowError("Failed to find GLB file URL in completed task response.");
                            }
                        }
                        else if (status == "failed")
                        {
                            EditorApplication.update -= PollTaskStatus;
                            SetLoadingState(false);
                            ShowError("Generation failed on Tripo3D server.");
                        }
                    }
                }
            };
        }

        private void DownloadModelBytes(string url, string format)
        {
            ShowStatus("Downloading 3D model file...");
            var req = UnityWebRequest.Get(url);
            var op = req.SendWebRequest();
            op.completed += (o) =>
            {
                SetLoadingState(false);
                if (req.result == UnityWebRequest.Result.Success)
                {
                    byte[] bytes = req.downloadHandler.data;
                    SaveAndImportModel(bytes, format);
                }
                else
                {
                    ShowError($"Failed to download model: {req.error}");
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

            if (rawPath.EndsWith(".glb") || rawPath.EndsWith(".gltf"))
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

                string absoluteFilePath = finalPath;
                if (finalPath.StartsWith("Assets"))
                {
                    absoluteFilePath = Path.Combine(Application.dataPath, "..", finalPath);
                }

                File.WriteAllBytes(absoluteFilePath, bytes);
                AssetDatabase.ImportAsset(finalPath);
                AssetDatabase.Refresh();

                ShowSuccess($"3D model saved and imported successfully at:\n{finalPath}");
            }
            catch (Exception ex)
            {
                ShowError($"Failed to save model file: {ex.Message}");
            }
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

            SetLoadingState(true);
            _convertBtn.SetEnabled(false);
            ShowStatus("Converting Three.js model to glTF...");

            // Ensure dependencies are installed
            EnsureNodeDependencies();

            string absoluteJsPath = jsPath.StartsWith("Assets") ? Path.Combine(Application.dataPath, "..", jsPath) : jsPath;
            string absoluteGltfPath = gltfPath.StartsWith("Assets") ? Path.Combine(Application.dataPath, "..", gltfPath) : gltfPath;

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
                            onComplete?.Invoke(true, gltfOutputPath);
                        }
                        else
                        {
                            string detail = string.IsNullOrEmpty(error) ? output : error;
                            onComplete?.Invoke(false, detail);
                        }
                    };
                    process.Dispose();
                };
                process.Start();
            }
            catch (Exception ex)
            {
                onComplete?.Invoke(false, ex.Message);
            }
        }

        private string GetApiKey(string model)
        {
            if (model.Contains("gpt") || model.Contains("o3")) return EditorPrefs.GetString("Omnisense_OpenAI_Key", "");
            if (model.Contains("claude")) return EditorPrefs.GetString("Omnisense_Anthropic_Key", "");
            if (model.Contains("gemini")) return EditorPrefs.GetString("Omnisense_Gemini_Key", "");
            if (model.Contains("grok")) return EditorPrefs.GetString("Omnisense_Grok_Key", "");
            if (model == "self-hosted") return EditorPrefs.GetString("Omnisense_SelfHosted_Key", "");
            return "";
        }

        private int GetMaxTokens(string model)
        {
            if (model.Contains("gpt") || model.Contains("o3")) return EditorPrefs.GetInt("Omnisense_OpenAI_MaxTokens", 4096);
            if (model.Contains("claude")) return EditorPrefs.GetInt("Omnisense_Anthropic_MaxTokens", 4096);
            if (model.Contains("gemini")) return EditorPrefs.GetInt("Omnisense_Gemini_MaxTokens", 4096);
            if (model.Contains("grok")) return EditorPrefs.GetInt("Omnisense_Grok_MaxTokens", 4096);
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

        private void SetLoadingState(bool loading)
        {
            _generateBtn.SetEnabled(!loading);
            _promptField.SetEnabled(!loading);
            _providerField.SetEnabled(!loading);
            _modelSelector.SetEnabled(!loading);
            _pathField.SetEnabled(!loading);
            if (_convertBtn != null) _convertBtn.SetEnabled(!loading);
        }

        private string JsonEscape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
