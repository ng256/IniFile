# IniFile Library: Detailed Description

## Contents

- [1. General Purpose](#1-general-purpose)
- [2. Key Features](#2-key-features)
  - [2.1. Formatting Preservation](#21-formatting-preservation)
  - [2.2. Parsing Flexibility](#22-parsing-flexibility)
  - [2.3. Duplicate Key Support](#23-duplicate-key-support)
  - [2.4. Multi-line Values](#24-multi-line-values)
  - [2.5. Built-in JSON Support](#25-built-in-json-support)
  - [2.6. Attribute-based Serialization](#26-attribute-based-serialization)
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
    - [3.5.7. Indexers](#357-indexers)
- [4. Internal Mechanisms and Implementation Details](#4-internal-mechanisms-and-implementation-details)
  - [4.1. Regex-based Parsing](#41-regex-based-parsing)
  - [4.2. Multi-line Value Handling](#42-multi-line-value-handling)
  - [4.3. Escape Sequences](#43-escape-sequences)
  - [4.4. Automatic Formatting Detection](#44-automatic-formatting-detection)
  - [4.5. String Operation Optimization](#45-string-operation-optimization)
  - [4.6. Duplicate Key Handling](#46-duplicate-key-handling)
  - [4.7. Object Serialization](#47-object-serialization)
  - [4.8. Type Conversion](#48-type-conversion)
  - [4.9. Security and Error Handling](#49-security-and-error-handling)
  - [4.10. Extensibility](#410-extensibility)
- [5. Usage Examples](#5-usage-examples)
  - [5.1. Basic Read/Write](#51-basic-readwrite)
  - [5.2. Working with JSON](#52-working-with-json)
  - [5.3. Object Serialization via Attributes](#53-object-serialization-via-attributes)
  - [5.4. Working with Duplicate Keys](#54-working-with-duplicate-keys)
- [6. Strengths and Limitations](#6-strengths-and-limitations)
- [7. Recommended Use Cases](#7-recommended-use-cases)
- [8. Dependencies and Requirements](#8-dependencies-and-requirements)

---

## 1. General Purpose

The **IniFile** library is a powerful INI file parser and editor implemented in C#. Its **key feature** is **preserving the original formatting** when modifying data: all edits are performed directly on the file’s text content, ensuring that comments, indentation, blank lines, and the order of elements remain intact.

The library supports:
- Reading and writing sections, keys, and values
- Multiple values for a single key
- Multi-line values enclosed in curly braces `{ ... }`
- Embedded JSON blocks (as raw strings or dynamic objects)
- Automatic object-to-INI mapping via attributes
- Flexible parsing configuration (delimiters, comments, handling of undefined text)

---

## 2. Key Features

### 2.1. Formatting Preservation
- All changes are made by manipulating the original string (`StringBuilder`)
- Comments, whitespace, line breaks, and entry order remain unchanged
- When adding new elements, the automatically detected line break style (CRLF/LF) is used

### 2.2. Parsing Flexibility
- Configurable key-value delimiters (`=`, `:`, or both)
- Configurable comment characters (`#`, `;`, or both)
- Option to allow spaces in keys
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

### 2.5. Built-in JSON Support
- Read/write JSON as raw strings without modification
- Parse JSON into `Dictionary<string, object>` or dynamic `ExpandoObject`
- Serialize objects to JSON with optional pretty printing

### 2.6. Attribute-based Serialization
- `[IniSection("Name")]` — sets the section name for a class or property
- `[IniEntry("Key")]` — sets the key name for a property
- `[IniIgnore]` — excludes a property from serialization
- Supports static and instance properties, nested types

---

## 3. Library Structure

### 3.1. Namespace
`System.Ini`

### 3.2. Enums


Enums


| Enum | Purpose |
|------|---------|
| `IniDelimiterMode` | Key-value delimiters: `Equals`, `Colon`, `Both`, `Default` |
| `IniCommentMode` | Comment characters: `Hash`, `Semicolon`, `Both`, `Default` |
| `IniUndefinedTextMode` | Mode for handling unrecognized text: `Ignore`, `Key`, `Value` |

### 3.3. `IniSettings` Class

**Purpose:** Configuration for parsing INI files.

**Properties:**
- `Comparison` — `StringComparison` (default: `InvariantCultureIgnoreCase`)
- `AllowEscapeChars` — enable escape sequence processing
- `AllowMultiLine` — enable multi-line values in `{ }`
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


Serialization Attributes


| Attribute | Purpose |
|-----------|---------|
| `[IniIgnore]` | Marks a property to exclude from serialization |
| `[IniSection("name")]` | Sets the section name for a class or property. If not specified, the full type name (including nesting) is used |
| `[IniEntry("name")]` | Sets the key name for a property. If not specified, the property name is used |

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

#### 3.5.2. Factory Methods

- `Create(string content, IniSettings settings)` — from string
- `Create(IniSettings settings)` — empty
- `Load(TextReader reader, IniSettings settings)` — from `TextReader`
- `Load(Stream stream, Encoding encoding, IniSettings settings)` — from stream
- `Load(string fileName, Encoding encoding, IniSettings settings)` — from file with specified encoding
- `Load(string fileName, IniSettings settings)` — from file with auto-detected encoding
- `LoadOrCreate(...)` — loads if file exists, otherwise creates an empty one

#### 3.5.3. Save Methods

- `Save(TextWriter writer)`
- `Save(Stream stream, Encoding encoding)`
- `Save(string fileName, Encoding encoding)`

#### 3.5.4. Static Quick Access Methods

- `ReadFromFile<T>(...)` — read a single value from a file without loading the entire object
- `WriteToFile<T>(...)` — write a single value to a file
- `ExportToDictionaryFile(...)` — export the entire file to a dictionary

#### 3.5.5. Data Read Methods

**Basic:**
- `ReadSections()` — list of all sections
- `ReadKeys(string section)` — list of keys in a section
- `ReadString(section, key, defaultValue)` — string value
- `ReadStrings(section, key, defaultValues)` — array of strings (all values for the key)
- `ReadJsonString(section, key, defaultValue)` — raw JSON string (without wrapping)

**Typed:**
- `Read<T>(section, key, defaultValue, converter)` — generic method
- `ReadBoolean`, `ReadChar`, `ReadSByte`, `ReadByte`, `ReadInt16`, `ReadUInt16`, `ReadInt32`, `ReadUInt32`, `ReadInt64`, `ReadUInt64`, `ReadSingle`, `ReadDouble`, `ReadDecimal`, `ReadDateTime` — for primitive types
- `ReadArray(section, key, elementType, converter)` — array of elements
- `ReadArray<T>(...)` — typed version

**JSON Handling:**
- `ReadJsonObject(section, key, defaultValue)` — returns `object` (primitive, `object[]` array, or `IDictionary<string, object>`)
- `ReadJsonDynamicObject(section, key, defaultValue)` — returns `dynamic` (`ExpandoObject`-like)

**Object Serialization:**
- `ReadProperty(PropertyInfo, object, defaultValue, converter)` — read a single property
- `ReadSettings(object)` — read all properties of an instance
- `ReadSettings(Type)` — read static properties of a type and all nested types
- `ReadSettings(Assembly)` — read all types in an assembly

**Formatting:**
- `FormatString(section, key, defaultValue, args)` — read a format string and apply `string.Format`

**Export:**
- `ExportToDictionary()` — export all content to a `Dictionary<string, Dictionary<string, List<string>>>` structure
- `Justify()` — returns a simplified INI representation without comments or extra whitespace

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
- `WriteJsonDynamicObject(section, key, dynamic value, bool beautify)` — serialize a dynamic object

**Object Serialization:**
- `WriteProperty(PropertyInfo, object, converter)` — write a single property
- `WriteSettings(object)` — write all properties of an instance
- `WriteSettings(Type)` — write static properties of a type and nested types
- `WriteSettings(Assembly)` — write all types in an assembly

#### 3.5.7. Indexers

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

### 4.8. Type Conversion
- Uses `System.ComponentModel.TypeConverter` for flexible conversion
- For boolean values, supports synonyms: `true/false`, `yes/no`, `on/off`, `enable/disable`, `1/0`, as well as numeric values (0 = false, non-zero = true)
- For enums, supports parsing names and numeric values, including flags (comma-separated)
- For byte arrays, uses hexadecimal representation with spaces between bytes

### 4.9. Security and Error Handling
- Recursion depth check during JSON parsing (max 64 levels)
- All read/write operations are wrapped in try-catch blocks, returning default values on errors
- File path validation (invalid characters, file existence when required)

### 4.10. Extensibility
- `IniSettings` allows full control over parser behavior
- Regex-building methods are protected for possible overriding (though the class is `sealed`)
- Supports custom `TypeConverter` for user-defined types

---

## 5. Usage Examples

### 5.1. Basic Read/Write

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

### 5.3. Object Serialization via Attributes

```csharp
[IniSection("Database")]
public class DbConfig
{
    [IniEntry("Host")]
    public string Host { get; set; } = "localhost";

    [IniEntry("Port")]
    public int Port { get; set; } = 5432;
}

var config = new DbConfig();
var ini = IniFile.LoadOrCreate("config.ini");
ini.ReadSettings(config);    // load from file
ini.WriteSettings(config);   // save to file
```

### 5.4. Working with Duplicate Keys

```csharp
var settings = new IniSettings { DuplicateKeyOverride = true };
var ini = IniFile.Create(content, settings);
string last = ini.ReadString("Section", "Key"); // returns the last value
string[] all = ini.ReadStrings("Section", "Key"); // all values
```

---

## 6. Strengths and Limitations

### Strengths:
- **Formatting Preservation** — ideal for manually edited files
- **High Performance** — match caching, minimized allocations
- **Flexibility** — many parsing settings
- **Extensibility** — support for custom types via `TypeConverter`
- **Safety** — error handling, stack overflow protection

### Limitations:
- **Regular Expressions** — parsing depends on pattern complexity, but they are optimized
- **Memory** — the entire file is loaded into memory (not streamed)
- **.NET Framework** — requires .NET, not cross-platform on older versions
- **Encoding** — auto-detection is heuristic and may be inaccurate

---

## 7. Recommended Use Cases

- For **configuration files** where readability and manual editing are important
- For **import/export** of data in a simple text format
- For **storing application settings** with commenting support
- For **embedding JSON** in INI files for complex structures
- For **migrating** from classic INI parsers to a more flexible tool

---

## 8. Dependencies and Requirements

- **.NET Standard 2.0** or higher
- Namespaces: `System`, `System.Text`, `System.Text.RegularExpressions`, `System.Globalization`, `System.ComponentModel`, `System.Reflection`, `System.Diagnostics`, `System.Collections`, `System.Dynamic`
- `#nullable disable` support for backward compatibility with older projects
