using RythmRPG.Combat;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.EditorTools
{
    /// <summary>Draws a <see cref="NoteCue"/> on one line: the moment, then the offset in seconds.</summary>
    [CustomPropertyDrawer(typeof(NoteCue))]
    public sealed class NoteCueDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty moment = property.FindPropertyRelative("moment");
            SerializedProperty offset = property.FindPropertyRelative("offset");
            EditorGUI.BeginProperty(position, label, property);
            Rect field = EditorGUI.PrefixLabel(position, label);
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            float secondsWidth = Mathf.Min(90f, field.width * 0.4f);
            var momentRect = new Rect(field.x, field.y, field.width - secondsWidth - 4f, field.height);
            var offsetRect = new Rect(momentRect.xMax + 4f, field.y, secondsWidth, field.height);
            EditorGUI.PropertyField(momentRect, moment, GUIContent.none);
            float old = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 14f;
            var momentValue = (NoteMoment)moment.enumValueIndex;
            bool canLead = momentValue == NoteMoment.ReachedBeat || momentValue == NoteMoment.HoldEnd;
            EditorGUI.PropertyField(offsetRect, offset, new GUIContent(canLead ? "±" : "+",
                canLead ? "Seconds after the moment (negative = before it)." : "Seconds after the moment."));
            if (!canLead && offset.floatValue < 0f) offset.floatValue = 0f;
            EditorGUIUtility.labelWidth = old;
            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
    }
}
