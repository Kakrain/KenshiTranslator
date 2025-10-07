# Add Batch Translation Support for Large Modpacks

## Overview
This PR adds comprehensive batch translation capabilities to KenshiTranslator, enabling users to translate and apply translations to up to 50 mods simultaneously. These improvements were developed to support translating large Kenshi modpacks (600+ mods) efficiently.

## Motivation
As a Brazilian Kenshi player working with a modpack of 600+ mods, I needed a way to efficiently translate multiple mods at once. The original version required translating each mod individually, which was extremely time-consuming. These changes reduce translation time from hours to minutes when working with large modpacks.

## Changes Made

### 🆕 New Features

#### 1. Batch Translation (Create Dictionary)
- Select multiple mods (up to 50) using Ctrl+Click or Shift+Click
- Translate all selected mods in sequence
- Real-time progress tracking: `[3/10] Translating ModName... 250/500`
- Detailed logging with per-mod prefixes
- Final summary showing successful vs failed translations

#### 2. Batch Apply Translations (Translate Mod)
- Apply translations to multiple mods at once
- Automatic validation:
  - Checks for missing translation dictionaries
  - Shows incomplete translations (<95%) and asks for confirmation
- Automatic backup creation for all mods
- Progress tracking and comprehensive error reporting

#### 3. Portuguese (Brazilian) Support
- Added `pt-BR` language code for Google Cloud Translation V3
- Proper support for Brazilian Portuguese variants

### 🐛 Bug Fixes

#### Critical: Language Code Selection
**Problem**: The application was incorrectly extracting language codes by parsing the combo box display text instead of using the underlying value.

**Impact**: This caused translation API errors like "source language is invalid" because mangled strings were being sent instead of proper language codes.

**Solution**: Changed from `ToString().Split(' ')[0]` to `(ComboItem).Code`, ensuring correct language codes (e.g., `en`, `pt-BR`) are always used.

**Files Changed**: `MainForm.cs:362-363`

#### Translation Progress Not Updating
**Problem**: Batch translations using Google Cloud V3 weren't updating the success counter, causing "No translations were produced" errors even when translations succeeded.

**Solution**: Properly capture the return value from `ApplyTranslationsAsync()` and update progress in the log callback.

**Files Changed**: `MainForm.cs:380`, `MainForm.cs:400-405`

### 📝 Code Changes

**Modified Files**:
- `MainForm.cs` - Added batch translation logic, fixed language code handling
- `Translator/CustomApiTranslator.cs` - Added `pt-BR` language support
- `CLAUDE.md` - Updated documentation with new features
- `CHANGELOG.md` - New file documenting all changes

**Key Implementation Details**:
- ListView now supports `MultiSelect = true`
- Both "Create Dictionary" and "Translate Mod" buttons handle multiple selections
- Limit of 50 mods per batch operation to prevent UI freezing
- Progress reporting shows both mod-level (`[3/10]`) and item-level (`250/500`) progress
- Thread-safe progress updates using `Interlocked.Increment`

## Testing
Tested with:
- Single mod translation (existing functionality preserved)
- Batch translation of 10+ mods simultaneously
- Google Cloud Translation V3 with Portuguese (Brazilian)
- Edge cases: incomplete translations, missing dictionaries, API failures

## Screenshots
*(You may want to add screenshots showing the batch selection and progress UI)*

## Breaking Changes
None. All existing functionality is preserved. Single-mod workflow works exactly as before.

## Compatibility
- .NET 9.0
- Windows Forms
- All existing translation providers (Aggregate, Bing, Google, DeepL, etc.)
- Requires KenshiCore dependency (existing requirement)

## Future Improvements
Potential enhancements that could be added later:
- Persist selected mods across sessions
- Resume interrupted batch translations
- Export/import batch translation configurations
- Parallel mod translation (currently sequential)

## Author Note
I'm a Brazilian developer and Kenshi player who loves this tool. These improvements have made translating my 600+ mod collection actually feasible. I hope this helps other players in the community who work with large modpacks!

Thank you for creating this amazing tool! 🙏

---

**Related Issues**: N/A (proactive contribution)
**Tested on**: Windows 11, .NET 9.0.305
