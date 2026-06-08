# OmniSense: Simple Image Generation Feature Plan

This document outlines the simplified, user-triggered Image Generation feature inside the OmniSense Unity Editor.

---

## 1. Core Workflow

1. **Activation**: The user clicks the **Image Generation Button** (a graphical icon like a painting palette `🎨` or image placeholder `🖼️`) located at the bottom of the main chat window, right next to the existing paperclip/attachment icon.
2. **Popup Modal**: Clicking the button opens a modal popup dialog window (`ImageGenerationPopup`).
3. **Configuration & Execution**: The user edits size/path parameters if needed, chooses a provider and style, enters their custom prompt, and clicks **Generate**.
4. **Asset Creation**: The generated image is downloaded, saved to the specified path, and imported into the project immediately.

---

## 2. Popup UI Specifications

The image generation popup is designed to be minimal and functional. It contains the following elements:

* **Dimensions (Size)**:
  * Two input fields for Width and Height.
  * **Default**: `1024` x `1024`.
  * **Behavior**: Fully editable by the user to customize the aspect ratio or resolution.
* **Storage Path**:
  * An input text field specifying where to save the generated images.
  * **Default**: The project's root directory (`Assets/`).
  * **Persistence**: Every time the user changes this path, it is saved in **PlayerPrefs** (`PlayerPrefs.SetString(...)` / `PlayerPrefs.Save()`). When opening the popup, it loads the last saved path so the user does not have to keep re-entering it.
* **Provider**:
  * A dropdown menu to select the AI image provider backend (e.g., Google Imagen, DALL-E, etc.).
* **Style**:
  * A dropdown menu to select prompt style presets (e.g., Pixel Art, Stylized, 2D Platformer, No Style).
  * **Default**: `No Style`.
  * **Behavior**: 
    * If `No Style` is selected, nothing is attached to the prompt.
    * If a style is selected, the corresponding style suffix (e.g., `, pixel art style, 2d game sprite, clean background`) is automatically appended to the user's prompt before sending the request.
* **Prompt**:
  * A multi-line text field where the user inputs their description.
* **Generate Button**:
  * A button at the bottom of the popup. Clicking it triggers the generation API call, displays a loading spinner/status text, downloads the asset, saves it, and refreshes the asset database.

---

## 3. Implementation Tasks

### UI Elements (`OmnisenseWindow.uxml` & USS)
* Add a button next to the paperclip attachment icon using a clean icon.
* Create a simple VisualElement popup or `EditorWindow` popup containing the input fields (Width, Height, Path, Dropdowns, Prompt, Generate Button).

### Storage & Persistence
* Read and write the target storage path using Unity's `PlayerPrefs` (`PlayerPrefs.GetString` and `PlayerPrefs.SetString`).

### Style Append Engine
* Implement a simple dictionary/method that maps selected dropdown styles to prompt suffix strings (e.g., `Pixel Art` maps to `, pixel art, clean background, 2d sprite`).
* Default selection `No Style` returns an empty string suffix.

### Image Download & Asset Import
* **API Invocations**: Execute HTTP POST requests to the selected provider's REST endpoints (e.g., OpenAI DALL-E, Google Imagen) using `UnityWebRequest`. Note that since Grok Imagine lacks a public REST API, we will utilize the xAI API if available or default to other active providers.
* **Saving Bytes**: Download the generated image payload as a byte array.
* **Folder Creation**: Verify if the user-specified directory exists; if not, create it dynamically before saving.
* **Asset Database Sync**: Save the image as a `.png` file at the target path, then immediately call `AssetImporter` and `AssetDatabase.ImportAsset(localPath)` followed by `AssetDatabase.Refresh()` to register it in Unity.

### Error Handling & Validation
* **Failure Responses**: Handle network timeouts, rate limit errors (HTTP 429), API authentication issues, and invalid prompt content blocks.
* **UI Feedback**: Display clear, user-friendly error messages on a dedicated status/error label in the popup UI so the user knows exactly why a generation attempt failed.
