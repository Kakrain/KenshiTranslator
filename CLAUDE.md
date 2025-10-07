# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

KenshiTranslator is a .NET 9 Windows Forms application for translating Kenshi game .mod files. It extracts text from .mod files into editable .dict (dictionary) files, supports translation via multiple providers (Google Translate, DeepL, etc.), and applies translations back to the .mod files.

## Build & Run Commands

```bash
# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run the application
dotnet run --project KenshiTranslator.csproj

# Build for release
dotnet build --configuration Release
```

## Architecture

### Core Components

1. **KenshiCore Dependency** (../KenshiCore)
   - External project referenced in the solution
   - Provides `ReverseEngineer`, `ModManager`, `ProtoMainForm`, `GeneralLogForm`, `ModItem` classes
   - Handles low-level .mod file parsing (v16 & v17 formats)
   - Located at: `..\KenshiCore\KenshiCore.csproj`

2. **MainForm.cs** - Main UI and orchestration
   - Inherits from `ProtoMainForm` (from KenshiCore)
   - Coordinates translation workflow: Extract → Translate → Apply
   - Handles language detection using NTextCat library
   - Manages translation provider selection and configuration
   - Uses `ModManager` and `ReverseEngineer` from KenshiCore

3. **Helper/TranslationDictionary.cs** - Dictionary file management
   - Exports mod strings to `.dict` format: `key|_SEP_|original|_SEP_|translation|_END_|`
   - Imports translated `.dict` files back into mod structure
   - **Critical feature**: Handles game constants (e.g., `/ITEM_NAME/`) using multiple fallback strategies:
     - Try normal translation
     - Try with `¤0¤` markers
     - Try with `[[MARKER_0]]` markers
     - Fallback: Split and translate parts separately
   - Supports batch translation with progress tracking and resume capability

4. **Translator/** - Translation provider abstraction
   - `TranslatorInterface.cs`: Common interface for all translators
   - `GTranslate_Translator.cs`: Wrapper for GTranslate library (Aggregate, Bing, Google, Google2, Microsoft, Yandex)
   - `CustomApiTranslator.cs`: Supports DeepL API, Google Cloud API v2/v3, and generic endpoints
     - Auto-detects API type from key format
     - Google Cloud V3 has efficient **batch translation** support (`TranslateBatchV3Async`)
     - Uses singleton pattern for Google V3 client initialization

### Translation Workflow

1. **Extract**: User selects mod(s) → `TranslationDictionary.ExportToDictFile()` creates `.dict` file
2. **Translate**:
   - `TranslationDictionary.ApplyTranslationsAsync()` processes dictionary in batches
   - Uses selected translator (GTranslate or CustomApi)
   - Google Cloud V3 can batch translate 100+ items per request (much faster)
   - Progress tracked and saved incrementally (resumable)
   - **Batch mode**: Supports translating up to 50 mods at once
3. **Apply**: `TranslationDictionary.ImportFromDictFile()` + `ReverseEngineer.SaveModFile()` merges translations back
   - **Batch mode**: Supports applying translations to up to 50 mods at once
4. **Backup**: Original .mod saved as `.backup` before applying

### Key Files & Patterns

- **languages.txt**: Cached language detection results (format: `modName=eng|___`)
- **LanguageModels/Core14.profile.xml**: Language detection model for NTextCat
- **Dictionary format**: Text-based with separators `|_SEP_|` and line endings `|_END_|`
- **Constant handling**: Game constants like `/STAT_NAME/` must be preserved during translation

## Development Notes

### Working with KenshiCore
- KenshiCore is referenced as a project dependency from `../KenshiCore`
- KenshiCore provides the base form classes and mod file I/O
- If modifying mod parsing logic, changes go in KenshiCore, not KenshiTranslator

### Translation Provider Extension
To add a new translator:
1. Implement `TranslatorInterface` in `Translator/` directory
2. Add to provider combo box in `MainForm.cs` constructor
3. Handle selection in `providerCombo_SelectedIndexChanged()`

### Constant Preservation
Game mods use special constants like `/FACTION_NAME/` that must remain untranslated. The `TranslateWithFallbacksAsync()` method tries multiple strategies to preserve these markers during translation.

### API Key Handling
- DeepL keys: 39 chars, end with `:fx` for free tier
- Google Cloud API keys: Start with `AIza`
- Google Cloud V3: Uses service account JSON file path
- Environment variable `GOOGLE_APPLICATION_CREDENTIALS` set for Google V3

### Language Detection
- Uses NTextCat library with Core14 language profiles
- Results cached in `languages.txt` for performance
- Detects language separately for alphanumeric vs special character content
- Format: `eng|___` means English alphanumeric, unknown symbols

## Important Patterns

- **Thread safety**: `reLockRE` object used to synchronize access to `ReverseEngineer`
- **UI updates**: Use `Invoke()` or `InvokeRequired` when updating UI from background threads
- **Error handling**: Translation failures are logged but don't stop the batch; threshold of 10 consecutive failures aborts
- **Progress reporting**: `InitializeProgress()` and `ReportProgress()` for user feedback
- **Multi-selection**: ListView supports `MultiSelect = true` for batch operations (Ctrl+Click, Shift+Click)

## Recent Improvements

### Batch Translation Support (2025)
- **Multi-mod selection**: Translate up to 50 mods simultaneously
- **Fixed language code handling**: Now correctly uses `ComboItem.Code` instead of parsing display text
- **Portuguese (Brazilian) support**: Added `pt-BR` language code for Google Cloud V3
- **Batch apply**: Apply translations to multiple mods at once with progress tracking
- **Enhanced validation**: Checks for missing dictionaries and incomplete translations before batch operations
- **Improved progress tracking**: Shows per-mod progress in batch mode `[3/10] Translating ModName...`
- **Better error reporting**: Detailed summary of successful vs failed translations in batch operations
