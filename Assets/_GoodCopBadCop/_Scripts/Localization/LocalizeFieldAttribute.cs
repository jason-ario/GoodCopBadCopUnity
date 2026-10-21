using System;

/// <summary>
/// Marks a player-facing <c>string</c> / <c>string[]</c> / <c>List&lt;string&gt;</c> field on a component (or on a
/// [Serializable] class nested in one) so the Localization window's scan picks up its values in scenes and prefabs,
/// including values overridden on prefab instances. The values land in the sheets keyed by origin; at runtime the text
/// is resolved by its English source, either automatically at render time (TMP) or explicitly with
/// <see cref="Loc.TranslateSource"/> when the code composes or animates it before display.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class LocalizeFieldAttribute : Attribute { }
