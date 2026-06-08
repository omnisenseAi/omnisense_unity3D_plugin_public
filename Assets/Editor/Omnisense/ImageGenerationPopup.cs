using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEngine.Networking;

namespace Omnisense
{
    public class ImageGenerationPopup : EditorWindow
    {
        private TextField _promptField;
        private DropdownField _styleField;
        private DropdownField _providerField;
        private IntegerField _widthField;
        private IntegerField _heightField;
        private TextField _pathField;
        private Button _generateBtn;
        private Label _statusLabel;

        private UnityWebRequest _activeRequest;
        private double _requestStartTime;

        [MenuItem("Window/Omnisense Image Generator")]
        public static void Open()
        {
            var window = GetWindow<ImageGenerationPopup>(true, "🎨 AI Image Generator", true);
            window.minSize = new Vector2(400, 520);
            window.maxSize = new Vector2(400, 520);
            window.Show();
        }

        private void OnEnable()
        {
            BuildUI();
        }

        private void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.backgroundColor = new StyleColor(new Color(0.17f, 0.18f, 0.2f));
            root.style.paddingLeft = 15;
            root.style.paddingRight = 15;
            root.style.paddingTop = 15;
            root.style.paddingBottom = 15;

            // Title Header
            var header = new Label("🎨 AI Image Generator");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.fontSize = 16;
            header.style.color = new StyleColor(new Color(0.9f, 0.9f, 0.9f));
            header.style.marginBottom = 15;
            header.style.alignSelf = Align.Center;
            root.Add(header);

            // Prompt Area
            var promptContainer = new VisualElement();
            promptContainer.style.marginBottom = 12;
            var promptLabel = new Label("Prompt:");
            promptLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            promptLabel.style.marginBottom = 4;
            promptLabel.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f));
            _promptField = new TextField { multiline = true };
            _promptField.style.minHeight = 80;
            _promptField.style.maxHeight = 120;
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
            root.Add(promptContainer);

            // Dropdowns (Style and Provider)
            var dropdownsRow = new VisualElement();
            dropdownsRow.style.flexDirection = FlexDirection.Row;
            dropdownsRow.style.justifyContent = Justify.SpaceBetween;
            dropdownsRow.style.marginBottom = 12;

            var styleContainer = new VisualElement();
            styleContainer.style.width = Length.Percent(48);
            var styleLabel = new Label("Style Preset:");
            styleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            styleLabel.style.marginBottom = 4;
            styleLabel.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f));
            _styleField = new DropdownField();
            _styleField.choices = new System.Collections.Generic.List<string> {
                "No Style", "Pixel Art", "Stylized", "2D Platformer", "Realistic / Photo", "Water Color", "Sci-Fi / Cyberpunk"
            };
            _styleField.value = "No Style";
            styleContainer.Add(styleLabel);
            styleContainer.Add(_styleField);
            dropdownsRow.Add(styleContainer);

            var providerContainer = new VisualElement();
            providerContainer.style.width = Length.Percent(48);
            var providerLabel = new Label("AI Provider:");
            providerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            providerLabel.style.marginBottom = 4;
            providerLabel.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f));
            _providerField = new DropdownField();
            _providerField.choices = new System.Collections.Generic.List<string> {
                "OpenAI Image", "Google Imagen"
            };
            _providerField.value = "OpenAI Image";
            providerContainer.Add(providerLabel);
            providerContainer.Add(_providerField);
            dropdownsRow.Add(providerContainer);

            root.Add(dropdownsRow);

            // Size / Dimensions
            var sizeContainer = new VisualElement();
            sizeContainer.style.marginBottom = 12;
            var sizeLabel = new Label("Dimensions (Width x Height):");
            sizeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            sizeLabel.style.marginBottom = 4;
            sizeLabel.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f));
            
            var sizeRow = new VisualElement();
            sizeRow.style.flexDirection = FlexDirection.Row;
            sizeRow.style.alignItems = Align.Center;

            _widthField = new IntegerField();
            _widthField.value = 1024;
            _widthField.style.width = 80;
            var widthInput = _widthField.Q("unity-text-input");
            if (widthInput != null) widthInput.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.12f));

            var xLabel = new Label(" x ");
            xLabel.style.marginLeft = 5;
            xLabel.style.marginRight = 5;
            xLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));

            _heightField = new IntegerField();
            _heightField.value = 1024;
            _heightField.style.width = 80;
            var heightInput = _heightField.Q("unity-text-input");
            if (heightInput != null) heightInput.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.12f));

            sizeRow.Add(_widthField);
            sizeRow.Add(xLabel);
            sizeRow.Add(_heightField);
            sizeContainer.Add(sizeLabel);
            sizeContainer.Add(sizeRow);
            root.Add(sizeContainer);

            // Target Storage Path
            var pathContainer = new VisualElement();
            pathContainer.style.marginBottom = 20;
            var pathLabel = new Label("Save Location:");
            pathLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            pathLabel.style.marginBottom = 4;
            pathLabel.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f));

            var pathRow = new VisualElement();
            pathRow.style.flexDirection = FlexDirection.Row;
            pathRow.style.alignItems = Align.Center;

            _pathField = new TextField();
            _pathField.value = PlayerPrefs.GetString("Omnisense_ImgGen_Path", "Assets/");
            _pathField.style.flexGrow = 1;
            var pathInput = _pathField.Q("unity-text-input");
            if (pathInput != null) pathInput.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.12f));
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
            browseBtn.style.marginLeft = 5;
            browseBtn.style.width = 30;
            browseBtn.style.backgroundColor = new StyleColor(new Color(0.25f, 0.27f, 0.3f));
            browseBtn.style.color = new StyleColor(Color.white);

            pathRow.Add(_pathField);
            pathRow.Add(browseBtn);
            pathContainer.Add(pathLabel);
            pathContainer.Add(pathRow);
            root.Add(pathContainer);

            // Status Label
            _statusLabel = new Label("");
            _statusLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _statusLabel.style.fontSize = 11;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginBottom = 15;
            _statusLabel.style.minHeight = 30;
            _statusLabel.style.alignSelf = Align.Center;
            root.Add(_statusLabel);

            // Generate Button
            _generateBtn = new Button(OnGenerateClicked) { text = "Generate Image" };
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
            root.Add(_generateBtn);
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
            string apiKey = "";

            Debug.Log($"[Omnisense-ImageGen] Start image generation. Provider: {provider}, Prompt: '{prompt}', Preset Style: {_styleField.value}");

            if (provider == "OpenAI Image")
            {
                apiKey = EditorPrefs.GetString("Omnisense_OpenAI_Key", "");
                if (string.IsNullOrEmpty(apiKey))
                {
                    ShowError("OpenAI API Key is missing. Please configure it in the Omnisense Window -> Settings tab.");
                    return;
                }
                string maskedKey = apiKey.Length > 8 ? $"{apiKey.Substring(0, 4)}...{apiKey.Substring(apiKey.Length - 4)}" : "invalid-key";
                Debug.Log($"[Omnisense-ImageGen] Loaded OpenAI API Key: {maskedKey}");
            }
            else if (provider == "Google Imagen")
            {
                apiKey = EditorPrefs.GetString("Omnisense_Gemini_Key", "");
                if (string.IsNullOrEmpty(apiKey))
                {
                    ShowError("Google Gemini API Key is missing. Please configure it in the Omnisense Window -> Settings tab.");
                    return;
                }
                string maskedKey = apiKey.Length > 8 ? $"{apiKey.Substring(0, 4)}...{apiKey.Substring(apiKey.Length - 4)}" : "invalid-key";
                Debug.Log($"[Omnisense-ImageGen] Loaded Gemini API Key: {maskedKey}");
            }

            SetLoadingState(true);
            ShowStatus("Generating image asset (this can take 1-3 minutes)...");

            // Construct style suffix
            string styleSuffix = "";
            string selectedStyle = _styleField.value;
            if (selectedStyle == "Pixel Art") styleSuffix = ", pixel art style, 2d game sprite, clean background";
            else if (selectedStyle == "Stylized") styleSuffix = ", stylized digital art, vibrant colors, game asset";
            else if (selectedStyle == "2D Platformer") styleSuffix = ", 2d platformer asset, side view, clean background, game graphic";
            else if (selectedStyle == "Realistic / Photo") styleSuffix = ", photorealistic, highly detailed, 8k resolution";
            else if (selectedStyle == "Water Color") styleSuffix = ", watercolor painting, artistic, soft lighting";
            else if (selectedStyle == "Sci-Fi / Cyberpunk") styleSuffix = ", cyberpunk style, neon lighting, sci-fi concept art";

            string finalPrompt = prompt + styleSuffix;
            int width = _widthField.value;
            int height = _heightField.value;

            Debug.Log($"[Omnisense-ImageGen] Dimensions: {width}x{height}, Target Path: '{_pathField.value}'");
            Debug.Log($"[Omnisense-ImageGen] Final Prompt: '{finalPrompt}'");

            if (provider == "OpenAI Image")
            {
                SendOpenAIRequest(finalPrompt, width, height, apiKey);
            }
            else
            {
                SendImagenRequest(finalPrompt, width, height, apiKey);
            }
        }

        private void SendOpenAIRequest(string finalPrompt, int width, int height, string apiKey)
        {
            string url = "https://api.openai.com/v1/images/generations";
            string model = "dall-e-2";
            if ((width == 1024 && height == 1024) || (width == 1024 && height == 1792) || (width == 1792 && height == 1024) ||
                (width == 1536 && height == 1024) || (width == 1024 && height == 1536))
            {
                model = "gpt-image-1";
            }

            string body = "{" +
                $"\"model\":\"{model}\"," +
                $"\"prompt\":\"{JsonEscape(finalPrompt)}\"," +
                $"\"n\":1," +
                $"\"size\":\"{width}x{height}\"" +
                "}";

            Debug.Log($"[Omnisense-ImageGen] Dispatching OpenAI POST request to: {url}");
            Debug.Log($"[Omnisense-ImageGen] OpenAI Request JSON Body: {body}");

            var req = new UnityWebRequest(url, "POST");
            req.timeout = 180; // 3 minutes timeout
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(body);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);

            StartRequest(req);
        }

        private void SendImagenRequest(string finalPrompt, int width, int height, string apiKey)
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

            string maskedUrl = $"https://generativelanguage.googleapis.com/v1beta/models/imagen-3.0-generate-002:generateImages?key=AIza...{apiKey.Substring(apiKey.Length - 4)}";
            Debug.Log($"[Omnisense-ImageGen] Dispatching Gemini/Imagen POST request to: {maskedUrl}");
            Debug.Log($"[Omnisense-ImageGen] Google Imagen Request JSON Body: {body}");

            var req = new UnityWebRequest(url, "POST");
            req.timeout = 180; // 3 minutes timeout
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(body);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            StartRequest(req);
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

        private void StartRequest(UnityWebRequest req)
        {
            _activeRequest = req;
            _requestStartTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += CheckRequestProgress;
            Debug.Log("[Omnisense-ImageGen] WebRequest SendWebRequest() invoked.");
            _activeRequest.SendWebRequest();
        }

        private void CheckRequestProgress()
        {
            if (_activeRequest == null)
            {
                EditorApplication.update -= CheckRequestProgress;
                return;
            }

            double elapsed = EditorApplication.timeSinceStartup - _requestStartTime;

            if (_activeRequest.isDone)
            {
                EditorApplication.update -= CheckRequestProgress;
                Debug.Log($"[Omnisense-ImageGen] WebRequest completed in {elapsed:F2} seconds.");
                ProcessFinishedRequest(_activeRequest);
                _activeRequest = null;
            }
            else if (elapsed > 180)
            {
                EditorApplication.update -= CheckRequestProgress;
                Debug.LogError($"[Omnisense-ImageGen] Request timed out. Aborting after {elapsed:F2} seconds.");
                _activeRequest.Abort();
                _activeRequest.Dispose();
                _activeRequest = null;
                SetLoadingState(false);
                ShowError($"Request timed out after {elapsed:F0} seconds.");
            }
        }

        private void ProcessFinishedRequest(UnityWebRequest req)
        {
            SetLoadingState(false);

            Debug.Log($"[Omnisense-ImageGen] Processing response. Result: {req.result}, ResponseCode: {req.responseCode}");

            if (req.result != UnityWebRequest.Result.Success)
            {
                string errorDetail = "";
                try { errorDetail = req.downloadHandler?.text ?? ""; } catch { }
                ShowError($"API Request Failed: {req.error}\nDetails: {errorDetail}");
                req.Dispose();
                return;
            }

            string responseText = req.downloadHandler.text;
            req.Dispose();

            // Log response sample for diagnostics
            string sampleText = responseText.Length > 300 ? responseText.Substring(0, 300) + "..." : responseText;
            Debug.Log($"[Omnisense-ImageGen] Raw Response Sample:\n{sampleText}");

            string provider = _providerField.value;
            if (provider == "OpenAI Image")
            {
                Debug.Log("[Omnisense-ImageGen] Parsing OpenAI Image response...");
                var b64Match = Regex.Match(responseText, @"""b64_json""\s*:\s*""([^""]+)""");
                if (b64Match.Success)
                {
                    string base64Data = b64Match.Groups[1].Value;
                    Debug.Log($"[Omnisense-ImageGen] Successfully parsed base64 image data. Character count: {base64Data.Length}");
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(base64Data);
                        SaveAndImportImage(bytes);
                    }
                    catch (Exception ex)
                    {
                        ShowError($"Failed to parse base64 image bytes from response: {ex.Message}");
                    }
                }
                else
                {
                    Debug.Log("[Omnisense-ImageGen] No b64_json found. Checking for image URL...");
                    var urlMatch = Regex.Match(responseText, @"""url""\s*:\s*""([^""]+)""");
                    if (urlMatch.Success)
                    {
                        string imageUrl = urlMatch.Groups[1].Value.Replace("\\/", "/");
                        Debug.Log($"[Omnisense-ImageGen] Successfully parsed image URL: {imageUrl}");
                        ShowStatus("Downloading generated image payload...");
                        DownloadImageBytes(imageUrl);
                    }
                    else
                    {
                        ShowError("Failed to parse image URL or base64 data from response:\n" + responseText);
                    }
                }
            }
            else // Google Imagen
            {
                Debug.Log("[Omnisense-ImageGen] Parsing Google Imagen response...");
                var match = Regex.Match(responseText, @"""imageBytes""\s*:\s*""([^""]+)""");
                if (match.Success)
                {
                    string base64Data = match.Groups[1].Value;
                    Debug.Log($"[Omnisense-ImageGen] Successfully parsed base64 image bytes. Character count: {base64Data.Length}");
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(base64Data);
                        SaveAndImportImage(bytes);
                    }
                    catch (Exception ex)
                    {
                        ShowError($"Failed to parse image bytes from Google Imagen response: {ex.Message}");
                    }
                }
                else
                {
                    ShowError("Failed to parse imageBytes from Google Imagen response:\n" + responseText);
                }
            }
        }

        private void DownloadImageBytes(string url)
        {
            SetLoadingState(true);
            Debug.Log($"[Omnisense-ImageGen] Downloading image payload from URL: {url}");
            var req = UnityWebRequest.Get(url);
            req.timeout = 180; // 3 minutes timeout
            double startTime = EditorApplication.timeSinceStartup;

            EditorApplication.update += () => {
                if (req == null) return;
                if (req.isDone)
                {
                    SetLoadingState(false);
                    double elapsed = EditorApplication.timeSinceStartup - startTime;
                    Debug.Log($"[Omnisense-ImageGen] Download completed in {elapsed:F2} seconds. Result: {req.result}");
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        byte[] bytes = req.downloadHandler.data;
                        Debug.Log($"[Omnisense-ImageGen] Downloaded payload size: {bytes.Length} bytes.");
                        SaveAndImportImage(bytes);
                    }
                    else
                    {
                        ShowError($"Failed to download image from URL: {req.error}");
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
                    ShowError("Image download timed out after 180 seconds.");
                }
            };

            req.SendWebRequest();
        }

        private void SaveAndImportImage(byte[] imageBytes)
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

                Debug.Log($"[Omnisense-ImageGen] Ensuring target folder exists: {absoluteDir}");
                if (!Directory.Exists(absoluteDir))
                {
                    Directory.CreateDirectory(absoluteDir);
                    Debug.Log($"[Omnisense-ImageGen] Created directory: {absoluteDir}");
                }

                string absoluteFilePath = finalPath;
                if (finalPath.StartsWith("Assets"))
                {
                    absoluteFilePath = Path.Combine(Application.dataPath, "..", finalPath);
                }

                Debug.Log($"[Omnisense-ImageGen] Writing {imageBytes.Length} bytes to file: {absoluteFilePath}");
                File.WriteAllBytes(absoluteFilePath, imageBytes);
                
                // Unity database import & refresh
                Debug.Log($"[Omnisense-ImageGen] Calling AssetDatabase.ImportAsset and Refresh for path: {finalPath}");
                AssetDatabase.ImportAsset(finalPath);
                AssetDatabase.Refresh();

                ShowSuccess($"Asset saved and imported successfully at:\n{finalPath}");
            }
            catch (Exception ex)
            {
                ShowError($"Failed to write image to file or import asset: {ex.Message}");
            }
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

        private void SetLoadingState(bool loading)
        {
            _generateBtn.SetEnabled(!loading);
            _promptField.SetEnabled(!loading);
            _styleField.SetEnabled(!loading);
            _providerField.SetEnabled(!loading);
            _widthField.SetEnabled(!loading);
            _heightField.SetEnabled(!loading);
            _pathField.SetEnabled(!loading);
        }

        private string JsonEscape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
