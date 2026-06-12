using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Omnisense
{
    public static class CombatUISceneFixer
    {
        public static void FixCombatUI()
        {
            Debug.Log("[CombatUISceneFixer] Starting scene fixes...");

            // 1. Ensure the active scene is OutdoorsScene.unity
            string scenePath = "Assets/OutdoorsScene.unity";
            var currentScene = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(currentScene.path) || !currentScene.path.Equals(scenePath, StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[CombatUISceneFixer] Opening scene: {scenePath}");
                currentScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }

            // 2. Locate CombatHUD in the loaded scene
            GameObject combatHUD = FindGameObjectDeep("CombatHUD");
            if (combatHUD == null)
            {
                Debug.LogWarning("[CombatUISceneFixer] CombatHUD GameObject not found in the scene! Creating a new one for testing...");
                combatHUD = CreateBaseCombatHUD();
            }

            Debug.Log($"[CombatUISceneFixer] Found CombatHUD at: {GetGameObjectPath(combatHUD)}");

            // 3. Fix PlayerPanel and EnemyPanel
            FixPanel(combatHUD, "PlayerPanel", "PlayerName", "PlayerHPBar");
            FixPanel(combatHUD, "EnemyPanel", "EnemyName", "EnemyHPBar");

            // 4. Fix ActionPanel choice elements (Buttons to Texts)
            FixActionPanel(combatHUD);

            // 5. Add and wire up CombatManager component on CombatHUD
            WireCombatManager(combatHUD);

            // 6. Save the scene
            EditorSceneManager.MarkSceneDirty(currentScene);
            bool saveResult = EditorSceneManager.SaveScene(currentScene);
            Debug.Log($"[CombatUISceneFixer] Scene fixes completed. Save Successful: {saveResult}");
        }

        private static GameObject FindGameObjectDeep(string name)
        {
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var root in roots)
            {
                var target = FindChildRecursive(root.transform, name);
                if (target != null) return target.gameObject;
            }
            return null;
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            if (string.Equals(parent.name, name, StringComparison.OrdinalIgnoreCase)) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var result = FindChildRecursive(parent.GetChild(i), name);
                if (result != null) return result;
            }
            return null;
        }

        private static string GetGameObjectPath(GameObject obj)
        {
            string path = obj.name;
            Transform current = obj.transform;
            while (current.parent != null)
            {
                current = current.parent;
                path = current.name + "/" + path;
            }
            return path;
        }

        private static GameObject CreateBaseCombatHUD()
        {
            GameObject hud = new GameObject("CombatHUD");
            Canvas canvas = hud.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            hud.AddComponent<CanvasScaler>();
            hud.AddComponent<GraphicRaycaster>();

            // Create subpanels
            CreateEmptyPanel(hud.transform, "PlayerPanel");
            CreateEmptyPanel(hud.transform, "EnemyPanel");
            CreateEmptyPanel(hud.transform, "ActionPanel");

            return hud;
        }

        private static void CreateEmptyPanel(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
        }

        private static void FixPanel(GameObject combatHUD, string panelName, string nameNodeName, string hpBarNodeName)
        {
            Transform panel = FindChildRecursive(combatHUD.transform, panelName);
            if (panel == null)
            {
                Debug.LogWarning($"[CombatUISceneFixer] {panelName} not found. Creating it.");
                GameObject panelGo = new GameObject(panelName);
                panelGo.transform.SetParent(combatHUD.transform, false);
                panel = panelGo.transform;
            }

            // Gather all children first
            List<Transform> children = new List<Transform>();
            for (int i = 0; i < panel.childCount; i++)
            {
                children.Add(panel.GetChild(i));
            }

            Transform nameNode = null;
            Transform hpBarNode = null;

            foreach (var child in children)
            {
                if (string.Equals(child.name, nameNodeName, StringComparison.OrdinalIgnoreCase))
                {
                    if (nameNode == null)
                    {
                        nameNode = child;
                    }
                    else
                    {
                        Debug.Log($"[CombatUISceneFixer] Deleting duplicate child {child.name} under {panelName}");
                        Undo.DestroyObjectImmediate(child.gameObject);
                    }
                }
                else if (string.Equals(child.name, hpBarNodeName, StringComparison.OrdinalIgnoreCase))
                {
                    if (hpBarNode == null)
                    {
                        hpBarNode = child;
                    }
                    else
                    {
                        Debug.Log($"[CombatUISceneFixer] Deleting duplicate child {child.name} under {panelName}");
                        Undo.DestroyObjectImmediate(child.gameObject);
                    }
                }
            }

            // If the nodes are missing, create them!
            if (nameNode == null)
            {
                GameObject nameGo = new GameObject(nameNodeName);
                nameGo.transform.SetParent(panel, false);
                nameNode = nameGo.transform;
            }
            if (hpBarNode == null)
            {
                GameObject hpBarGo = new GameObject(hpBarNodeName);
                hpBarGo.transform.SetParent(panel, false);
                hpBarNode = hpBarGo.transform;
            }

            // Ensure the Name node has a Text component
            var textComp = nameNode.GetComponent<Text>();
            if (textComp == null)
            {
                textComp = nameNode.gameObject.AddComponent<Text>();
                textComp.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                textComp.text = panelName.StartsWith("Player") ? "Player" : "Enemy";
                textComp.color = Color.white;
                Debug.Log($"[CombatUISceneFixer] Added Text component to {nameNode.name}");
            }

            // Ensure the HPBar node has an Image component
            var imageComp = hpBarNode.GetComponent<Image>();
            if (imageComp == null)
            {
                imageComp = hpBarNode.gameObject.AddComponent<Image>();
                imageComp.type = Image.Type.Filled;
                imageComp.fillMethod = Image.FillMethod.Horizontal;
                Debug.Log($"[CombatUISceneFixer] Added Image component to {hpBarNode.name}");
            }
        }

        private static void FixActionPanel(GameObject combatHUD)
        {
            Transform actionPanel = FindChildRecursive(combatHUD.transform, "ActionPanel");
            if (actionPanel == null)
            {
                Debug.LogWarning("[CombatUISceneFixer] ActionPanel not found. Creating it.");
                GameObject panelGo = new GameObject("ActionPanel");
                panelGo.transform.SetParent(combatHUD.transform, false);
                actionPanel = panelGo.transform;
            }

            string[] actions = { "Attack", "Defend", "Skill", "Item" };

            foreach (var act in actions)
            {
                // We could find either ChoiceText_Act or ActButton
                Transform choiceNode = FindChildRecursive(actionPanel, $"ChoiceText_{act}");
                if (choiceNode == null)
                {
                    choiceNode = FindChildRecursive(actionPanel, $"{act}Button");
                }

                if (choiceNode == null)
                {
                    GameObject actGo = new GameObject($"ChoiceText_{act}");
                    actGo.transform.SetParent(actionPanel, false);
                    choiceNode = actGo.transform;
                    Debug.Log($"[CombatUISceneFixer] Created missing choice node ChoiceText_{act}");
                }
                else if (!choiceNode.name.Equals($"ChoiceText_{act}", StringComparison.OrdinalIgnoreCase))
                {
                    string oldName = choiceNode.name;
                    choiceNode.name = $"ChoiceText_{act}";
                    Debug.Log($"[CombatUISceneFixer] Renamed {oldName} to {choiceNode.name}");
                }

                // Remove Button component if present
                var buttonComp = choiceNode.GetComponent<Button>();
                if (buttonComp != null)
                {
                    Debug.Log($"[CombatUISceneFixer] Removing Button component from {choiceNode.name}");
                    Undo.DestroyObjectImmediate(buttonComp);
                }

                // Ensure Text component is present
                var textComp = choiceNode.GetComponent<Text>();
                if (textComp == null)
                {
                    textComp = choiceNode.gameObject.AddComponent<Text>();
                    textComp.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    textComp.text = act.ToUpper();
                    textComp.color = Color.white;
                    Debug.Log($"[CombatUISceneFixer] Added Text component to {choiceNode.name}");
                }
            }
        }

        private static void WireCombatManager(GameObject combatHUD)
        {
            // Add CombatManager component if missing
            System.Type combatManagerType = System.Type.GetType("Omnisense.CombatManager, Assembly-CSharp") ?? System.Type.GetType("Omnisense.CombatManager");
            if (combatManagerType == null)
            {
                Debug.LogError("[CombatUISceneFixer] Could not find Omnisense.CombatManager type. Ensure CombatManager.cs is compiled.");
                return;
            }

            Component manager = combatHUD.GetComponent(combatManagerType);
            if (manager == null)
            {
                manager = combatHUD.AddComponent(combatManagerType);
                Debug.Log("[CombatUISceneFixer] Added CombatManager component to CombatHUD");
            }

            // Find all components to wire
            Transform playerPanel = FindChildRecursive(combatHUD.transform, "PlayerPanel");
            Text playerName = playerPanel.Find("PlayerName").GetComponent<Text>();
            Image playerHP = playerPanel.Find("PlayerHPBar").GetComponent<Image>();

            Transform enemyPanel = FindChildRecursive(combatHUD.transform, "EnemyPanel");
            Text enemyName = enemyPanel.Find("EnemyName").GetComponent<Text>();
            Image enemyHP = enemyPanel.Find("EnemyHPBar").GetComponent<Image>();

            Transform actionPanel = FindChildRecursive(combatHUD.transform, "ActionPanel");
            Text attackText = actionPanel.Find("ChoiceText_Attack").GetComponent<Text>();
            Text defendText = actionPanel.Find("ChoiceText_Defend").GetComponent<Text>();
            Text skillText = actionPanel.Find("ChoiceText_Skill").GetComponent<Text>();
            Text itemText = actionPanel.Find("ChoiceText_Item").GetComponent<Text>();

            // Wire using SerializedObject for persistent serialization
            SerializedObject so = new SerializedObject(manager);

            so.FindProperty("playerNameText").objectReferenceValue = playerName;
            so.FindProperty("playerHPBarImage").objectReferenceValue = playerHP;

            so.FindProperty("enemyNameText").objectReferenceValue = enemyName;
            so.FindProperty("enemyHPBarImage").objectReferenceValue = enemyHP;

            so.FindProperty("attackChoiceText").objectReferenceValue = attackText;
            so.FindProperty("defendChoiceText").objectReferenceValue = defendText;
            so.FindProperty("skillChoiceText").objectReferenceValue = skillText;
            so.FindProperty("itemChoiceText").objectReferenceValue = itemText;

            bool success = so.ApplyModifiedProperties();
            Debug.Log($"[CombatUISceneFixer] Serialized properties applied successfully: {success}");
        }
    }
}
