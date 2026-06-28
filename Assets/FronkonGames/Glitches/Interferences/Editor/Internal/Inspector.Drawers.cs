////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) Martin Bustos @FronkonGames <fronkongames@gmail.com>. All rights reserved.
//
// THIS FILE CAN NOT BE HOSTED IN PUBLIC REPOSITORIES.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
// WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Rendering;

namespace FronkonGames.Glitches.Interferences.Editor
{
  /// <summary> Custom drawers. </summary>
  public abstract partial class Inspector : VolumeComponentEditor
  {
    /// <summary> Draws an IntSliderWithResetAttribute with slider and reset using attribute configuration. </summary>
    protected void DrawIntSliderWithReset(string name, GUIContent label = null)
    {
      SerializedDataParameter parameter = UnpackParameter(name);
      if (parameter == null)
        return;

      // Try to get the attribute
      var attr = parameter.GetAttribute<IntSliderWithResetAttribute>();

      // Fallback to default drawing if attribute not found
      if (attr == null)
      {
        EditorGUILayout.PropertyField(parameter.value, label ?? new GUIContent(parameter.displayName));
        return;
      }

      // Determine label content
      GUIContent displayLabel = label ?? new GUIContent(parameter.displayName, attr.tooltip);

      EditorGUILayout.BeginHorizontal();
      {
        bool isOverridden = parameter.overrideState.boolValue;

        EditorGUI.BeginDisabledGroup(!isOverridden);
        {
          // Get current value
          int value = parameter.value.intValue;

          // Draw slider using attribute values
          EditorGUI.BeginChangeCheck();
          value = EditorGUILayout.IntSlider(displayLabel, value, attr.min, attr.max);
          if (EditorGUI.EndChangeCheck() == true)
          {
            parameter.value.intValue = value;
            parameter.overrideState.boolValue = true;
          }
        }
        EditorGUI.EndDisabledGroup();

        int oldIndentLevel = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        EditorGUILayout.PropertyField(parameter.overrideState, GUIContent.none, GUILayout.Width(16));
        EditorGUI.indentLevel = oldIndentLevel;

        EditorGUI.BeginDisabledGroup(!parameter.overrideState.boolValue);
        {
          if (ResetButton(attr.value, parameter.value.intValue != attr.value) == true)
          {
            parameter.value.intValue = attr.value;
            parameter.overrideState.boolValue = true;
          }
        }
        EditorGUI.EndDisabledGroup();
      }
      EditorGUILayout.EndHorizontal();
    }

    /// <summary> Draws an FloatSliderWithResetAttribute with slider and reset using attribute configuration. </summary>
    protected void DrawFloatSliderWithReset(string name, string label = null)
    {
      SerializedDataParameter parameter = UnpackParameter(name);
      if (parameter == null)
        return;

      // Try to get the attribute
      var attr = parameter.GetAttribute<FloatSliderWithResetAttribute>();

      // Fallback to default drawing if attribute not found
      if (attr == null)
      {
        PropertyField(parameter, new GUIContent(label ?? parameter.displayName));
        return;
      }

      // Determine label content
      GUIContent displayLabel = new(label ?? parameter.displayName, attr.tooltip);

      EditorGUILayout.BeginHorizontal();
      {
        bool isOverridden = parameter.overrideState.boolValue;

        EditorGUI.BeginDisabledGroup(!isOverridden);
        {
          // Get current value from serialized property
          float value = parameter.value.floatValue;

          // Draw slider using attribute values
          EditorGUI.BeginChangeCheck();
          value = EditorGUILayout.Slider(displayLabel, value, attr.min, attr.max);
          if (EditorGUI.EndChangeCheck() == true)
          {
            parameter.value.floatValue = value;
            parameter.overrideState.boolValue = true;
          }
        }
        EditorGUI.EndDisabledGroup();

        int oldIndentLevel = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        EditorGUILayout.PropertyField(parameter.overrideState, GUIContent.none, GUILayout.Width(16));
        EditorGUI.indentLevel = oldIndentLevel;

        EditorGUI.BeginDisabledGroup(!parameter.overrideState.boolValue);
        {
          if (ResetButton(attr.value, parameter.value.floatValue != attr.value) == true)
          {
            parameter.value.floatValue = attr.value;
            parameter.overrideState.boolValue = true;
          }
        }
        EditorGUI.EndDisabledGroup();
      }
      EditorGUILayout.EndHorizontal();
    }

    protected void DrawToggleWithReset(string name, bool defaultValue = default)
    {
      SerializedDataParameter parameter = UnpackParameter(name);
      if (parameter == null)
        return;
    
      GUIContent displayLabel = new(parameter.displayName);

      EditorGUILayout.BeginHorizontal();
      {
        bool isOverridden = parameter.overrideState.boolValue;

        EditorGUI.BeginDisabledGroup(!isOverridden);
        {
          bool value = parameter.value.boolValue;
          EditorGUI.BeginChangeCheck();
          value = EditorGUILayout.Toggle(displayLabel, value);
          if (EditorGUI.EndChangeCheck() == true)
          {
            parameter.value.boolValue = value;
            parameter.overrideState.boolValue = true;
          }
        }
        EditorGUI.EndDisabledGroup();

        int oldIndentLevel = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        EditorGUILayout.PropertyField(parameter.overrideState, GUIContent.none, GUILayout.Width(16));
        EditorGUI.indentLevel = oldIndentLevel;

        EditorGUI.BeginDisabledGroup(!parameter.overrideState.boolValue);
        {
          if (ResetButton(defaultValue, parameter.value.boolValue != defaultValue) == true)
          {
            parameter.value.boolValue = defaultValue;
            parameter.overrideState.boolValue = true;
          }
        }
        EditorGUI.EndDisabledGroup();
      }
      EditorGUILayout.EndHorizontal();      
    }

    /// <summary> Draws a ColorFieldWithResetAttribute with color field and reset using attribute configuration. </summary>
    protected void DrawColorWithReset(string name, string label = null, Color defaultValue = default)
    {
      SerializedDataParameter parameter = UnpackParameter(name);
      if (parameter == null)
        return;
    
      GUIContent displayLabel = new(label ?? parameter.displayName);

      EditorGUILayout.BeginHorizontal();
      {
        bool isOverridden = parameter.overrideState.boolValue;

        EditorGUI.BeginDisabledGroup(!isOverridden);
        {
          Color value = parameter.value.colorValue;
          EditorGUI.BeginChangeCheck();
          value = EditorGUILayout.ColorField(displayLabel, value);
          if (EditorGUI.EndChangeCheck() == true)
          {
            parameter.value.colorValue = value;
            parameter.overrideState.boolValue = true;
          }
        }
        EditorGUI.EndDisabledGroup();

        int oldIndentLevel = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        EditorGUILayout.PropertyField(parameter.overrideState, GUIContent.none, GUILayout.Width(16));
        EditorGUI.indentLevel = oldIndentLevel;

        EditorGUI.BeginDisabledGroup(!parameter.overrideState.boolValue);
        {
          if (ResetButton(defaultValue, parameter.value.colorValue != defaultValue) == true)
          {
            parameter.value.colorValue = defaultValue;
            parameter.overrideState.boolValue = true;
          }
        }
        EditorGUI.EndDisabledGroup();
      }
      EditorGUILayout.EndHorizontal();
    }

    /// <summary> Draws an EnumParameter with dropdown and reset button using generics. </summary>
    /// <typeparam name="T">The enum type</typeparam>
    protected void DrawEnumDropdownWithReset<T>(string name, string label = null, T defaultValue = default) where T : Enum
    {
      SerializedDataParameter parameter = UnpackParameter(name);
      if (parameter == null)
        return;
    
      GUIContent displayLabel = new(label ?? parameter.displayName);

      EditorGUILayout.BeginHorizontal();
      {
        bool isOverridden = parameter.overrideState.boolValue;

        EditorGUI.BeginDisabledGroup(!isOverridden);
        {
          // Get current value (stored as int)
          int currentInt = parameter.value.intValue;

          // Validate bounds (safety check)
          Array enumValues = Enum.GetValues(typeof(T));
          if (currentInt < 0 || currentInt >= enumValues.Length)
            currentInt = 0;

          // Convert int to enum
          T currentValue = (T)enumValues.GetValue(currentInt);

          // Draw enum popup
          EditorGUI.BeginChangeCheck();
          T newValue = (T)EditorGUILayout.EnumPopup(displayLabel, currentValue);
          if (EditorGUI.EndChangeCheck() == true)
          {
            parameter.value.intValue = Convert.ToInt32(newValue);
            parameter.overrideState.boolValue = true;
          }
        }
        EditorGUI.EndDisabledGroup();

        int oldIndentLevel = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        EditorGUILayout.PropertyField(parameter.overrideState, GUIContent.none, GUILayout.Width(16));
        EditorGUI.indentLevel = oldIndentLevel;

        EditorGUI.BeginDisabledGroup(!parameter.overrideState.boolValue);
        {
          if (ResetButton(defaultValue, parameter.value.intValue != Convert.ToInt32(defaultValue)) == true)
          {
            parameter.value.intValue = Convert.ToInt32(defaultValue);
            parameter.overrideState.boolValue = true;
          }
        }
        EditorGUI.EndDisabledGroup();
      }
      EditorGUILayout.EndHorizontal();
    }

    /// <summary> Draws a Vector2Int field with reset. </summary>
    protected void DrawVector2IntWithReset(string name, string label = null, Vector2Int defaultValue = default)
    {
      SerializedDataParameter parameter = UnpackParameter(name);
      if (parameter == null)
        return;

      GUIContent displayLabel = new(label ?? parameter.displayName);

      Rect rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
      
      float buttonsWidth = 16.0f + 20.0f + 2.0f;
      Rect fieldRect = new(rect.x, rect.y, rect.width - buttonsWidth, rect.height);
      Rect overrideRect = new(rect.x + rect.width - buttonsWidth + 2.0f, rect.y, 16.0f, rect.height);
      Rect resetRect = new(rect.x + rect.width - 19.0f, rect.y, 19.0f, rect.height);

      bool isOverridden = parameter.overrideState.boolValue;
      
      EditorGUI.BeginDisabledGroup(!isOverridden);
      {
        Vector2Int value = parameter.value.vector2IntValue;
        EditorGUI.BeginChangeCheck();
        
        Rect labelRect = new(fieldRect.x, fieldRect.y, EditorGUIUtility.labelWidth, fieldRect.height);
        Rect valueRect = new(fieldRect.x + EditorGUIUtility.labelWidth, fieldRect.y, fieldRect.width - EditorGUIUtility.labelWidth, fieldRect.height);
        
        EditorGUI.LabelField(labelRect, displayLabel);
        value = EditorGUI.Vector2IntField(valueRect, GUIContent.none, value);
        
        if (EditorGUI.EndChangeCheck() == true)
        {
          parameter.value.vector2IntValue = value;
          parameter.overrideState.boolValue = true;
        }
      }
      EditorGUI.EndDisabledGroup();

      int oldIndentLevel = EditorGUI.indentLevel;
      EditorGUI.indentLevel = 0;
      EditorGUI.PropertyField(overrideRect, parameter.overrideState, GUIContent.none);
      EditorGUI.indentLevel = oldIndentLevel;

      EditorGUI.BeginDisabledGroup(!parameter.overrideState.boolValue);
      {
        if (ResetButton(resetRect, defaultValue, parameter.value.vector2IntValue != defaultValue) == true)
        {
          parameter.value.vector2IntValue = defaultValue;
          parameter.overrideState.boolValue = true;
        }
      }
      EditorGUI.EndDisabledGroup();
    }

    /// <summary> Draws a MinMaxSlider field with reset. </summary>
    protected void DrawMinMaxSliderWithReset(string name, float minLimit, float maxLimit, Vector2 defaultValue = default)
    {
      SerializedDataParameter parameter = UnpackParameter(name);
      if (parameter == null)
        return;

      GUIContent displayLabel = new(parameter.displayName);

      Rect rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
      
      float buttonsWidth = 16.0f + 20.0f + 2.0f;
      Rect fieldRect = new(rect.x, rect.y, rect.width - buttonsWidth, rect.height);
      Rect overrideRect = new(rect.x + rect.width - buttonsWidth + 2.0f, rect.y, 16.0f, rect.height);
      Rect resetRect = new(rect.x + rect.width - 19.0f, rect.y, 19.0f, rect.height);

      bool isOverridden = parameter.overrideState.boolValue;

      EditorGUI.BeginDisabledGroup(!isOverridden);
      {
        Vector2 value = parameter.value.vector2Value;
        EditorGUI.BeginChangeCheck();
        
        EditorGUI.MinMaxSlider(fieldRect, displayLabel, ref value.x, ref value.y, minLimit, maxLimit);
        
        if (EditorGUI.EndChangeCheck() == true)
        {
          parameter.value.vector2Value = value;
          parameter.overrideState.boolValue = true;
        }
      }
      EditorGUI.EndDisabledGroup();

      int oldIndentLevel = EditorGUI.indentLevel;
      EditorGUI.indentLevel = 0;
      EditorGUI.PropertyField(overrideRect, parameter.overrideState, GUIContent.none);
      EditorGUI.indentLevel = oldIndentLevel;

      EditorGUI.BeginDisabledGroup(!parameter.overrideState.boolValue);
      {
        if (ResetButton(resetRect, defaultValue, parameter.value.vector2Value != defaultValue) == true)
        {
          parameter.value.vector2Value = defaultValue;
          parameter.overrideState.boolValue = true;
        }
      }
      EditorGUI.EndDisabledGroup();
    }
    
    /// <summary> Helper to extract attributes from SerializedDataParameter. </summary>
    protected T GetAttribute<T>(SerializedDataParameter param) where T : Attribute
    {
      if (param.attributes == null)
        return null;
      
      return param.attributes.OfType<T>().FirstOrDefault();
    }    
  }
}
