# OmniSense: Game-Dev Aware Image Generation Feature Implementation Plan

This document outlines the design, API selection, architecture, and step-by-step roadmap for implementing a game-aware Image Generation system inside the OmniSense Unity Editor Plugin.

---

## 1. Executive Summary & Core Objective

The goal is to integrate a game-development-aware image generation feature into the OmniSense agentic pipeline. Instead of a generic prompt-to-image interface, the tool is designed to be **context-aware**, enabling both AI agents (during task execution) and game developers (via the Unity Editor UI) to instantly generate, import, and configure game-ready textures, sprites, UI icons, and concept art.

### Core Value Pillars
* **Seamless Asset Import**: Automatically imports generated assets directly into the Unity project (`Assets/Generated/...`) with correct import settings (e.g. Sprite vs. Default Texture).
* **Agent-Driven Generation**: Allows the worker agents (e.g., UI Specialist, 2D Modeler) to programmatically call `generate_image` when requested to build UI panels or construct 2D game scenes.
* **Game-Dev Context Engine**: Enforces pre-defined styles and formats (Pixel Art, UI Flat, PBR textures) through parameterization rather than raw prompting alone.

---

## 2. Standard Game Dev Use Cases

| Category | Target Asset Type | Best Styles | Unity Import Configuration |
| :--- | :--- | :--- | :--- |
| **2D Game Asset** | Character, Enemy, Prop Sprites | Pixel Art, Hand-Drawn, Retro, Anime | TextureType: `Sprite (2D and UI)`, Sprite Mode: `Single/Multiple` |
| **2D Tilemap** | Environment Tiles, Tilesets | Isometric, Orthogonal Pixel Art, Painterly | Sprite Mode: `Multiple`, Filter Mode: `Point` (for crisp pixels) |
| **UI System** | Ability, Item, Skill, HUD Icons | Flat, Minimalist, Glossy/Glassmorphism | TextureType: `Sprite (2D and UI)`, Alpha Source: `Input Texture Alpha` |
| **3D Game Asset** | Diffuse, Normal, Roughness Maps | PBR Realistic, Stylized Hand-painted | TextureType: `Default` or `Normal Map`, Wrap Mode: `Repeat` (Tileable) |
| **Concept Art** | Character Sheets, Environments | Sketch, Digital Painting, Cinematic | TextureType: `Default`, Max Size: `2048` |

---

## 3. API Backend Capabilities & Comparison

We will support multiple API backends to give developers flexibility in quality, cost, and rate-limiting profiles.

| Feature | OpenAI (DALL·E 3) | Google (Imagen 3) | xAI (Grok Imagine) |
| :--- | :--- | : :--- | :--- |
| **Quality & Styling** | Outstanding (Highest semantic compliance) | Very Good (Photo & Artistic) | Excellent (Creative & Atmospheric) |
| **Size/Aspect Control** | Precise (`1024x1024`, `1792x1024`, `1024x1792`) | Selectable aspect ratios | Aspect ratio presets only (`landscape`, `portrait`) |
| **Billing Model** | Fixed cost per image | Fixed cost per image | Rate-limit/subscription-based |
| **Privacy / Safety** | Strict moderation filters | Strict moderation filters | Standard moderation |

---

## 4. MCP Tool Specification

We will expose image generation to the agentic network via a new MCP tool. This tool handles prompting, download, directory setup, and native Unity asset importing.

```json
{
  "method": "project/generate_image",
  "params": {
    "prompt": "A cyberpunk street vendor sprite, side view, clean background",
    "purpose": "sprite_2d" | "tileset_2d" | "ui_icon" | "texture_3d" | "concept_art",
    "style": "pixel_art" | "hand_painted" | "realistic" | "clean_flat" | "cinematic",
    "aspect_ratio": "square" | "portrait" | "landscape",
    "targetPath": "Assets/Sprites/Characters/Vendor.png"
  }
}
```

### Tool Parameters & Backend Processing
1. **Prompt Sanitization**: The tool handler appends semantic style keywords automatically based on `purpose` and `style` to maximize generation quality (e.g. adding `"pixel art, flat color, sprite sheets"` or `"tileable seamless texture, high-detail normal map template"`).
2. **Download & Serialization**: Downloads the generated image bytes asynchronously using `UnityWebRequestTexture`.
3. **Asset Import Settings Assignment**: Once the file is written to the `targetPath`, the tool queries `AssetImporter` and programmatically sets appropriate presets (Texture Type, Filter Mode, Sprite Mode) based on the asset's `purpose`.

---

## 5. Unity Editor UI/UX Design

The user interface will be built inside the existing `OmnisenseWindow` using Unity UI Toolkit:

```
+--------------------------------------------------------------+
| [ Chat ]  [ Settings ]  [ Image Generator ]                  |
+--------------------------------------------------------------+
| Prompt: [ A medieval stone castle door sprite...           ] |
|                                                              |
| Purpose:  (o) Sprite 2D   ( ) UI Icon   ( ) 3D Texture       |
| Style:    ( ) Pixel Art   (o) Hand-Painted   ( ) Realistic   |
| Size:     ( ) Square      (o) Landscape      ( ) Portrait    |
|                                                              |
| Import Directory: [ Assets/Generated/Sprites/              ] |
|                                                              |
|                      [ Generate Asset ]                      |
+--------------------------------------------------------------+
| Gallery / History:                                           |
| +-----------------+  +-----------------+  +-----------------+ |
| | [Image Preview] |  | [Image Preview] |  | [Image Preview] | |
| | Door_A.png      |  | Castle_Wall.png |  | Key_Icon.png    | |
| | [Apply to Scene]|  | [Apply to Scene]|  | [Apply to Scene]| |
| +-----------------+  +-----------------+  +-----------------+ |
+--------------------------------------------------------------+
```

### UI Features
* **Live Gallery Grid**: Displays thumbnail previews of generated assets inside the Unity Editor.
* **Quick Apply**: A button to instantly instantiate the generated image as a `SpriteRenderer` in the scene or assign it to the selected object's material.
* **Progress Bar**: An animated loader tracking the download progress of the asset.

---

## 6. Implementation Roadmap

### Phase 1: Tool Registry Setup (MCP Backend)
1. Add `project/generate_image` schema and documentation inside `PromptLibrary.SHARED_MCP_TOOLS`.
2. Map the tool case inside `ToolDispatcher.cs` and categorize it under `ApprovalMode.Deferred` (since it creates files).
3. Implement `MCPToolRegistry.GenerateImage()`:
   * Perform HTTP calls to the selected API provider using `LLMProviders.cs` client mechanisms.
   * Save the image bytes to `targetPath`.
   * Configure Unity's `TextureImporter` settings dynamically.

### Phase 2: Agent System Prompts Alignment
1. Update worker prompts (`GENERIC_WORKER` and `NATIVE_2D_MODELER`) so they know they can use `project/generate_image` to dynamically create custom graphic elements rather than leaving them blank.
2. Teach the `PLANNER` to include an image-generation sub-task when users ask for custom visuals/placeholders.

### Phase 3: Editor Panel & Gallery Development
1. Create `ImageGeneratorTab` inside `OmnisenseWindow.uxml` and style it with modern USS.
2. Implement user-triggered generation logic and save asset settings in `EditorPrefs`.
3. Add a simple local database cache to index previously generated assets.
