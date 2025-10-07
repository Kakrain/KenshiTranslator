# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased] - 2025-01-06

### Added
- **Batch Translation Support**: Translate up to 50 mods simultaneously
  - Multi-select mods using Ctrl+Click or Shift+Click
  - Confirmation dialog showing number of mods to be translated
  - Progress tracking per mod: `[3/10] Translating ModName... 250/500`
  - Final summary showing successful vs failed translations

- **Batch Apply Translations**: Apply translations to multiple mods at once
  - Validates all selected mods have translation dictionaries
  - Shows list of mods with incomplete translations (<95%) before applying
  - Automatic backup creation for all mods
  - Progress tracking and error reporting

- **Portuguese (Brazilian) Language Support**
  - Added `pt-BR` language code for Google Cloud Translation V3
  - Properly supports Brazilian Portuguese variants

### Fixed
- **Language Code Selection Bug**: Fixed critical bug where language codes were incorrectly extracted from display text
  - Changed from parsing `ToString()` output to using `ComboItem.Code` property
  - Ensures correct language codes are sent to translation APIs (e.g., `en`, `pt-BR` instead of mangled strings)
  - Fixes "source language is invalid" errors

- **Translation Progress Tracking**: Fixed issue where batch translations weren't updating progress counters
  - Now correctly captures return value from `ApplyTranslationsAsync()`
  - Progress updates work for both individual and batch translation modes
  - Enables "Translate Mod" button after successful dictionary creation

### Improved
- **Multi-selection UI**: Enabled `MultiSelect = true` on ListView for better UX
- **Progress Reporting**: Enhanced to show both mod-level and item-level progress in batch mode
- **Error Handling**: More detailed error messages with mod names in batch operations
- **Logging**: All log entries now prefixed with `[ModName]` for easier debugging

### Context
These improvements were developed to support translating large Kenshi modpacks (600+ mods) from English to Portuguese (Brazilian). The batch operations significantly reduce the time and manual effort required to translate many mods, especially when using Google Cloud Translation V3's efficient batch API.

## Previous Versions
For changes prior to this fork, see the original repository at https://github.com/Kakrain/KenshiTranslator
