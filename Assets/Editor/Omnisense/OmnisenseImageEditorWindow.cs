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
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace Omnisense
{
    /// <summary>
    /// CORE PHILOSOPHY & DESIGN DECISION:
    /// The OmnisenseImageEditorWindow provides non-destructive, standalone manual image post-processing
    /// inside the Unity editor, fully integrated with UI Toolkit and Project tab workflows.
    /// 
    /// WHY:
    /// Often, AI-generated images or raw sprites require immediate, small adjustments (cropping, grid-slicing
    /// sheets, resizing, pivot adjustment) before being usable in game scenes. Forcing the developer to launch
    /// Photoshop or GIMP breaks focus.
    /// Crucially, we enforce a STRICTLY COPY-SAFE policy where the original asset is NEVER modified directly;
    /// all alterations compile into newly spawned template copies (e.g. `{Name}_sliced_X_Y.png`), maintaining
    /// absolute asset safety.
    /// 
    /// HOW:
    /// Combines UI Toolkit layouts for properties (grid size, pivots, aspect ratios) and an IMGUI container
    /// for responsive grid preview overlays. Includes drag-and-drop callbacks and native file picking systems.
    /// </summary>
    public class OmnisenseImageEditorWindow : EditorWindow
    {
        private enum EditMode
        {
            Splitter,
            CropperResizer
        }

        private enum SlicingType
        {
            GridCount, // Rows / Columns
            CellSize   // Pixel dimensions
        }

        private EditMode _currentMode = EditMode.Splitter;
        private SlicingType _slicingType = SlicingType.GridCount;

        // Active Asset
        private Texture2D _targetTexture;
        private string _assetPath = "";

        // Slicing Settings
        private int _gridColumns = 4;
        private int _gridRows = 4;
        private int _cellWidth = 64;
        private int _cellHeight = 64;
        private SpriteAlignment _pivotAlignment = SpriteAlignment.Center;
        private Vector2 _customPivot = new Vector2(0.5f, 0.5f);

        // Cropping / Resizing Settings
        private Rect _cropNormalized = new Rect(0.1f, 0.1f, 0.8f, 0.8f);
        private bool _lockAspectRatio = false;
        private float _lockedAspectVal = 1f;
        private string _aspectRatioString = "Free";
        private int _resizeWidth = 1024;
        private int _resizeHeight = 1024;
        private bool _useBilinear = true;

        // Output Settings
        private TextField _outputPathField;
        private TextField _imagePathField;

        // UI Controls (UI Toolkit)
        private Label _assetNameLabel;
        private Label _assetDetailsLabel;
        private VisualElement _controlPanel;
        private IMGUIContainer _canvasContainer;

        // Interactive GUI Dragging State
        private enum DragState
        {
            None,
            Move,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }
        private DragState _dragState = DragState.None;
        private Vector2 _dragStartMousePos;
        private Rect _dragStartCropRect;

        [MenuItem("Omnisense/Image Editor")]
        [MenuItem("Window/Omnisense/Image Editor")]
        public static void Open()
        {
            var window = GetWindow<OmnisenseImageEditorWindow>("🎨 Omnisense Image Editor");
            window.minSize = new Vector2(700, 520);
            window.Show();
        }

        public static void OpenWithAsset(string path)
        {
            var window = GetWindow<OmnisenseImageEditorWindow>("🎨 Omnisense Image Editor");
            window.minSize = new Vector2(700, 520);
            window.LoadAsset(path);
            window.Show();
        }

        [MenuItem("Assets/Omnisense/Image Editor")]
        public static void OpenFromAssets()
        {
            if (Selection.activeObject is Texture2D tex)
            {
                string path = AssetDatabase.GetAssetPath(tex);
                if (!string.IsNullOrEmpty(path))
                {
                    OpenWithAsset(path);
                }
            }
        }

        [MenuItem("Assets/Omnisense/Image Editor", true)]
        public static bool ValidateOpenFromAssets()
        {
            return Selection.activeObject is Texture2D;
        }

        private void OnEnable()
        {
            BuildUI();
            Selection.selectionChanged += OnSelectionChanged;
            OnSelectionChanged();

            var root = rootVisualElement;
            root.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            root.RegisterCallback<DragPerformEvent>(OnDragPerform);
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;

            var root = rootVisualElement;
            if (root != null)
            {
                root.UnregisterCallback<DragUpdatedEvent>(OnDragUpdated);
                root.UnregisterCallback<DragPerformEvent>(OnDragPerform);
            }
        }

        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            bool containsTexture = false;
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is Texture2D)
                {
                    containsTexture = true;
                    break;
                }
            }
            if (containsTexture)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            }
            else
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
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
                        LoadAsset(path);
                        break;
                    }
                }
            }
        }

        private void OnSelectionChanged()
        {
            if (Selection.activeObject is Texture2D tex)
            {
                string path = AssetDatabase.GetAssetPath(tex);
                if (!string.IsNullOrEmpty(path))
                {
                    LoadAsset(path);
                }
            }
        }

        private void LoadAsset(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
            {
                _targetTexture = tex;
                _assetPath = path;
                _resizeWidth = tex.width;
                _resizeHeight = tex.height;

                if (_assetNameLabel != null)
                {
                    _assetNameLabel.text = Path.GetFileName(path);
                    _assetDetailsLabel.text = $"{tex.width} x {tex.height} px | {(TextureImporter.GetAtPath(path) is TextureImporter imp ? imp.spriteImportMode.ToString() : "Default")}";
                }

                if (_imagePathField != null && _imagePathField.value != path)
                {
                    _imagePathField.SetValueWithoutNotify(path);
                }

                // Default output folder to the target texture folder
                string directory = Path.GetDirectoryName(path).Replace("\\", "/");
                if (_outputPathField != null && !string.IsNullOrEmpty(directory))
                {
                    _outputPathField.value = directory;
                    PlayerPrefs.SetString("Omnisense_ImgEditor_OutputPath", directory);
                    PlayerPrefs.Save();
                }
                
                if (_canvasContainer != null)
                {
                    _canvasContainer.MarkDirtyLayout();
                }
            }
        }

        private void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();

            // Background & Layout Styling
            root.style.backgroundColor = new StyleColor(new Color(0.18f, 0.19f, 0.2f));
            root.style.flexDirection = FlexDirection.Row;

            // 1. LEFT SIDEBAR PANEL (Controls)
            var sidebar = new VisualElement();
            sidebar.style.width = 280;
            sidebar.style.minWidth = 280;
            sidebar.style.maxWidth = 280;
            sidebar.style.backgroundColor = new StyleColor(new Color(0.14f, 0.15f, 0.16f));
            sidebar.style.paddingLeft = 12;
            sidebar.style.paddingRight = 12;
            sidebar.style.paddingTop = 12;
            sidebar.style.paddingBottom = 12;
            sidebar.style.borderRightColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f));
            sidebar.style.borderRightWidth = 1;
            root.Add(sidebar);

            // Asset Status Box
            var statusBox = new VisualElement();
            statusBox.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.11f));
            statusBox.style.paddingLeft = 8;
            statusBox.style.paddingRight = 8;
            statusBox.style.paddingTop = 8;
            statusBox.style.paddingBottom = 8;
            statusBox.style.marginBottom = 12;
            statusBox.style.borderTopLeftRadius = 4;
            statusBox.style.borderTopRightRadius = 4;
            statusBox.style.borderBottomLeftRadius = 4;
            statusBox.style.borderBottomRightRadius = 4;

            _assetNameLabel = new Label(_targetTexture != null ? Path.GetFileName(_assetPath) : "No Texture Loaded");
            _assetNameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _assetNameLabel.style.fontSize = 12;
            _assetNameLabel.style.color = new StyleColor(new Color(0.9f, 0.9f, 0.9f));
            
            _assetDetailsLabel = new Label(_targetTexture != null ? $"{_targetTexture.width} x {_targetTexture.height} px" : "Select a PNG/JPG texture inside Project tab.");
            _assetDetailsLabel.style.fontSize = 10;
            _assetDetailsLabel.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
            _assetDetailsLabel.style.whiteSpace = WhiteSpace.Normal;

            statusBox.Add(_assetNameLabel);
            statusBox.Add(_assetDetailsLabel);
            sidebar.Add(statusBox);

            // Target Image Selection Box
            var selectImgBox = new VisualElement();
            selectImgBox.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.11f));
            selectImgBox.style.paddingLeft = 8;
            selectImgBox.style.paddingRight = 8;
            selectImgBox.style.paddingTop = 8;
            selectImgBox.style.paddingBottom = 8;
            selectImgBox.style.marginBottom = 12;
            selectImgBox.style.borderTopLeftRadius = 4;
            selectImgBox.style.borderTopRightRadius = 4;
            selectImgBox.style.borderBottomLeftRadius = 4;
            selectImgBox.style.borderBottomRightRadius = 4;

            var selectImgLabel = new Label("Target Image Asset:");
            selectImgLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            selectImgLabel.style.fontSize = 11;
            selectImgLabel.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f));
            selectImgLabel.style.marginBottom = 4;

            var selectImgRow = new VisualElement();
            selectImgRow.style.flexDirection = FlexDirection.Row;
            selectImgRow.style.alignItems = Align.Center;

            _imagePathField = new TextField();
            _imagePathField.value = _assetPath;
            _imagePathField.style.flexGrow = 1;
            var imgInput = _imagePathField.Q("unity-text-input");
            if (imgInput != null) imgInput.style.backgroundColor = new StyleColor(new Color(0.08f, 0.08f, 0.1f));
            _imagePathField.RegisterValueChangedCallback(evt => {
                string p = evt.newValue.Trim();
                if (!string.IsNullOrEmpty(p))
                {
                    LoadAsset(p);
                }
            });

            var browseImgBtn = new Button(() => {
                string selected = EditorUtility.OpenFilePanel("Select Image Asset", "Assets", "png,jpg,jpeg,tga");
                if (!string.IsNullOrEmpty(selected))
                {
                    if (selected.StartsWith(Application.dataPath))
                    {
                        selected = "Assets" + selected.Substring(Application.dataPath.Length);
                    }
                    _imagePathField.value = selected;
                    LoadAsset(selected);
                }
            }) { text = "..." };
            browseImgBtn.style.marginLeft = 4;
            browseImgBtn.style.width = 25;
            browseImgBtn.style.height = 20;

            selectImgRow.Add(_imagePathField);
            selectImgRow.Add(browseImgBtn);
            selectImgBox.Add(selectImgLabel);
            selectImgBox.Add(selectImgRow);
            sidebar.Add(selectImgBox);

            // Mode Toggle Toolbar Buttons
            var modeToggleGroup = new VisualElement();
            modeToggleGroup.style.flexDirection = FlexDirection.Row;
            modeToggleGroup.style.marginBottom = 12;

            var btnSplitMode = new Button(() => SwitchMode(EditMode.Splitter)) { text = "✂ Sprite Splitter" };
            btnSplitMode.style.flexGrow = 1;
            btnSplitMode.style.fontSize = 11;
            btnSplitMode.style.height = 28;
            btnSplitMode.style.borderTopLeftRadius = 4;
            btnSplitMode.style.borderBottomLeftRadius = 4;
            btnSplitMode.style.borderTopRightRadius = 0;
            btnSplitMode.style.borderBottomRightRadius = 0;

            var btnCropMode = new Button(() => SwitchMode(EditMode.CropperResizer)) { text = "🖼 Cropper & Resizer" };
            btnCropMode.style.flexGrow = 1;
            btnCropMode.style.fontSize = 11;
            btnCropMode.style.height = 28;
            btnCropMode.style.borderTopLeftRadius = 0;
            btnCropMode.style.borderBottomLeftRadius = 0;
            btnCropMode.style.borderTopRightRadius = 4;
            btnCropMode.style.borderBottomRightRadius = 4;

            modeToggleGroup.Add(btnSplitMode);
            modeToggleGroup.Add(btnCropMode);
            sidebar.Add(modeToggleGroup);

            // Load Selection Button
            var loadBtn = new Button(() => {
                if (Selection.activeObject is Texture2D tex)
                {
                    LoadAsset(AssetDatabase.GetAssetPath(tex));
                }
                else
                {
                    EditorUtility.DisplayDialog("No Texture Selected", "Please select a Texture2D asset in the Project window and click Load Selection again.", "OK");
                }
            }) { text = "🔄 Load Selected Texture" };
            loadBtn.style.marginBottom = 12;
            loadBtn.style.height = 26;
            loadBtn.style.backgroundColor = new StyleColor(new Color(0.25f, 0.26f, 0.28f));
            loadBtn.style.color = new StyleColor(Color.white);
            sidebar.Add(loadBtn);

            // Output Folder Configuration Box
            var outputBox = new VisualElement();
            outputBox.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.11f));
            outputBox.style.paddingLeft = 8;
            outputBox.style.paddingRight = 8;
            outputBox.style.paddingTop = 8;
            outputBox.style.paddingBottom = 8;
            outputBox.style.marginBottom = 12;
            outputBox.style.borderTopLeftRadius = 4;
            outputBox.style.borderTopRightRadius = 4;
            outputBox.style.borderBottomLeftRadius = 4;
            outputBox.style.borderBottomRightRadius = 4;

            var outLabel = new Label("Save / Output Directory:");
            outLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            outLabel.style.fontSize = 11;
            outLabel.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f));
            outLabel.style.marginBottom = 4;

            var outRow = new VisualElement();
            outRow.style.flexDirection = FlexDirection.Row;
            outRow.style.alignItems = Align.Center;

            _outputPathField = new TextField();
            _outputPathField.value = PlayerPrefs.GetString("Omnisense_ImgEditor_OutputPath", "Assets/");
            _outputPathField.style.flexGrow = 1;
            var outInput = _outputPathField.Q("unity-text-input");
            if (outInput != null) outInput.style.backgroundColor = new StyleColor(new Color(0.08f, 0.08f, 0.1f));
            _outputPathField.RegisterValueChangedCallback(evt => {
                PlayerPrefs.SetString("Omnisense_ImgEditor_OutputPath", evt.newValue.Trim());
                PlayerPrefs.Save();
            });

            var browseOutBtn = new Button(() => {
                string selected = EditorUtility.OpenFolderPanel("Select Output Directory", "Assets", "");
                if (!string.IsNullOrEmpty(selected))
                {
                    if (selected.StartsWith(Application.dataPath))
                    {
                        selected = "Assets" + selected.Substring(Application.dataPath.Length);
                    }
                    _outputPathField.value = selected;
                }
            }) { text = "..." };
            browseOutBtn.style.marginLeft = 4;
            browseOutBtn.style.width = 25;
            browseOutBtn.style.height = 20;

            outRow.Add(_outputPathField);
            outRow.Add(browseOutBtn);
            outputBox.Add(outLabel);
            outputBox.Add(outRow);
            sidebar.Add(outputBox);

            // Container for dynamic settings
            _controlPanel = new VisualElement();
            sidebar.Add(_controlPanel);

            // 2. RIGHT WORKSPACE AREA (Interactive Preview Canvas)
            var rightArea = new VisualElement();
            rightArea.style.flexGrow = 1;
            rightArea.style.paddingLeft = 10;
            rightArea.style.paddingRight = 10;
            rightArea.style.paddingTop = 10;
            rightArea.style.paddingBottom = 10;
            rightArea.style.alignItems = Align.Center;
            rightArea.style.justifyContent = Justify.Center;
            root.Add(rightArea);

            _canvasContainer = new IMGUIContainer(DrawCanvas);
            _canvasContainer.style.width = Length.Percent(100);
            _canvasContainer.style.height = Length.Percent(100);
            rightArea.Add(_canvasContainer);

            // Set default tool mode panel
            SwitchMode(_currentMode);
        }

        private void SwitchMode(EditMode mode)
        {
            _currentMode = mode;
            _controlPanel.Clear();

            if (_currentMode == EditMode.Splitter)
            {
                RenderSplitterControls();
            }
            else
            {
                RenderCropperControls();
            }

            if (_canvasContainer != null)
            {
                _canvasContainer.MarkDirtyLayout();
            }
        }

        private void RenderSplitterControls()
        {
            var header = new Label("Grid Slicing Parameters");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 8;
            header.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
            _controlPanel.Add(header);

            // Slicing Mode (Grid Count vs Cell Size)
            var typeField = new DropdownField("Slice Method");
            typeField.choices = new List<string> { "Grid Columns & Rows", "Fixed Sprite Cell Size" };
            typeField.value = _slicingType == SlicingType.GridCount ? "Grid Columns & Rows" : "Fixed Sprite Cell Size";
            typeField.RegisterValueChangedCallback(evt => {
                _slicingType = evt.newValue == "Grid Columns & Rows" ? SlicingType.GridCount : SlicingType.CellSize;
                SwitchMode(EditMode.Splitter);
            });
            _controlPanel.Add(typeField);

            if (_slicingType == SlicingType.GridCount)
            {
                var colsField = new IntegerField("Columns");
                colsField.value = _gridColumns;
                colsField.RegisterValueChangedCallback(evt => {
                    _gridColumns = Mathf.Max(1, evt.newValue);
                    _canvasContainer.MarkDirtyLayout();
                });
                _controlPanel.Add(colsField);

                var rowsField = new IntegerField("Rows");
                rowsField.value = _gridRows;
                rowsField.RegisterValueChangedCallback(evt => {
                    _gridRows = Mathf.Max(1, evt.newValue);
                    _canvasContainer.MarkDirtyLayout();
                });
                _controlPanel.Add(rowsField);
            }
            else
            {
                var wField = new IntegerField("Cell Width");
                wField.value = _cellWidth;
                wField.RegisterValueChangedCallback(evt => {
                    _cellWidth = Mathf.Max(8, evt.newValue);
                    _canvasContainer.MarkDirtyLayout();
                });
                _controlPanel.Add(wField);

                var hField = new IntegerField("Cell Height");
                hField.value = _cellHeight;
                hField.RegisterValueChangedCallback(evt => {
                    _cellHeight = Mathf.Max(8, evt.newValue);
                    _canvasContainer.MarkDirtyLayout();
                });
                _controlPanel.Add(hField);
            }

            // Pivot Selection Controls
            var pivotHeader = new Label("Pivot Settings (For Slices)");
            pivotHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            pivotHeader.style.marginTop = 12;
            pivotHeader.style.marginBottom = 4;
            pivotHeader.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
            _controlPanel.Add(pivotHeader);

            var pivotField = new DropdownField("Alignment");
            pivotField.choices = new List<string> {
                "Center", "TopLeft", "TopCenter", "TopRight", "LeftCenter", "RightCenter", "BottomLeft", "BottomCenter", "BottomRight", "Custom"
            };
            pivotField.value = _pivotAlignment.ToString();
            pivotField.RegisterValueChangedCallback(evt => {
                _pivotAlignment = (SpriteAlignment)Enum.Parse(typeof(SpriteAlignment), evt.newValue);
                SwitchMode(EditMode.Splitter);
            });
            _controlPanel.Add(pivotField);

            if (_pivotAlignment == SpriteAlignment.Custom)
            {
                var customPivotField = new Vector2Field("Custom Offset");
                customPivotField.value = _customPivot;
                customPivotField.RegisterValueChangedCallback(evt => {
                    _customPivot = new Vector2(Mathf.Clamp01(evt.newValue.x), Mathf.Clamp01(evt.newValue.y));
                });
                _controlPanel.Add(customPivotField);
            }

            // Warning note
            var warningLabel = new Label("Original sheet remains untouched.\nSlices are saved as new individual PNG files.");
            warningLabel.style.fontSize = 9;
            warningLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            warningLabel.style.whiteSpace = WhiteSpace.Normal;
            warningLabel.style.marginTop = 8;
            _controlPanel.Add(warningLabel);

            // Actions Button
            var sliceBtn = new Button(OnSliceClicked) { text = "✂ Extract and Save Slices" };
            sliceBtn.style.marginTop = 8;
            sliceBtn.style.height = 32;
            sliceBtn.style.backgroundColor = new StyleColor(new Color(0.0f, 0.45f, 0.75f));
            sliceBtn.style.color = new StyleColor(Color.white);
            sliceBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            _controlPanel.Add(sliceBtn);
        }

        private void RenderCropperControls()
        {
            var header = new Label("Crop & Resize Parameters");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 8;
            header.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
            _controlPanel.Add(header);

            // Aspect Ratio constraints
            var aspectField = new DropdownField("Aspect Ratio Lock");
            aspectField.choices = new List<string> { "Free", "Square (1:1)", "Landscape (16:9)", "Portrait (9:16)" };
            aspectField.value = _aspectRatioString;
            aspectField.RegisterValueChangedCallback(evt => {
                _aspectRatioString = evt.newValue;
                if (_aspectRatioString == "Free")
                {
                    _lockAspectRatio = false;
                }
                else
                {
                    _lockAspectRatio = true;
                    if (_aspectRatioString == "Square (1:1)") _lockedAspectVal = 1f;
                    else if (_aspectRatioString == "Landscape (16:9)") _lockedAspectVal = 16f / 9f;
                    else if (_aspectRatioString == "Portrait (9:16)") _lockedAspectVal = 9f / 16f;
                    
                    EnforceCropAspect();
                }
                _canvasContainer.MarkDirtyLayout();
            });
            _controlPanel.Add(aspectField);

            // Dimensions/Resizing Settings
            var resizeHeader = new Label("Resize Output Dimensions");
            resizeHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            resizeHeader.style.marginTop = 12;
            resizeHeader.style.marginBottom = 4;
            resizeHeader.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
            _controlPanel.Add(resizeHeader);

            var wField = new IntegerField("Resize Width");
            wField.value = _resizeWidth;
            wField.RegisterValueChangedCallback(evt => {
                _resizeWidth = Mathf.Max(8, evt.newValue);
            });
            _controlPanel.Add(wField);

            var hField = new IntegerField("Resize Height");
            hField.value = _resizeHeight;
            hField.RegisterValueChangedCallback(evt => {
                _resizeHeight = Mathf.Max(8, evt.newValue);
            });
            _controlPanel.Add(hField);

            var filterField = new DropdownField("Scaling Filter");
            filterField.choices = new List<string> { "Bilinear Resampling", "Nearest Neighbor" };
            filterField.value = _useBilinear ? "Bilinear Resampling" : "Nearest Neighbor";
            filterField.RegisterValueChangedCallback(evt => {
                _useBilinear = evt.newValue == "Bilinear Resampling";
            });
            _controlPanel.Add(filterField);

            // Warning note
            var warningLabel = new Label("Original asset remains untouched.\nOutput is written as a new PNG file.");
            warningLabel.style.fontSize = 9;
            warningLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            warningLabel.style.whiteSpace = WhiteSpace.Normal;
            warningLabel.style.marginTop = 8;
            _controlPanel.Add(warningLabel);

            // Buttons
            var cropBtn = new Button(OnCropClicked) { text = "🖼 Crop to New Asset" };
            cropBtn.style.marginTop = 8;
            cropBtn.style.height = 28;
            cropBtn.style.backgroundColor = new StyleColor(new Color(0.2f, 0.55f, 0.3f));
            cropBtn.style.color = new StyleColor(Color.white);
            cropBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            _controlPanel.Add(cropBtn);

            var resizeBtn = new Button(OnResizeClicked) { text = "⚖ Scale to New Asset" };
            resizeBtn.style.marginTop = 8;
            resizeBtn.style.height = 28;
            resizeBtn.style.backgroundColor = new StyleColor(new Color(0.55f, 0.4f, 0.15f));
            resizeBtn.style.color = new StyleColor(Color.white);
            resizeBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            _controlPanel.Add(resizeBtn);
        }

        private void OnSliceClicked()
        {
            if (_targetTexture == null || string.IsNullOrEmpty(_assetPath))
            {
                EditorUtility.DisplayDialog("Error", "No texture loaded. Please select an image in the project hierarchy first.", "OK");
                return;
            }

            string outFolder = _outputPathField.value.Trim();
            if (string.IsNullOrEmpty(outFolder)) outFolder = "Assets/";

            int w = 64, h = 64;
            if (_slicingType == SlicingType.GridCount)
            {
                w = Mathf.Max(8, _targetTexture.width / _gridColumns);
                h = Mathf.Max(8, _targetTexture.height / _gridRows);
            }
            else
            {
                w = Mathf.Clamp(_cellWidth, 8, _targetTexture.width);
                h = Mathf.Clamp(_cellHeight, 8, _targetTexture.height);
            }

            try
            {
                SpriteSlicer.ExtractAndSaveSlices(_assetPath, outFolder, w, h, _pivotAlignment, _customPivot);
                EditorUtility.DisplayDialog("Success", $"Sprite sheet sliced successfully.\nExtracted frames saved to folder: {outFolder}", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error Slicing Sheet", ex.Message, "OK");
            }
        }

        private void OnCropClicked()
        {
            if (_targetTexture == null || string.IsNullOrEmpty(_assetPath))
            {
                EditorUtility.DisplayDialog("Error", "No texture loaded to crop.", "OK");
                return;
            }

            string outFolder = _outputPathField.value.Trim();
            if (string.IsNullOrEmpty(outFolder)) outFolder = "Assets/";

            int texW = _targetTexture.width;
            int texH = _targetTexture.height;

            int x = Mathf.Clamp((int)(_cropNormalized.x * texW), 0, texW - 8);
            int y = Mathf.Clamp((int)(_cropNormalized.y * texH), 0, texH - 8);
            int w = Mathf.Clamp((int)(_cropNormalized.width * texW), 8, texW - x);
            int h = Mathf.Clamp((int)(_cropNormalized.height * texH), 8, texH - y);

            RectInt cropRect = new RectInt(x, y, w, h);

            try
            {
                string croppedPath = CropTexture(_assetPath, outFolder, cropRect);
                EditorUtility.DisplayDialog("Success", $"Image cropped and saved as new file successfully:\n{croppedPath}", "OK");
                // Reset normalized crop
                _cropNormalized = new Rect(0.1f, 0.1f, 0.8f, 0.8f);
                if (_lockAspectRatio) EnforceCropAspect();
                Repaint();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error Cropping", ex.Message, "OK");
            }
        }

        private void OnResizeClicked()
        {
            if (_targetTexture == null || string.IsNullOrEmpty(_assetPath))
            {
                EditorUtility.DisplayDialog("Error", "No texture loaded to resize.", "OK");
                return;
            }

            string outFolder = _outputPathField.value.Trim();
            if (string.IsNullOrEmpty(outFolder)) outFolder = "Assets/";

            try
            {
                string resizedPath = ResizeTexture(_assetPath, outFolder, _resizeWidth, _resizeHeight, _useBilinear);
                EditorUtility.DisplayDialog("Success", $"Image scaled and saved as new file successfully:\n{resizedPath}", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error Resizing", ex.Message, "OK");
            }
        }

        private void EnforceCropAspect()
        {
            float w = _cropNormalized.width;
            float h = w / _lockedAspectVal;
            
            if (_cropNormalized.y + h > 1.0f)
            {
                h = 1.0f - _cropNormalized.y;
                w = h * _lockedAspectVal;
            }
            if (_cropNormalized.x + w > 1.0f)
            {
                w = 1.0f - _cropNormalized.x;
                h = w / _lockedAspectVal;
            }

            _cropNormalized.width = w;
            _cropNormalized.height = h;
        }

        private void DrawCanvas()
        {
            Rect containerRect = _canvasContainer.layout;
            if (containerRect.width <= 0 || containerRect.height <= 0) return;

            // Background canvas layout box
            GUI.Box(new Rect(0, 0, containerRect.width, containerRect.height), "", (GUIStyle)"CurveEditorBackground");

            if (_targetTexture == null)
            {
                var centeredStyle = new GUIStyle(EditorStyles.label);
                centeredStyle.alignment = TextAnchor.MiddleCenter;
                centeredStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
                GUI.Label(new Rect(0, 0, containerRect.width, containerRect.height), "No Texture Loaded.\nSelect an asset in the Project window or click 'Load Selected Texture'.", centeredStyle);
                return;
            }

            // Preserving image aspect ratio inside canvas layout bounds
            float canvasAspect = containerRect.width / containerRect.height;
            float imageAspect = (float)_targetTexture.width / _targetTexture.height;

            float drawWidth = containerRect.width;
            float drawHeight = containerRect.height;
            float drawX = 0;
            float drawY = 0;

            if (imageAspect > canvasAspect)
            {
                drawWidth = containerRect.width - 20;
                drawHeight = drawWidth / imageAspect;
                drawX = 10;
                drawY = (containerRect.height - drawHeight) / 2f;
            }
            else
            {
                drawHeight = containerRect.height - 20;
                drawWidth = drawHeight * imageAspect;
                drawY = 10;
                drawX = (containerRect.width - drawWidth) / 2f;
            }

            Rect textureScreenRect = new Rect(drawX, drawY, drawWidth, drawHeight);

            // Draw checkerboard background
            DrawCheckerboard(textureScreenRect);

            // Draw loaded texture
            GUI.DrawTexture(textureScreenRect, _targetTexture, ScaleMode.ScaleToFit);

            if (_currentMode == EditMode.Splitter)
            {
                DrawGridLines(textureScreenRect);
            }
            else if (_currentMode == EditMode.CropperResizer)
            {
                HandleCropInput(textureScreenRect);
                DrawCropOverlay(textureScreenRect);
            }

            // Draw border line around texture preview
            Handles.BeginGUI();
            Handles.color = new Color(0.25f, 0.25f, 0.25f, 0.8f);
            Handles.DrawSolidRectangleWithOutline(textureScreenRect, Color.clear, new Color(0.4f, 0.4f, 0.4f, 1f));
            Handles.EndGUI();
        }

        private void DrawCheckerboard(Rect rect)
        {
            Handles.BeginGUI();
            int squareSize = 10;
            int rows = Mathf.CeilToInt(rect.height / squareSize);
            int cols = Mathf.CeilToInt(rect.width / squareSize);

            Color colorA = new Color(0.22f, 0.22f, 0.22f, 1f);
            Color colorB = new Color(0.28f, 0.28f, 0.28f, 1f);

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float x = rect.x + (c * squareSize);
                    float y = rect.y + (r * squareSize);
                    float w = Mathf.Min(squareSize, rect.xMax - x);
                    float h = Mathf.Min(squareSize, rect.yMax - y);

                    Color sqColor = ((r + c) % 2 == 0) ? colorA : colorB;
                    EditorGUI.DrawRect(new Rect(x, y, w, h), sqColor);
                }
            }
            Handles.EndGUI();
        }

        private void DrawGridLines(Rect screenRect)
        {
            int cols = 2, rows = 2;
            if (_slicingType == SlicingType.GridCount)
            {
                cols = _gridColumns;
                rows = _gridRows;
            }
            else
            {
                cols = Mathf.Max(1, _targetTexture.width / _cellWidth);
                rows = Mathf.Max(1, _targetTexture.height / _cellHeight);
            }

            Handles.BeginGUI();
            Handles.color = new Color(0.0f, 0.64f, 0.96f, 0.5f); // Semi-transparent Cyan

            // Vertical Slicers
            for (int c = 1; c < cols; c++)
            {
                float x = screenRect.x + (screenRect.width * c / cols);
                Handles.DrawLine(new Vector2(x, screenRect.y), new Vector2(x, screenRect.yMax), 2f);
            }

            // Horizontal Slicers
            for (int r = 1; r < rows; r++)
            {
                float y = screenRect.y + (screenRect.height * r / rows);
                Handles.DrawLine(new Vector2(screenRect.x, y), new Vector2(screenRect.xMax, y), 2f);
            }

            // Draw Custom Pivot Previews in corner of cell if defined
            Vector2 pivotRatio = new Vector2(0.5f, 0.5f);
            if (_pivotAlignment == SpriteAlignment.TopLeft) pivotRatio = new Vector2(0f, 1f);
            else if (_pivotAlignment == SpriteAlignment.TopCenter) pivotRatio = new Vector2(0.5f, 1f);
            else if (_pivotAlignment == SpriteAlignment.TopRight) pivotRatio = new Vector2(1f, 1f);
            else if (_pivotAlignment == SpriteAlignment.LeftCenter) pivotRatio = new Vector2(0f, 0.5f);
            else if (_pivotAlignment == SpriteAlignment.RightCenter) pivotRatio = new Vector2(1f, 0.5f);
            else if (_pivotAlignment == SpriteAlignment.BottomLeft) pivotRatio = new Vector2(0f, 0f);
            else if (_pivotAlignment == SpriteAlignment.BottomCenter) pivotRatio = new Vector2(0.5f, 0f);
            else if (_pivotAlignment == SpriteAlignment.BottomRight) pivotRatio = new Vector2(1f, 0f);
            else if (_pivotAlignment == SpriteAlignment.Custom) pivotRatio = _customPivot;

            Handles.color = new Color(0.96f, 0.32f, 0.0f, 0.8f); // Orange for pivot preview points
            float cellWidthScreen = screenRect.width / cols;
            float cellHeightScreen = screenRect.height / rows;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float px = screenRect.x + (c * cellWidthScreen) + (pivotRatio.x * cellWidthScreen);
                    float py = screenRect.yMax - (r * cellHeightScreen) - (pivotRatio.y * cellHeightScreen);
                    
                    Handles.DrawSolidDisc(new Vector3(px, py, 0), Vector3.forward, 3f);
                }
            }

            Handles.EndGUI();
        }

        private void DrawCropOverlay(Rect screenRect)
        {
            Rect selectionRect = GetScreenCropRect(screenRect);

            Handles.BeginGUI();
            
            // Dim background mask
            Color maskColor = new Color(0.08f, 0.08f, 0.08f, 0.5f);
            EditorGUI.DrawRect(new Rect(screenRect.x, screenRect.y, screenRect.width, selectionRect.y - screenRect.y), maskColor);
            EditorGUI.DrawRect(new Rect(screenRect.x, selectionRect.yMax, screenRect.width, screenRect.yMax - selectionRect.yMax), maskColor);
            EditorGUI.DrawRect(new Rect(screenRect.x, selectionRect.y, selectionRect.x - screenRect.x, selectionRect.height), maskColor);
            EditorGUI.DrawRect(new Rect(selectionRect.xMax, selectionRect.y, screenRect.xMax - selectionRect.xMax, selectionRect.height), maskColor);

            // Crop border outline
            Handles.color = new Color(0.0f, 0.8f, 0.3f, 1f); // Vibrant Green
            Handles.DrawSolidRectangleWithOutline(selectionRect, Color.clear, new Color(0f, 0.8f, 0.3f, 1f));

            // Corner Resize handles
            DrawHandleBox(new Vector2(selectionRect.x, selectionRect.y));      // TL
            DrawHandleBox(new Vector2(selectionRect.xMax, selectionRect.y));   // TR
            DrawHandleBox(new Vector2(selectionRect.x, selectionRect.yMax));   // BL
            DrawHandleBox(new Vector2(selectionRect.xMax, selectionRect.yMax));// BR

            Handles.EndGUI();
        }

        private void DrawHandleBox(Vector2 pos)
        {
            Rect box = new Rect(pos.x - 4, pos.y - 4, 8, 8);
            EditorGUI.DrawRect(box, new Color(0f, 0.8f, 0.3f, 1f));
            Handles.DrawSolidRectangleWithOutline(box, new Color(0f, 0.8f, 0.3f, 1f), Color.white);
        }

        private Rect GetScreenCropRect(Rect screenRect)
        {
            return new Rect(
                screenRect.x + (_cropNormalized.x * screenRect.width),
                screenRect.y + ((1f - _cropNormalized.y - _cropNormalized.height) * screenRect.height),
                _cropNormalized.width * screenRect.width,
                _cropNormalized.height * screenRect.height
            );
        }

        private void HandleCropInput(Rect screenRect)
        {
            Event e = Event.current;
            Vector2 mousePos = e.mousePosition;

            Rect currentScreenCrop = GetScreenCropRect(screenRect);
            float handleThreshold = 8f;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                Vector2 tl = new Vector2(currentScreenCrop.x, currentScreenCrop.y);
                Vector2 tr = new Vector2(currentScreenCrop.xMax, currentScreenCrop.y);
                Vector2 bl = new Vector2(currentScreenCrop.x, currentScreenCrop.yMax);
                Vector2 br = new Vector2(currentScreenCrop.xMax, currentScreenCrop.yMax);

                if (Vector2.Distance(mousePos, tl) < handleThreshold) _dragState = DragState.TopLeft;
                else if (Vector2.Distance(mousePos, tr) < handleThreshold) _dragState = DragState.TopRight;
                else if (Vector2.Distance(mousePos, bl) < handleThreshold) _dragState = DragState.BottomLeft;
                else if (Vector2.Distance(mousePos, br) < handleThreshold) _dragState = DragState.BottomRight;
                else if (currentScreenCrop.Contains(mousePos)) _dragState = DragState.Move;

                if (_dragState != DragState.None)
                {
                    _dragStartMousePos = mousePos;
                    _dragStartCropRect = _cropNormalized;
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseDrag && _dragState != DragState.None)
            {
                float deltaXNorm = (mousePos.x - _dragStartMousePos.x) / screenRect.width;
                float deltaYNorm = -(mousePos.y - _dragStartMousePos.y) / screenRect.height;

                Rect nextCrop = _dragStartCropRect;

                switch (_dragState)
                {
                    case DragState.Move:
                        nextCrop.x = Mathf.Clamp(nextCrop.x + deltaXNorm, 0f, 1f - nextCrop.width);
                        nextCrop.y = Mathf.Clamp(nextCrop.y + deltaYNorm, 0f, 1f - nextCrop.height);
                        break;
                    case DragState.TopLeft:
                        float newYMaxTL = _dragStartCropRect.yMax + deltaYNorm;
                        float newXTL = _dragStartCropRect.x + deltaXNorm;
                        newYMaxTL = Mathf.Clamp(newYMaxTL, _dragStartCropRect.y + 0.05f, 1f);
                        newXTL = Mathf.Clamp(newXTL, 0f, _dragStartCropRect.xMax - 0.05f);

                        nextCrop.x = newXTL;
                        nextCrop.width = _dragStartCropRect.xMax - newXTL;
                        nextCrop.height = newYMaxTL - nextCrop.y;
                        break;
                    case DragState.TopRight:
                        float newYMaxTR = _dragStartCropRect.yMax + deltaYNorm;
                        float newXMaxTR = _dragStartCropRect.xMax + deltaXNorm;
                        newYMaxTR = Mathf.Clamp(newYMaxTR, _dragStartCropRect.y + 0.05f, 1f);
                        newXMaxTR = Mathf.Clamp(newXMaxTR, _dragStartCropRect.x + 0.05f, 1f);

                        nextCrop.width = newXMaxTR - nextCrop.x;
                        nextCrop.height = newYMaxTR - nextCrop.y;
                        break;
                    case DragState.BottomLeft:
                        float newYBL = _dragStartCropRect.y + deltaYNorm;
                        float newXBL = _dragStartCropRect.x + deltaXNorm;
                        newYBL = Mathf.Clamp(newYBL, 0f, _dragStartCropRect.yMax - 0.05f);
                        newXBL = Mathf.Clamp(newXBL, 0f, _dragStartCropRect.xMax - 0.05f);

                        nextCrop.x = newXBL;
                        nextCrop.width = _dragStartCropRect.xMax - newXBL;
                        nextCrop.y = newYBL;
                        nextCrop.height = _dragStartCropRect.yMax - newYBL;
                        break;
                    case DragState.BottomRight:
                        float newYBR = _dragStartCropRect.y + deltaYNorm;
                        float newXMaxBR = _dragStartCropRect.xMax + deltaXNorm;
                        newYBR = Mathf.Clamp(newYBR, 0f, _dragStartCropRect.yMax - 0.05f);
                        newXMaxBR = Mathf.Clamp(newXMaxBR, _dragStartCropRect.x + 0.05f, 1f);

                        nextCrop.width = newXMaxBR - nextCrop.x;
                        nextCrop.y = newYBR;
                        nextCrop.height = _dragStartCropRect.yMax - newYBR;
                        break;
                }

                _cropNormalized = nextCrop;
                if (_lockAspectRatio) EnforceCropAspect();
                e.Use();
                _canvasContainer.MarkDirtyLayout();
            }
            else if (e.type == EventType.MouseUp)
            {
                _dragState = DragState.None;
            }
        }

        // --- Core File Copy-Editing and Importer Methods ---

        private static string CropTexture(string sourcePath, string outputFolder, RectInt cropRect)
        {
            TextureImporter importer = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
            bool wasReadable = false;
            if (importer != null)
            {
                wasReadable = importer.isReadable;
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            if (texture == null)
            {
                throw new Exception("Unable to load source texture asset for cropping.");
            }

            Color[] pixels = texture.GetPixels(cropRect.x, cropRect.y, cropRect.width, cropRect.height);

            Texture2D croppedTexture = new Texture2D(cropRect.width, cropRect.height);
            croppedTexture.SetPixels(pixels);
            croppedTexture.Apply();

            byte[] bytes = croppedTexture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(croppedTexture);

            // Construct new path inside target output directory
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            string fileName = $"{baseName}_cropped.png";
            string relativePath = Path.Combine(outputFolder, fileName).Replace("\\", "/");
            
            string absoluteOutDir = outputFolder;
            if (outputFolder.StartsWith("Assets"))
            {
                absoluteOutDir = Path.Combine(Application.dataPath, "..", outputFolder);
            }
            if (!Directory.Exists(absoluteOutDir))
            {
                Directory.CreateDirectory(absoluteOutDir);
            }
            string absolutePath = Path.Combine(absoluteOutDir, fileName);

            Debug.Log($"[Omnisense-ImgEditor] Creating cropped copy at: {absolutePath}");
            File.WriteAllBytes(absolutePath, bytes);

            if (importer != null)
            {
                importer.isReadable = wasReadable;
                importer.SaveAndReimport();
            }

            AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            // Match importer settings to source asset
            TextureImporter newImporter = AssetImporter.GetAtPath(relativePath) as TextureImporter;
            if (newImporter != null && importer != null)
            {
                newImporter.textureType = importer.textureType;
                newImporter.spriteImportMode = SpriteImportMode.Single;
                newImporter.SaveAndReimport();
            }

            Debug.Log($"[OmniSense] Saved cropped asset at: {relativePath}");
            return relativePath;
        }

        private static string ResizeTexture(string sourcePath, string outputFolder, int targetWidth, int targetHeight, bool useBilinear)
        {
            TextureImporter importer = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
            bool wasReadable = false;
            if (importer != null)
            {
                wasReadable = importer.isReadable;
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            Texture2D original = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            if (original == null)
            {
                throw new Exception("Unable to load source texture asset for scaling.");
            }

            Texture2D resized = new Texture2D(targetWidth, targetHeight);
            
            if (useBilinear)
            {
                RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight);
                RenderTexture.active = rt;
                Graphics.Blit(original, rt);
                resized.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                resized.Apply();
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
            else
            {
                float scaleX = (float)original.width / targetWidth;
                float scaleY = (float)original.height / targetHeight;

                for (int y = 0; y < targetHeight; y++)
                {
                    for (int x = 0; x < targetWidth; x++)
                    {
                        int origX = Mathf.FloorToInt(x * scaleX);
                        int origY = Mathf.FloorToInt(y * scaleY);
                        resized.SetPixel(x, y, original.GetPixel(origX, origY));
                    }
                }
                resized.Apply();
            }

            byte[] bytes = resized.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(resized);

            // Construct new path inside target output directory
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            string fileName = $"{baseName}_resized.png";
            string relativePath = Path.Combine(outputFolder, fileName).Replace("\\", "/");
            
            string absoluteOutDir = outputFolder;
            if (outputFolder.StartsWith("Assets"))
            {
                absoluteOutDir = Path.Combine(Application.dataPath, "..", outputFolder);
            }
            if (!Directory.Exists(absoluteOutDir))
            {
                Directory.CreateDirectory(absoluteOutDir);
            }
            string absolutePath = Path.Combine(absoluteOutDir, fileName);

            Debug.Log($"[Omnisense-ImgEditor] Creating scaled copy at: {absolutePath}");
            File.WriteAllBytes(absolutePath, bytes);

            if (importer != null)
            {
                importer.isReadable = wasReadable;
                importer.SaveAndReimport();
            }

            AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            TextureImporter newImporter = AssetImporter.GetAtPath(relativePath) as TextureImporter;
            if (newImporter != null && importer != null)
            {
                newImporter.textureType = importer.textureType;
                newImporter.spriteImportMode = SpriteImportMode.Single;
                newImporter.SaveAndReimport();
            }

            Debug.Log($"[OmniSense] Saved scaled asset at: {relativePath}");
            return relativePath;
        }
    }

    public static class SpriteSlicer
    {
        public static void ExtractAndSaveSlices(string sourcePath, string outputFolder, int sliceWidth, int sliceHeight, SpriteAlignment alignment, Vector2 customPivot)
        {
            TextureImporter importer = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
            bool wasReadable = false;
            if (importer != null)
            {
                wasReadable = importer.isReadable;
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            if (texture == null)
            {
                throw new Exception("Unable to load texture asset for slicing.");
            }
            
            int texWidth = texture.width;
            int texHeight = texture.height;
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);

            // Ensure target directory exists
            string absoluteOutDir = outputFolder;
            if (outputFolder.StartsWith("Assets"))
            {
                absoluteOutDir = Path.Combine(Application.dataPath, "..", outputFolder);
            }
            if (!Directory.Exists(absoluteOutDir))
            {
                Directory.CreateDirectory(absoluteOutDir);
            }

            List<string> generatedPaths = new List<string>();
            int index = 0;

            for (int y = texHeight - sliceHeight; y >= 0; y -= sliceHeight)
            {
                for (int x = 0; x < texWidth; x += sliceWidth)
                {
                    int currentWidth = Mathf.Min(sliceWidth, texWidth - x);
                    int currentHeight = Mathf.Min(sliceHeight, texHeight - y);

                    Color[] cellPixels = texture.GetPixels(x, y, currentWidth, currentHeight);
                    Texture2D cellTex = new Texture2D(currentWidth, currentHeight);
                    cellTex.SetPixels(cellPixels);
                    cellTex.Apply();

                    byte[] pngBytes = cellTex.EncodeToPNG();
                    UnityEngine.Object.DestroyImmediate(cellTex);

                    string fileName = $"{baseName}_slice_{index++}.png";
                    string relativePath = Path.Combine(outputFolder, fileName).Replace("\\", "/");
                    string absolutePath = Path.Combine(absoluteOutDir, fileName);

                    File.WriteAllBytes(absolutePath, pngBytes);
                    generatedPaths.Add(relativePath);
                }
            }

            // Restore source read settings
            if (importer != null)
            {
                importer.isReadable = wasReadable;
                importer.SaveAndReimport();
            }

            // Batch import generated slices
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in generatedPaths)
                {
                    AssetDatabase.ImportAsset(path);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.Refresh();

            // Set importer alignment & pivot settings on each individual slice
            foreach (var path in generatedPaths)
            {
                TextureImporter cellImporter = AssetImporter.GetAtPath(path) as TextureImporter;
                if (cellImporter != null)
                {
                    cellImporter.textureType = TextureImporterType.Sprite;
                    cellImporter.spriteImportMode = SpriteImportMode.Single;
                    
                    TextureImporterSettings settings = new TextureImporterSettings();
                    cellImporter.ReadTextureSettings(settings);
                    settings.spriteAlignment = (int)alignment;
                    if (alignment == SpriteAlignment.Custom)
                    {
                        settings.spritePivot = customPivot;
                    }
                    cellImporter.SetTextureSettings(settings);
                    
                    cellImporter.SaveAndReimport();
                }
            }

            Debug.Log($"[OmniSense] Successfully extracted {index} slices from {sourcePath} into directory: {outputFolder}.");
        }
    }
}
