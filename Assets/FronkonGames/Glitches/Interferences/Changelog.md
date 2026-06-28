# Changelog
All notable changes to this package will be documented in this file.

## [3.0.2] - 21-02-2026

# Changed
- Only intensity is affected by the volume weight.

## [3.0.1] - 20-02-2026

# Added
- Added 'Use Scaled Time' advanced parameter.

# Fix
- Correct interpolation with Volume Weight.

## [3.0.0] - 01-02-2026

# Added
- Support for Volumes.

# Removed
- Removed support for 2022.3.

## [2.1.3] - 04-09-2025

# Fix
- Fixed VR build compatibility in Unity 6.

## [2.1.2] - 22-04-2025

# Fix
- Camera 'Post Processing' checkbox fixed.

## [2.1.1] - 11-03-2025

# Fix
- Color precision fix.

## [2.1.0] - 24-02-2025

# Added
- Support for effects in multiples Renderers.
- GetAllSettings() added to get all settings of all effects in the pipeline.
- 'Affect the Scene View' added to Unity 6.

# Removed
- Removed GetSettings(), use .Instance.settings

# Fix
- Errors when domain reload is disabled.
- Memory leak in Unity 2022.3.
- Documentation URL fixed.

## [2.0.0] - 18-12-2024

# Added
- Support for Unity 6.

# Changed
- Performance improvements.
- Removed support for Unity 2021.3.

# Fix
- pow() replaced with SafePositivePow().
- Reset static variables when Domain Reload is disabled.

## [1.1.0] - 21-07-2024

# Changed
- Removed the AddRenderFeature() and RemoveRenderFeature() from the effect that damaged the configuration file.
- Performance improvements.

# Fix
- Small fixes.

## [1.0.1] - 02-06-2024

### Fixed.
- 'Remap' changed to 'RemapValue' to avoid conflicts.

### Changed
- New online documentation.

## [1.0.0] - 05-05-2024

- Initial release.