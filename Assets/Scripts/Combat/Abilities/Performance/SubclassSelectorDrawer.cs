#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Inspector for [SerializeReference, SubclassSelector] fields: a type popup (every concrete, serializable
    /// subclass, found automatically) above the chosen type's fields. Lives in the runtime assembly behind
    /// UNITY_EDITOR because the combat code has no editor assembly.
    /// </summary>
    [CustomPropertyDrawer(typeof(SubclassSelectorAttribute))]
    public sealed class SubclassSelectorDrawer : PropertyDrawer
    {
        private static readonly Dictionary<Type, Type[]> Cache = new();

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.ManagedReference) return EditorGUIUtility.singleLineHeight;
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.ManagedReference)
            {
                EditorGUI.LabelField(position, label.text, "Use [SerializeReference] with [SubclassSelector].");
                return;
            }

            Type baseType = ResolveBaseType(property);
            Type[] types = baseType != null ? SubtypesOf(baseType) : Array.Empty<Type>();
            object current = property.managedReferenceValue;
            string currentName = current != null ? Nice(current.GetType()) : "(none)";

            Rect popupRect = new(position.x + EditorGUIUtility.labelWidth + 2f, position.y,
                position.width - EditorGUIUtility.labelWidth - 2f, EditorGUIUtility.singleLineHeight);
            if (EditorGUI.DropdownButton(popupRect, new GUIContent(currentName), FocusType.Keyboard))
            {
                SerializedObject owner = property.serializedObject;
                string path = property.propertyPath;
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("(none)"), current == null, () => Assign(owner, path, null));
                foreach (Type type in types)
                {
                    Type captured = type;
                    menu.AddItem(new GUIContent(Nice(type)), current != null && current.GetType() == type,
                        () => Assign(owner, path, captured));
                }
                menu.DropDown(popupRect);
            }

            string title = current != null ? label.text + "  (" + currentName + ")" : label.text;
            EditorGUI.PropertyField(position, property, new GUIContent(title), true);
        }

        // The menu callback runs after OnGUI, so the property is looked up again by path.
        private static void Assign(SerializedObject owner, string path, Type type)
        {
            if (owner == null || owner.targetObject == null) return;
            owner.Update();
            SerializedProperty property = owner.FindProperty(path);
            if (property == null) return;
            property.managedReferenceValue = type != null ? Activator.CreateInstance(type) : null;
            owner.ApplyModifiedProperties();
        }

        private static string Nice(Type type)
        {
            string name = type.Name.EndsWith("Step") && type.Name.Length > 4 ? type.Name.Substring(0, type.Name.Length - 4) : type.Name;
            return ObjectNames.NicifyVariableName(name);
        }

        private static Type[] SubtypesOf(Type baseType)
        {
            if (Cache.TryGetValue(baseType, out Type[] cached)) return cached;
            Type[] found = TypeCache.GetTypesDerivedFrom(baseType)
                .Where(t => !t.IsAbstract && !t.IsGenericType && t.IsSerializable && t.GetConstructor(Type.EmptyTypes) != null
                            && !typeof(UnityEngine.Object).IsAssignableFrom(t))
                .OrderBy(t => t.Name)
                .ToArray();
            Cache[baseType] = found;
            return found;
        }

        // "managedReferenceFieldTypename" is "Assembly TypeName"; for list elements it is the element type.
        private static Type ResolveBaseType(SerializedProperty property)
        {
            string typename = property.managedReferenceFieldTypename;
            if (string.IsNullOrEmpty(typename)) return null;
            int space = typename.IndexOf(' ');
            if (space < 0) return null;
            string assembly = typename.Substring(0, space);
            string type = typename.Substring(space + 1);
            return Type.GetType(type + ", " + assembly);
        }
    }
}
#endif
