# IniFile Library: Detailed Description

This document is a complete technical description of the IniFile library, a one-file, external-dependency-free parser and editor for INI files for .NET. The document provides a detailed overview of the library's purpose and scope, its key features (format preservation, flexible parsing, JSON support, JSON path navigation, dynamic objects, environment variable expansion, multi-radix numbers, change tracking, dictionary round-trip, attribute serialization), internal class structure and mechanisms (regular expressions, caching, and optimizations), practical usage examples, strengths, and limitations. This material is intended for developers who want to integrate or modify the library, as well as for creating a technical specification based on the existing code.

## Contents

- [1. General Purpose](#1-general-purpose)
- [2. Key Features](#2-key-features)
  - [2.1. Formatting Preservation](#21-formatting-preservation)
  - [2.2. Parsing Flexibility](#22-parsing-flexibility)
  - [2.3. Duplicate Key Support](#23-duplicate-key-support)
  - [2.4. Multi-line Values](#24-multi-line-values)
  - [2.5. Built-in JSON Support](#25-built-in-json-support)
  - [2.6. JSON Path Navigation](#26-json-path-navigation)
  - [2.7. Attribute-based Serialization](#27-attribute-based-serialization)
  - [2.8. Change Tracking](#28-change-tracking)
  - [2.9. Dictionary Round-trip](#29-dictionary-round-trip)
  - [2.10. Environment Variable Expansion](#210-environment-variable-expansion)
  - [2.11. Numbers in Different Radices](#211-numbers-in-different-radices)
  - [2.12. Presence Checks](#212-presence-checks)
  - [2.13. Normalized Output](#213-normalized-output)
- [3. Library Structure](#3-library-structure)
  - [3.1. Namespace](#31-namespace)
  - [3.2. Enums](#32-enums)
  - [3.3. IniSettings Class](#33-inisettings-class)
  - [3.4. Serialization Attributes](#34-serialization-attributes)
  - [3.5. Main IniFile Class](#35-main-inifile-class)
    - [3.5.1. Fields and Properties](#351-fields-and-properties)
    - [3.5.2. Factory Methods](#352-factory-methods)
    - [3.5.3. Save Methods](#353-save-methods)
    - [3.5.4. Static Quick Access Methods](#354-static-quick-access-methods)
    - [3.5.5. Data Read Methods](#355-data-read-methods)
    - [3.5.6. Data Write Methods](#356-data-write-methods)
    - [3.5.7. Presence Checks](#357-presence-checks)
    - [3.5.8. Change Tracking](#358-change-tracking)
    - [3.5.9. Dictionary Import/Export](#359-dictionary-importexport)
    - [3.5.10. Indexers](#3510-indexers)
- [4. Internal Mechanisms and Implementation Details](#4-internal-mechanisms-and-implementation-details)
  - [4.1. Regex-based Parsing](#41-regex-based-parsing)
  - [4.2. Multi-line Value Handling](#42-multi-line-value-handling)
  - [4.3. Escape Sequences](#43-escape-sequences)
  - [4.4. Automatic Formatting Detection](#44-automatic-formatting-detection)
  - [4.5. String Operation Optimization](#45-string-operation-optimization)
  - [4.6. Duplicate Key Handling](#46-duplicate-key-handling)
  - [4.7. Object Serialization](#47-object-serialization)
  - [4.8. Type Conversion](#48-type-conversion)
  - [4.9. JSON Path Navigation](#49-json-path-navigation)
  - [4.10. Environment Variable Expansion](#410-environment-variable-expansion)
  - [4.11. Change Tracking Internals](#411-change-tracking-internals)
  - [4.12. Security and Error Handling](#412-security-and-error-handling)
  - [4.13. Extensibility](#413-extensibility)
- [5. Usage Examples](#5-usage-examples)
  - [5.1. Basic Read/Write](#51-basic-readwrite)
  - [5.2. Working with JSON](#52-working-with-json)
  - [5.3. Navigating JSON by Path](#53-navigating-json-by-path)
  - [5.4. Object Serialization via Attributes](#54-object-serialization-via-attributes)
  - [5.5. Working with Duplicate Keys](#55-working-with-duplicate-keys)
  - [5.6. Change Tracking with WatchSettings](#56-change-tracking-with-watchsettings)
  - [5.7. Dictionary Round-trip](#57-dictionary-round-trip)
  - [5.8. Environment Variable Expansion](#58-environment-variable-expansion)
  - [5.9. Numbers in Different Radices](#59-numbers-in-different-radices)
  - [5.10. Culture and Decimal Separators](#510-culture-and-decimal-separators)
- [6. Strengths and Limitations](#6-strengths-and-limitations)
- [7. Recommended Use Cases](#7-recommended-use-cases)
- [8. Dependencies and Requirements](#8-dependencies-and-requirements)

---

## 1. General Purpose

The **IniFile** library is a powerful INI file parser and editor implemented in C#. Its **key feature** is **preserving the original formatting** when modifying data: all edits are performed directly on the file's text content, ensuring that comments, indentation, blank lines, and the order of elements remain intact.

The library supports:
- Reading and writing sections, keys, and values
- Multiple values for a single key
- Multi-line values enclosed in curly braces `{ ... }` or quotes
- Embedded JSON blocks (as raw strings, plain objects, or dynamic objects)
- JSON path navigation with multi-radix array indices
- Automatic object-to-INI mapping via attributes
- Change tracking of `INotifyPropertyChanged` objects
- Dictionary round-trip (export/import)
- Environment variable expansion
- Numbers in decimal, hexadecimal, octal, and binary notation
- Flexible parsing configuration (delimiters, comments, handling of undefined text)

---

## 2. Key Features

### 2.1. Formatting Preservation
- All changes are made by manipulating the original string (`StringBuilder`)
- Comments, whitespace, line breaks, and entry order remain unchanged
- When adding new elements, the automatically detected line break style (CRLF/LF/CR) is used
- The configured delimiter (`=`, `:`, or both) is used for new entries

### 2.2. Parsing Flexibility
- Configurable key-value delimiters (`=`, `:`, or both)
- Configurable comment characters (`#`, `;`, or both)
- Option to allow spaces in keys
- Support for quoted values (single or double quotes) with whitespace and line breaks preserved
- Support for escape sequences (`\n`, `\t`, `\uXXXX`, etc.)
- Modes for handling unrecognized text:
  - `Ignore` — treat as an error and ignore
  - `Key` — interpret as a key with no value (flag)
  - `Value` — interpret as a value with no key (line continuation)

### 2.3. Duplicate Key Support
- Control behavior: return first or last occurrence (`DuplicateKeyOverride`)
- `ReadStrings` always returns all values for a key

### 2.4. Multi-line Values
- Enable/disable via `AllowMultiLine`
- Values are wrapped in `{` and `}`, with line breaks preserved inside
- Quoted values can also span multiple lines when `AllowQuotedValues` is enabled

### 2.5. Built-in JSON Support
- Read/write JSON as raw strings without modification
- Parse JSON into `Dictionary<string, object>` or dynamic `ExpandoObject`
- Serialize objects to JSON with optional pretty printing
- Support for C-style comments (`//` and `/* */`) inside JSON blocks
- Depth-limited recursive parsing (max 64 levels)

### 2.6. JSON Path Navigation
- Navigate nested JSON values with slash- or backslash-separated paths: `"root/nested/number"`
- Array elements addressed by numeric index, which accepts decimal, hexadecimal, octal, and binary notation: `"items/0x2/name"`
- Read operations: `ReadJsonString`, `ReadJsonObject`, `ReadJsonDynamicObject` — each has a `path` overload
- Write operations: `WriteJsonObject`, `WriteJsonDynamicObject` — each has a `path` overload that modifies only the addressed node
- Missing intermediate objects are created automatically on write
- All failures (missing key, invalid JSON, path not resolved) return `defaultValue` without throwing

### 2.7. Attribute-based Serialization
- `[IniSection("Name")]` — sets the section name for a class or property
- `[IniEntry("Key")]` — sets the key name for a property
- `[IniIgnore]` — excludes a property from serialization
- `[IniExpanded]` — expands environment variables on read
- `[Dynamic]` — routes a dynamic property through JSON serialization
- Supports static and instance properties, nested types, and whole assemblies

### 2.8. Change Tracking
- `WatchSettings(INotifyPropertyChanged)` subscribes to property change notifications and writes changed properties back to the INI file as they change
- Returns an `IDisposable` that unsubscribes when disposed
- Skips properties marked `[IniIgnore]`
- Empty or null `PropertyName` in the notification writes all tracked properties
- The property name → `PropertyInfo` map is built once on subscription; no reflection lookups per event

### 2.9. Dictionary Round-trip
- `ExportToDictionary` and `ExportToDictionaryFile` — convert content to `Dictionary<string, Dictionary<string, List<string>>>`
- `ImportFromDictionary` and `ImportFromDictionaryFile` — write the dictionary back, either merging (default) or replacing (`replace: true`)
- Empty value list removes all occurrences of the corresponding key
- Global entries stored under the empty-string key
- Round-trip preserves multi-value keys, order of sections, and formatting of untouched entries

### 2.10. Environment Variable Expansion
- `ReadExpandedString` expands standard environment variables and pseudo-variables before returning the value
- Standard: any variable supported by `Environment.ExpandEnvironmentVariables` (`%TEMP%`, `%USERPROFILE%`, ...)
- Pseudo-variables emulating CMD dynamic variables: `%RANDOM%`, `%DATE%`, `%TIME%`, `%CD%`, `%__CD__%`, `%CMDCMDLINE%`, `%__APPDIR__%`, `%0`, `%1`..`%9`, `%*`
- Invalid variables are left unchanged
- `[IniExpanded]` attribute makes `ReadSettings` use `ReadExpandedString` for the marked property

### 2.11. Numbers in Different Radices
- Parse and format integers in decimal, hexadecimal, octal, and binary notation using common prefixes and suffixes:
  - Hex: `0x`, `&h`, `$`, `#`, `&`, trailing `h`
  - Binary: `0b`, `%`, trailing `b`
  - Octal: `0o`, `8#`, `&o`, trailing `o`
- Floating-point values respect the culture derived from `IniSettings.Comparison`
- The same parser is used for enum values, array indices in JSON paths, and all numeric `Read*` methods
- Enumerations support names, numeric values, and mixed flags combinations

### 2.12. Presence Checks
- `ContainsSection(section)` — fast check for the existence of a section
- `Contains(section, key)` — fast check for the existence of a key in a section (or global entries when `section` is `null`)
- Both use the cached match list; no value parsing is performed
- Distinguishes "key not present" from "key present with empty value"

### 2.13. Normalized Output
- `Justify()` produces a compact representation containing only sections and key-value pairs, without comments, empty lines, or extra whitespace
- The delimiter is taken from the configured `IniSettings.Delimiters`
- The line breaker is the auto-detected one from the source file
- `Save(..., justify: true)` writes the normalized form without modifying the `IniFile` instance

---

## 3. Library Structure

### 3.1. Namespace
`System.Ini`

### 3.2. Enums

| Enum | Purpose |
|------|---------|
| `IniDelimiterMode` | Key-value delimiters: `Equals`, `Colon`, `Both`, `Default` |
| `IniCommentMode` | Comment characters: `Hash`, `Semicolon`, `Both`, `Default` |
| `IniUndefinedTextMode` | Mode for handling unrecognized text: `Ignore`, `Key`, `Value` |

### 3.3. `IniSettings` Class

**Purpose:** Configuration for parsing INI files.

**Properties:**
- `Comparison` — `StringComparison` (default: `InvariantCultureIgnoreCase`). Also determines the `CultureInfo` used for parsing and formatting floating-point numbers.
- `AllowEscapeChars` — enable escape sequence processing
- `AllowMultiLine` — enable multi-line values in `{ }`
- `AllowQuotedValues` — enable quoted values
- `AllowSpacesInKey` — allow spaces in keys
- `AllowInlineComments` — allow comments after values on the same line
- `DuplicateKeyOverride` — if `true`, the last value overrides previous ones
- `Delimiters` — allowed delimiters
- `Comments` — allowed comment characters
- `UndefinedText` — mode for handling unrecognized text

**Methods:**
- Constructors with parameters for all settings
- Static method `Parse(string content)` — extracts settings from the INI file itself (sections named `#comparison`, `#escape_chars`, etc.)
- Internal methods for building regular expressions:
  - `BuildIniPatternEx()` — main regex pattern for INI
  - `BuildJsonPattern()` — regex pattern for JSON parsing

### 3.4. Serialization Attributes

| Attribute | Purpose |
|-----------|---------|
| `[IniIgnore]` | Marks a property to exclude from serialization |
| `[IniSection("name")]` | Sets the section name for a class or property. If not specified, the full type name (including nesting) is used |
| `[IniEntry("name")]` | Sets the key name for a property. If not specified, the property name is used |
| `[IniExpanded]` | Indicates that the value should be expanded via `ReadExpandedString` when read |
| `[Dynamic]` | Indicates that the property value should be read/written as JSON |

### 3.5. Main `IniFile` Class

#### 3.5.1. Fields and Properties

**Public Properties:**
- `string Content` — the content of the INI file. Setting this rebuilds the match cache.

**Internal Fields:**
- `_content` — string with content
- `_matches` — cache of regex matches (sections and entries only)
- `_iniRegex`, `_jsonRegex` — compiled regular expressions
- `_allowEscapeChars`, `_allowMultiLine`, `_allowOverrides` — setting flags
- `_lineBreaker` — automatically detected line break style (`\r\n`, `\n`, `\r`)
- `_defaultDelimiter` — default delimiter for new entries
- `_trueValues`, `_falseValues` — sets of strings for boolean parsing
- `_groupSection`, `_groupEntry`, `_groupKey`, `_groupValue` — regex group indices for quick access
- `_pathSeparatorChars` — separator characters for JSON paths (`/`, `\`)

#### 3.5.2. Factory Methods

- `Create(string content, IniSettings settings)` — from string
- `Create(IniSettings settings)` — empty
- `Load(TextReader reader, IniSettings settings)` — from `TextReader`
- `Load(Stream stream, Encoding encoding, IniSettings settings)` — from stream
- `Load(string fileName, Encoding encoding, IniSettings settings)` — from file with specified encoding
- `Load(string fileName, IniSettings settings)` — from file with auto-detected encoding
- `LoadOrCreate(...)` — loads if file exists, otherwise creates an empty one

#### 3.5.3. Save Methods

- `Save(TextWriter writer, bool justify = false)`
- `Save(Stream stream, Encoding encoding = null, bool justify = false)`
- `Save(string fileName, Encoding encoding = null, bool justify = false)`

When `justify` is `true`, the normalized representation from `Justify()` is written instead of `Content`. The `IniFile` instance is not modified.

#### 3.5.4. Static Quick Access Methods

- `ReadFromFile<T>(...)` — read a single value from a file without loading the entire object
- `WriteToFile<T>(...)` — write a single value to a file
- `ExportToDictionaryFile(...)` — export the entire file to a dictionary
- `ImportFromDictionaryFile(...)` — write a dictionary back to the file

#### 3.5.5. Data Read Methods

**Basic:**
- `ReadSections()` — list of all sections
- `ReadKeys(string section)` — list of keys in a section
- `ReadString(section, key, defaultValue)` — string value
- `ReadExpandedString(section, key, defaultValue)` — string value with environment variables expanded
- `ReadStrings(section, key, defaultValues)` — array of strings (all values for the key)
- `ReadJsonString(section, key, defaultValue)` — raw JSON string
- `ReadJsonString(section, key, path, defaultValue)` — raw JSON fragment at the given path
- `FormatString(section, key, defaultValue, args)` — read a format string and apply `string.Format`

**Typed:**
- `Read<T>(section, key, defaultValue, converter)` — generic method
- `ReadBoolean`, `ReadChar`, `ReadSByte`, `ReadByte`, `ReadInt16`, `ReadUInt16`, `ReadInt32`, `ReadUInt32`, `ReadInt64`, `ReadUInt64`, `ReadSingle`, `ReadDouble`, `ReadDecimal`, `ReadDateTime` — for primitive types
- `ReadArray(section, key, elementType, converter)` — array of elements
- `ReadArray<T>(...)` — typed version

**JSON Handling:**
- `ReadJsonObject(section, key, defaultValue)` — returns `object` (primitive, `object[]` array, or `IDictionary<string, object>`)
- `ReadJsonObject(section, key, path, defaultValue)` — returns the value at the given path
- `ReadJsonDynamicObject(section, key, defaultValue)` — returns `dynamic` (`ExpandoObject`-like)
- `ReadJsonDynamicObject(section, key, path, defaultValue)` — returns the value at the given path as `dynamic`

**Object Serialization:**
- `ReadProperty(PropertyInfo, object, defaultValue, converter)` — read a single property
- `ReadProperty(section, key, PropertyInfo, object, defaultValue, converter)` — read with explicit section/key
- `ReadSettings(object)` — read all properties of an instance
- `ReadSettings(Type)` — read static properties of a type and all nested types
- `ReadSettings(Assembly)` — read all types in an assembly

**Export:**
- `ExportToDictionary()` — export all content to a `Dictionary<string, Dictionary<string, List<string>>>` structure
- `Justify()` — returns a simplified INI representation without comments or extra whitespace, using the configured delimiter and auto-detected line breaker

#### 3.5.6. Data Write Methods

**Basic:**
- `WriteString(section, key, value)`
- `WriteStrings(section, key, params string[] values)` — write multiple values
- `WriteJsonString(section, key, value)` — write raw JSON string
- `RemoveKey(section, key)` — remove the first occurrence of a key
- `RemoveKeys(section, key)` — remove all occurrences of a key
- `RemoveSection(section)` — remove a section (including all entries)

**Typed:**
- `Write<T>(section, key, value, converter)` — generic method
- `WriteBoolean`, `WriteChar`, `WriteSByte`, `WriteByte`, `WriteInt16`, `WriteUInt16`, `WriteInt32`, `WriteUInt32`, `WriteInt64`, `WriteUInt64`, `WriteSingle`, `WriteDouble`, `WriteDecimal`, `WriteDateTime` — for primitive types
- `WriteArray(section, key, Array, converter)` — write an array
- `WriteArray<T>(section, key, params T[] array)` — typed version

**JSON Handling:**
- `WriteJsonObject(section, key, object value, bool beautify)` — serialize an object to JSON
- `WriteJsonObject(section, key, string path, object value, bool beautify)` — write a value at the given path
- `WriteJsonDynamicObject(section, key, dynamic value, bool beautify)` — serialize a dynamic object
- `WriteJsonDynamicObject(section, key, string path, dynamic value, bool beautify)` — write a value at the given path

**Object Serialization:**
- `WriteProperty(PropertyInfo, object, converter)` — write a single property
- `WriteProperty(section, key, PropertyInfo, object, converter)` — write with explicit section/key
- `WriteSettings(object)` — write all properties of an instance
- `WriteSettings(Type)` — write static properties of a type and nested types
- `WriteSettings(Assembly)` — write all types in an assembly

#### 3.5.7. Presence Checks

- `ContainsSection(string section)` — returns `true` if a section with the given name exists
- `Contains(string section, string key)` — returns `true` if the key exists in the specified section (or global entries if `section` is `null` or empty)

Both work on the cached match list and do not parse values, making them significantly cheaper than `ReadString(section, key, null) != null`.

#### 3.5.8. Change Tracking

- `WatchSettings(INotifyPropertyChanged obj)` — subscribes to property change notifications and writes changed properties back to the INI file as they change. Returns `IDisposable` for unsubscribing.

#### 3.5.9. Dictionary Import/Export

- `ImportFromDictionary(IDictionary<string, Dictionary<string, List<string>>> data, bool replace = false)` — merge or replace the content
- `ImportFromDictionaryFile(string fileName, ...)` — same, at the file level
- `ExportToDictionary()` and `ExportToDictionaryFile(...)` — the inverse operations

#### 3.5.10. Indexers

- `this[string section, string key]` — get/set string value
- `this[string section, string key, string defaultValue]` — get with default value

---

## 4. Internal Mechanisms and Implementation Details

### 4.1. Regex-based Parsing

**INI Parser:**
- Uses a single regular expression built from `IniSettings`
- Splits text into tokens: comments, sections, entries (key=value), unrecognized text, line breaks, whitespace
- Caches all matches for sections and entries in `_matches` to speed up repeated operations
- Regex groups are named (`section`, `entry`, `key`, `value`, `comment`, `undefined`, etc.)

**JSON Parser:**
- Separate regular expression for parsing JSON structures within values
- Supports nested objects and arrays with depth checking (max 64 levels)
- Skips C#-style comments (`//` and `/* */`) inside JSON blocks
- Returns objects as `Dictionary<string, object>` or `object[]` arrays

### 4.2. Multi-line Value Handling
- When reading: removes enclosing `{` and `}` and trims inner whitespace
- When writing: if the value contains `\r` or `\n`, it is automatically wrapped in `{` with line breaks
- Quoted values (`"..."` or `'...'`) can also span multiple lines when `AllowQuotedValues` is enabled
- Can be disabled via `AllowMultiLine = false`

### 4.3. Escape Sequences
- Supported: `\\`, `\0`, `\a`, `\b`, `\n`, `\r`, `\f`, `\t`, `\v`, `\uXXXX`, `\xXX`, `\cX`
- `UnEscape` and `ToEscape` methods are implemented with low memory usage (use `StringBuilder` only when necessary)

### 4.4. Automatic Formatting Detection
- **Line breaks:** analyzes CR and LF characters with a 10% threshold, selects the most frequent (`\r\n`, `\n`, `\r`)
- **Encoding:** when loading from a file, analyzes BOM and performs heuristic UTF-8/UTF-16 detection

### 4.5. String Operation Optimization
- Substring comparison is performed without memory allocation via `string.Compare(source, index, value, 0, length, comparison)`
- Case normalization is only applied to keys and sections when necessary
- String insertion and deletion are done via `StringBuilder` with offset adjustment
- The property name → `PropertyInfo` map in `WatchSettings` is built once on subscription

### 4.6. Duplicate Key Handling
- If `DuplicateKeyOverride = true`, methods like `ReadString`, `Read<T>`, etc., return the last value
- If `false`, they return the first value
- `ReadStrings` always returns all values in the order they appear

### 4.7. Object Serialization
- Supports static and instance properties
- Nested types are processed recursively
- Section names can be set at the class or individual property level (property takes precedence)
- Key names are set via the `[IniEntry]` attribute or derived from the property name
- Supports `DefaultValueAttribute` for specifying default values when reading
- `[IniExpanded]` routes reading through `ReadExpandedString`
- `[Dynamic]` routes reading/writing through JSON-dynamic methods

### 4.8. Type Conversion
- Uses `System.ComponentModel.TypeConverter` for flexible conversion
- For boolean values, supports synonyms: `true/false`, `yes/no`, `on/off`, `enable/disable`, `1/0`, as well as numeric values (0 = false, non-zero = true)
- For enums, supports parsing names and numeric values, including flags (comma-separated)
- For integers, uses the extended `ParseNumber` routine that supports decimal, hexadecimal, octal, and binary notation via common prefixes and suffixes
- For floating-point values, uses the culture derived from `IniSettings.Comparison`
- For byte arrays, uses hexadecimal representation with spaces between bytes

### 4.9. JSON Path Navigation
- Paths are split by `GetPathSegments` into segments using `/` and `\` as separators; empty segments are removed
- `TryNavigateJsonPath` walks the parsed JSON structure segment by segment:
  - For dictionaries (`IDictionary<string, object>`), the segment is used as a key
  - For arrays (`object[]`), the segment is parsed by `ParseNumber` (supporting decimal, hex, octal, and binary notation) and used as an index
  - If a primitive value is encountered mid-path, navigation fails
- `TrySetJsonPathValue` performs the same walk but creates intermediate dictionaries as needed, then assigns the value
- `ReadJsonString(section, key, path, defaultValue)` returns the raw JSON fragment using the token positions from `_jsonRegex`, preserving the original formatting
- All failures return the caller-supplied `defaultValue` without throwing

### 4.10. Environment Variable Expansion
- `ReadExpandedString` calls `ExpandVariables` on the raw value before returning it
- Standard environment variables are expanded via `Environment.ExpandEnvironmentVariables`
- Pseudo-variables (`%RANDOM%`, `%DATE%`, `%TIME%`, `%CD%`, `%__CD__%`, `%CMDCMDLINE%`, `%__APPDIR__%`, `%0`..`%9`, `%*`) are replaced manually
- Invalid variables are left unchanged, so the output is safe for arbitrary input
- `[IniExpanded]` makes `ReadSettings` use `ReadExpandedString` for the marked property

### 4.11. Change Tracking Internals
- `SettingsWatcher` is a private nested class implementing `IDisposable`
- On construction it builds a `Dictionary<string, PropertyInfo>` keyed by property name, using the comparer derived from `_comparison`
- Properties marked `[IniIgnore]` and write-only properties are skipped
- `PropertyChanged` handler:
  - Empty or null `PropertyName` → writes all tracked properties
  - Non-empty → writes only the matching property
- `Dispose` unsubscribes from the event and is idempotent

### 4.12. Security and Error Handling
- Recursion depth check during JSON parsing (max 64 levels)
- All read/write operations are wrapped in try-catch blocks, returning default values on errors
- File path validation (invalid characters, file existence when required)
- No exceptions are thrown from JSON path operations — all failures return `defaultValue`

### 4.13. Extensibility
- `IniSettings` allows full control over parser behavior
- Regex-building methods are protected for possible overriding (though the class is `sealed`)
- Supports custom `TypeConverter` for user-defined types
- `WatchSettings` can be combined with any `INotifyPropertyChanged` implementation

---

## 5. Usage Examples

### 5.1. Basic Read/Write

```ini
; config.ini
[Section]
Key = old value
```

```csharp
var ini = IniFile.Load("config.ini");
string value = ini.ReadString("Section", "Key", "default");
ini.WriteString("Section", "Key", "new value");
ini.Save("config.ini");
```

### 5.2. Working with JSON

```csharp
var data = new { name = "test", values = new[] { 1, 2, 3 } };
ini.WriteJsonObject("Section", "JsonData", data, beautify: true);
var restored = ini.ReadJsonDynamicObject("Section", "JsonData");
Console.WriteLine(restored.name); // "test"
```

### 5.3. Navigating JSON by Path

```ini
[test]
json = { "root": { "nested": { "number": 42 }, "list": [10, 20, 30] } }
```

```csharp
// Read a primitive at any depth.
int n = ini.ReadJsonObject("test", "json", "root/nested/number", -1);
// → 42

// Read a subtree as a plain object.
object nested = ini.ReadJsonObject("test", "json", "root/nested");

// Read as dynamic.
dynamic root = ini.ReadJsonDynamicObject("test", "json", "root");
int v = root.nested.number;

// Read the raw JSON fragment, preserving its original formatting.
string raw = ini.ReadJsonString("test", "json", "root/nested");
// → "{ \"number\": 42 }"

// Array indices support hex, octal, and binary notation.
ini.ReadJsonString("test", "json", "root/list/0x1"); // → "20"
ini.ReadJsonString("test", "json", "root/list/0b10"); // → "30"

// Write a value at the given path, creating intermediate nodes as needed.
ini.WriteJsonObject("test", "json", "root/extra/deep/value", "hi");
// → json = { "root": { "nested": { "number": 42 },
//                       "list": [10, 20, 30],
//                       "extra": { "deep": { "value": "hi" } } } }
```

### 5.4. Object Serialization via Attributes

```ini
[Database]
Host = localhost
Port = 5432
LogPath = %TEMP%\db.log
```

```csharp
[IniSection("Database")]
public class DbConfig
{
    [IniEntry("Host")]
    public string Host { get; set; } = "localhost";

    [IniEntry("Port")]
    public int Port { get; set; } = 5432;

    [IniEntry("LogPath")]
    [IniExpanded]
    public string LogPath { get; set; }
}

var config = new DbConfig();
var ini = IniFile.LoadOrCreate("config.ini");
ini.ReadSettings(config);    // load from file
ini.WriteSettings(config);   // save to file
```

### 5.5. Working with Duplicate Keys

```csharp
var settings = new IniSettings { DuplicateKeyOverride = true };
var ini = IniFile.Create(content, settings);
string last = ini.ReadString("Section", "Key"); // returns the last value
string[] all = ini.ReadStrings("Section", "Key"); // all values
```

### 5.6. Change Tracking with WatchSettings

```ini
[Server]
Host = localhost
Port = 8080
```

```csharp
public class AppConfig : INotifyPropertyChanged
{
    private string _host = "localhost";
    private int _port = 8080;

    [IniSection("Server"), IniEntry("Host")]
    public string Host
    {
        get => _host;
        set { _host = value; OnPropertyChanged(nameof(Host)); }
    }

    [IniSection("Server"), IniEntry("Port")]
    public int Port
    {
        get => _port;
        set { _port = value; OnPropertyChanged(nameof(Port)); }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

var ini = IniFile.LoadOrCreate("config.ini");
var config = new AppConfig();

ini.WriteSettings(config); // capture initial state
using (ini.WatchSettings(config))
{
    config.Host = "10.0.0.1"; // immediately written to the INI file
    config.Port = 9090;        // and so is this
}
```

### 5.7. Dictionary Round-trip

```ini
theme = dark

[Server]
host = localhost
port = 8080

[Paths]
include = /opt/data/
include = /mnt/backup/
```

```csharp
// Export: section → key → list of values.
var snapshot = ini.ExportToDictionary();
// snapshot[""]       → { "theme"   → ["dark"] }
// snapshot["Server"] → { "host"    → ["localhost"], "port" → ["8080"] }
// snapshot["Paths"]  → { "include" → ["/opt/data/", "/mnt/backup/"] }

// Import: merge into the existing content.
ini.ImportFromDictionary(snapshot);

// Or replace the entire content.
ini.ImportFromDictionary(snapshot, replace: true);

// File-based round-trip.
var dict = IniFile.ExportToDictionaryFile("messy.ini");
IniFile.ImportFromDictionaryFile("clean.ini", dict, replace: true);
```

### 5.8. Environment Variable Expansion

```ini
[Logging]
Path = %TEMP%\app.log
Backup = %USERPROFILE%\backup\%DATE%_%TIME%.log
SessionDir = %CD%\session_%RANDOM%
```

```csharp
string path = ini.ReadExpandedString("Logging", "Path");
// → C:\Users\Alice\AppData\Local\Temp\app.log

string backup = ini.ReadExpandedString("Logging", "Backup");
// → C:\Users\Alice\backup\20260912_143022.log

string session = ini.ReadExpandedString("Logging", "SessionDir");
// → C:\Work\MyApp\session_1234567890
```

### 5.9. Numbers in Different Radices

```ini
[Masks]
Read = 0x01
Write = 0x02
Execute = 0x04
All = 0x07

[Flags]
Alpha = &h01
Beta = %10
Gamma = 0o4

[BitPattern]
Top = 1000b
Bottom = $0F
```

```csharp
int read   = ini.ReadInt32("Masks", "Read");        // 1
int write  = ini.ReadInt32("Masks", "Write");       // 2
int all    = ini.ReadInt32("Masks", "All");         // 7
int beta   = ini.ReadInt32("Flags", "Beta");        // 2
int top    = ini.ReadInt32("BitPattern", "Top");    // 8
int bottom = ini.ReadInt32("BitPattern", "Bottom"); // 15
```

### 5.10. Culture and Decimal Separators

The culture for floating-point parsing is derived from `IniSettings.Comparison`. The default (`InvariantCultureIgnoreCase`) always treats `.` as the decimal separator; `CurrentCulture*` uses the running system's locale.

```ini
[Measurements]
Invariant = 3.14
Localized = 3,14
Scientific = 1.5e-3
```

```csharp
// Invariant culture (the default).
var inv = IniFile.Load("m.ini");
double a = inv.ReadDouble("Measurements", "Invariant");        // 3.14
double b = inv.ReadDouble("Measurements", "Localized", 0.0);   // 0.0 — not parsed
double s = inv.ReadDouble("Measurements", "Scientific");       // 0.0015

// Current culture (e.g. Russian, German).
var settings = new IniSettings { Comparison = StringComparison.CurrentCultureIgnoreCase };
var cur = IniFile.Load("m.ini", settings);
double a2 = cur.ReadDouble("Measurements", "Invariant", 0.0);  // 0.0 — not parsed
double b2 = cur.ReadDouble("Measurements", "Localized");       // 3.14
```

---

## 6. Strengths and Limitations

### Strengths:
- **Formatting Preservation** — ideal for manually edited files
- **High Performance** — match caching, minimized allocations, cached property maps
- **Flexibility** — many parsing settings; multi-radix numbers; culture-aware floating point
- **Rich JSON Support** — path navigation, dynamic objects, raw fragment access
- **Extensibility** — support for custom types via `TypeConverter`
- **Change Tracking** — `WatchSettings` binds model changes to INI persistence with a single `using` block
- **Safety** — error handling, stack overflow protection, no exceptions from JSON path operations

### Limitations:
- **Regular Expressions** — parsing depends on pattern complexity, but they are optimized
- **Memory** — the entire file is loaded into memory (not streamed)
- **.NET Framework** — requires .NET, not cross-platform on older versions. The library is written for .NET Standard 2.0, which ensures compatibility with:
  - .NET Core 2.0+,
  - .NET 5+,
  - .NET Framework 4.6.1+,
  - Mono 5.4+,
  - Xamarin.iOS / Xamarin.Android,
  - UWP and other implementations that support .NET Standard 2.0.

  It is not tied to Windows and works on Linux, macOS, and any other systems that support .NET Standard 2.0. However, .NET Framework versions lower than 4.6.1 do not support .NET Standard 2.0, so the library will not work on such outdated platforms. This limitation only applies to older versions of the .NET Framework and is not a problem for modern projects that can be deployed on any operating system.
- **Encoding** — auto-detection is heuristic and may be inaccurate
- **Thread safety** — `IniFile` instances are not thread-safe; `WatchSettings` writes are not synchronised
- **Read notifications** — .NET has no built-in mechanism to subscribe to property *reads*; only changes are tracked

---

## 7. Recommended Use Cases

- For **configuration files** where readability and manual editing are important
- For **import/export** of data in a simple text format
- For **storing application settings** with commenting support
- For **embedding JSON** in INI files for complex structures
- For **navigating JSON** configurations without deserializing the whole structure
- For **wiring a live configuration model** to a file via `WatchSettings`
- For **migrating** from classic INI parsers to a more flexible tool

---

## 8. Dependencies and Requirements

- **.NET Standard 2.0** or higher
- Namespaces: `System`, `System.Text`, `System.Text.RegularExpressions`, `System.Globalization`, `System.ComponentModel`, `System.Reflection`, `System.Diagnostics`, `System.Collections`, `System.Dynamic`
- `#nullable disable` support for backward compatibility with older projects
