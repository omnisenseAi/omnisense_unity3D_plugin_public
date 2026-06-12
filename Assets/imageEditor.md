# OmniSense: Manual Sprite & Image Editor Suite

This document outlines the detailed architecture and implementation plan for the manual game-development productivity tools integrated directly into the Unity Editor. It focuses exclusively on manual, developer-driven interactions to ensure a robust foundation before incorporating AI automation.

---

## 1. Vision & Strategy

Developers routinely need to slice, crop, resize, and edit pivot points of 2D assets inside Unity. The **OmniSense Image Editor Suite** provides a lightweight, integrated Editor Window that handles these adjustments natively. 

This plan implements a **manual-first, copy-safe approach** to lay a stable foundation:
- **Zero Destructive Actions**: Under no circumstances is the original texture file modified. 
- **Non-Destructive Slicing**: Instead of modifying the original texture's importer properties, slicing extracts cell rects as individual, single-sprite PNG files.
- **Save Path Selector**: Enables users to configure the output target path where all newly generated slices, crops, or resized copies are written.

All AI-driven auto-slicing and MCP tools are decoupled and documented separately in [imageEditorAIHandoff.md](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/imageEditorAIHandoff.md).

> [!NOTE]
> All visual assets, canvases, and overlays are styled to inherit the premium dark-theme stylesheet settings defined in [OmnisenseWindow.uss](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/Editor/Omnisense/OmnisenseWindow.uss).

---

## 2. Core Manual Features (v1.0)

### A. Grid Sprite Sheet Splitter
Extracts cells of a sprite sheet and writes them as new, standalone PNG image files.
* **Grid Layout Slicing**:
  * Input parameters: Rows & Columns (e.g. 4x4) or absolute Sprite Dimensions (e.g. 64x64, 128x128).
  * Automatically calculates bounding rectangles for each grid cell.
* **Sprite Pivot settings**:
  * Applies standard pivot presets (Center, BottomCenter, TopLeft, etc.) or custom relative offsets.
  * Commits these parameters to each newly exported slice asset via `TextureImporterSettings` on import.
* **Slices Output Generation**:
  * Loops through grid cells, copies texture pixel arrays, and saves files as `{TargetFolder}/{OriginalName}_slice_{index}.png`.

### B. Quick Image Cropper & Resizer
Creates a cropped or resized copy of the target asset.
* **Visual Bounding Handles**: Interactive rect boundaries overlaid on the image canvas.
* **Aspect Ratio Constraint**: Locks cropping shapes to target formats (`1:1`, `4:3`, `16:9`, `Free`).
* **Resampling Options**: Bilinear or Nearest Neighbor pixel resizing.
* **Output Copy Generation**:
  * Cropped image is saved as `{TargetFolder}/{OriginalName}_cropped.png`.
  * Resized image is saved as `{TargetFolder}/{OriginalName}_resized.png`.

### C. Smart Slicing (Deferred)
* **Smart Alpha-Island Detection is deferred to Phase 2** and is documented in [imageEditorAIHandoff.md](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/imageEditorAIHandoff.md).

---

## 3. UI/UX Architecture

The suite is implemented as a custom editor window (`OmnisenseImageEditorWindow`). 

### Window Initialization
The editor window is registered in the Unity menu bar under:
`Omnisense > Image Editor` and `Window > Omnisense > Image Editor`

Alternatively, you can right-click any image/texture asset directly in the Project tab and select:
`Omnisense > Image Editor`

### Asset Ingestion Methods
Images can be loaded into the workspace in three ways:
1. **Context/Selection Tracking**: Click **🔄 Load Selected Texture** to import the currently highlighted Project view asset.
2. **Drag & Drop**: Drag and drop any texture asset directly onto the editor window canvas area.
3. **Target Image Selector**: Click the browse button (`...`) next to the **Target Image Asset** text field to open a project file dialog and choose an image.

```mermaid
graph TD
    A[Image Generated / Selected] -->|Click 'Open in Image Editor'<br/>(Manual Convenience Link Only)| B(Omnisense Image Editor Window)
    B --> C[Configure Output Folder / Save Path]
    C --> D{Select Operation}
    D -->|Split Sheet| E[Sprite Splitter Canvas]
    D -->|Crop / Resize| F[Resampling Canvas]
    E -->|Adjust Grid & Pivots| G[Apply Slices]
    F -->|Drag Bounding Rect| H[Crop / Scale]
    G -->|Extract PNG Cells| I[Write Slices to Output Folder]
    H -->|Resize Texture Bytes| J[Write New Asset Copy to Output Folder]
    I --> K[Sync TextureImporterSettings]
    J --> K
```

> [!IMPORTANT]
> **Convenience Workflow Note**: The hand-off link shown in the popup does not perform any automatic AI formatting or dispatching; it is purely a UI convenience shortcut that loads the target file path into the manual editor window.

### UI Components (UI Toolkit & IMGUI)
* **Left Sidebar**: 
  * Select mode (Splitter vs. Cropper & Resizer).
  * Load selected texture button.
  * **Output Directory Field**: A text input field showing the target output path (defaults to the loaded texture's directory) with a `...` browse button to select a target project folder.
  * Parameter editor inspector (Width, Height, Rows, Columns, Pivots).
* **Center Canvas Workspace**: A dynamic container displaying the target image using a scrollable visual container. Grid lines (`#007acc`) or crop selection borders are rendered over the texture.

---

## 4. Technical Design & Implementation Code

### A. Copy-Based Sprite Grid Extraction
```csharp
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
    int texWidth = texture.width;
    int texHeight = texture.height;
    string baseName = Path.GetFileNameWithoutExtension(sourcePath);

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
            Color[] cellPixels = texture.GetPixels(x, y, sliceWidth, sliceHeight);
            Texture2D cellTex = new Texture2D(sliceWidth, sliceHeight);
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

    if (importer != null)
    {
        importer.isReadable = wasReadable;
        importer.SaveAndReimport();
    }

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
}
```

### B. Sprite Pivot Setting Details
When slicing and creating single sprite assets, pivots are assigned via `TextureImporterSettings` fields:
1. `spriteAlignment`: Mapped to standard preset alignments (Center, BottomCenter, Custom).
2. `spritePivot`: A normalized `Vector2(0..1, 0..1)` offset relative to the single sprite cell boundaries (evaluated only if alignment is set to Custom).

### C. Copy-Based Canvas Image Cropping & Resizer
```csharp
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
    Color[] pixels = texture.GetPixels(cropRect.x, cropRect.y, cropRect.width, cropRect.height);

    Texture2D croppedTexture = new Texture2D(cropRect.width, cropRect.height);
    croppedTexture.SetPixels(pixels);
    croppedTexture.Apply();

    byte[] bytes = croppedTexture.EncodeToPNG();
    UnityEngine.Object.DestroyImmediate(croppedTexture);

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

    return relativePath;
}
```

---

## 5. Timeline & Milestones (v1.0)

* **Milestone 1: Editor Window Frame & Selection Hand-off** (2 Days)
  * Set up `OmnisenseImageEditorWindow.cs` with the menu command item paths (`Omnisense > Image Editor` and `Window > Omnisense > Image Editor`).
  * Connect a manual convenience link button in the [ImageGenerationPopup.cs](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/Editor/Omnisense/ImageGenerationPopup.cs) UI to open the editor window.
  * Integrate path selector text fields and folder browse interfaces in the sidebar.
* **Milestone 2: Slices Extraction & Importer Settings** (2 Days)
  * Implement coordinate preview guides overlay inside the visual canvas.
  * Program grid calculation math, cell extraction routines, and `TextureImporterSettings` custom pivot setups.
* **Milestone 3: Visual Cropping Bounds UI & Resampler Copy-generator** (2 Days)
  * Build interactive crop bounding UI canvas using mouse drag event listeners.
  * Support aspect ratio locks, resampling algorithms, and save as copy methods.
