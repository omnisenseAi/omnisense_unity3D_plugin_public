using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;

namespace Omnisense
{
    public static class UnityComponentHelper
    {
        public static bool SetProperty(Component comp, string propertyName, string value, out string error)
        {
            error = string.Empty;
            if (comp == null)
            {
                error = "Component is null.";
                return false;
            }

            Undo.RecordObject(comp, "Modify Property via Omnisense");

            // 1. Try SerializedObject first (handles Prefabs and Undo better)
            SerializedObject so = new SerializedObject(comp);
            SerializedProperty prop = so.FindProperty(propertyName);
            
            if (prop == null && !propertyName.StartsWith("m_"))
            {
                prop = so.FindProperty("m_" + char.ToUpper(propertyName[0]) + propertyName.Substring(1));
                if (prop == null) prop = so.FindProperty("m_" + propertyName);
            }

            if (prop != null)
            {
                if (ApplyToSerializedProperty(prop, value, out error))
                {
                    so.ApplyModifiedProperties();
                    return true;
                }
            }

            // 2. Fallback to Reflection (handles C# properties like gravityScale that may not map to m_ properly)
            if (ApplyViaReflection(comp, propertyName, value, out error))
            {
                EditorUtility.SetDirty(comp);
                return true;
            }

            error = $"Property '{propertyName}' not found or could not be set on {comp.GetType().Name}.";
            return false;
        }

        private static bool ApplyToSerializedProperty(SerializedProperty prop, string value, out string error)
        {
            error = string.Empty;
            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Enum:
                        int enumIndex = Array.FindIndex(prop.enumNames, name => name.Equals(value, StringComparison.OrdinalIgnoreCase));
                        if (enumIndex >= 0) prop.enumValueIndex = enumIndex;
                        else if (int.TryParse(value, out int intVal)) prop.enumValueIndex = intVal;
                        else 
                        {
                            error = $"Invalid enum value '{value}'. Valid options: {string.Join(", ", prop.enumNames)}";
                            return false;
                        }
                        break;
                    case SerializedPropertyType.Integer: prop.intValue = int.Parse(value); break;
                    case SerializedPropertyType.Float: prop.floatValue = float.Parse(value); break;
                    case SerializedPropertyType.Boolean: prop.boolValue = bool.Parse(value); break;
                    case SerializedPropertyType.String: prop.stringValue = value; break;
                    case SerializedPropertyType.Vector2:
                        string[] v2 = value.Split(',');
                        prop.vector2Value = new Vector2(float.Parse(v2[0]), float.Parse(v2[1]));
                        break;
                    case SerializedPropertyType.Vector3:
                        string[] v3 = value.Split(',');
                        prop.vector3Value = new Vector3(float.Parse(v3[0]), float.Parse(v3[1]), float.Parse(v3[2]));
                        break;
                    case SerializedPropertyType.ObjectReference:
                        string targetPath = value.StartsWith("/") ? value.Substring(1) : value;
                        GameObject targetObj = GameObject.Find(targetPath);
                        UnityEngine.Object finalTarget = targetObj;

                        if (finalTarget == null) 
                        {
                            finalTarget = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(value);
                            if (finalTarget == null)
                            {
                                string[] guids = AssetDatabase.FindAssets(value);
                                if (guids.Length > 0) finalTarget = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(guids[0]));
                            }
                        }

                        if (finalTarget != null) 
                        {
                            if (finalTarget is GameObject go)
                            {
                                if (prop.type.Contains("GameObject")) prop.objectReferenceValue = go;
                                else if (prop.type.Contains("Transform")) prop.objectReferenceValue = go.transform;
                                else 
                                {
                                    string typeName = prop.type.Replace("PPtr<$", "").Replace(">", "");
                                    prop.objectReferenceValue = go.GetComponent(typeName);
                                }
                            }
                            else 
                            {
                                prop.objectReferenceValue = finalTarget;
                            }
                        } 
                        else 
                        {
                            error = $"Target object/asset not found for value: {value}";
                            return false;
                        }
                        break;
                    default:
                        error = $"Property type {prop.propertyType} is not supported via SerializedProperty.";
                        return false;
                }
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        private static bool ApplyViaReflection(Component comp, string propertyName, string value, out string error)
        {
            error = string.Empty;
            Type type = comp.GetType();

            // Try Property
            PropertyInfo propInfo = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (propInfo != null && propInfo.CanWrite)
            {
                try
                {
                    object parsedValue = ParseValueForType(propInfo.PropertyType, value);
                    propInfo.SetValue(comp, parsedValue);
                    return true;
                }
                catch (Exception e)
                {
                    error = $"Reflection Property Error: {e.Message}";
                    return false;
                }
            }

            // Try Field
            FieldInfo fieldInfo = type.GetField(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (fieldInfo != null)
            {
                try
                {
                    object parsedValue = ParseValueForType(fieldInfo.FieldType, value);
                    fieldInfo.SetValue(comp, parsedValue);
                    return true;
                }
                catch (Exception e)
                {
                    error = $"Reflection Field Error: {e.Message}";
                    return false;
                }
            }

            return false;
        }

        private static object ParseValueForType(Type type, string value)
        {
            if (type == typeof(int)) return int.Parse(value);
            if (type == typeof(float)) return float.Parse(value);
            if (type == typeof(bool)) return bool.Parse(value);
            if (type == typeof(string)) return value;
            if (type.IsEnum)
            {
                try { return Enum.Parse(type, value, true); }
                catch { throw new ArgumentException($"Invalid enum value for {type.Name}: {value}"); }
            }
            if (type == typeof(Vector2))
            {
                string[] parts = value.Split(',');
                return new Vector2(float.Parse(parts[0]), float.Parse(parts[1]));
            }
            if (type == typeof(Vector3))
            {
                string[] parts = value.Split(',');
                return new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
            }

            throw new NotSupportedException($"Type {type.Name} is not currently supported via Reflection fallback.");
        }
    }
}
