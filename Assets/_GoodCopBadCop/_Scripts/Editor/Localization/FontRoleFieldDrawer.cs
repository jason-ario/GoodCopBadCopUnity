using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dropdown for <see cref="FontRoleFieldAttribute"/> string fields: "Auto (from font)" plus every role in
/// LocalizationSettings. Supports multi-object editing, so many labels can be set at once.
/// </summary>
[CustomPropertyDrawer(typeof(FontRoleFieldAttribute))]
public class FontRoleFieldDrawer : PropertyDrawer
{
    private const string Auto = "Auto (from font)";

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        var settings = LocalizationSettings.Instance;
        var roles = settings != null && settings.fontRoles != null
            ? settings.fontRoles.Where(r => r != null && !string.IsNullOrEmpty(r.name)).Select(r => r.name).ToList()
            : new List<string>();

        var options = new List<string> { Auto };
        options.AddRange(roles);

        string current = property.stringValue;
        int index = string.IsNullOrEmpty(current) ? 0 : options.IndexOf(current);
        if (index < 0)
        {
            options.Add(current + " (missing role)");
            index = options.Count - 1;
        }

        label = EditorGUI.BeginProperty(position, label, property);
        EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        int next = EditorGUI.Popup(position, label, index, options.Select(o => new GUIContent(o)).ToArray());
        if (EditorGUI.EndChangeCheck() && next != index)
            property.stringValue = next == 0 ? "" : next <= roles.Count ? roles[next - 1] : current;
        EditorGUI.showMixedValue = false;
        EditorGUI.EndProperty();
    }
}
