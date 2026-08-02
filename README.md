# IniFile — Convenient Single-file INI Editor for .NET
A single-file, dependency-free INI reader and editor that modifies configuration files without destroying their formatting.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![TestRegex](https://img.shields.io/badge/Test-Regex-blue)](https://regex101.com/r/mul0C2/13)

**IniFile** is a lightweight INI parser that is tolerant of malformed files. Unlike traditional dictionary-based implementations, it **preserves the original formatting** — including whitespace, comments, line endings, and entry order — by modifying the original text directly instead of rebuilding the file.

It provides a convenient API for reading, writing, and deleting values, as well as handling multi-line **JSON blocks** embedded in INI files. The library consists of a single source file and has no external dependencies. Drop one file into your project and edit INI files without destroying their formatting.

See [Details](https://github.com/ng256/IniFile/blob/main/Details.md) document for more information.

---

## Key Features

- **Read & write sections, keys, and values** – standard operations with configurable case sensitivity.
- **Multiple values** – supports duplicate keys in the same section (e.g., for arrays).
- **Deletion** – remove a single key, all keys with the same name, or entire sections.
- **Global entries** – work with key‑value pairs outside any section by passing `null` or an empty string as the section name.
- **Object serialization** – automatically map INI data to classes using attributes.
- **Multi-line JSON blocks** – read and write JSON blocks that may span multiple lines and include C-like comments. Work with JSON as raw strings or as dynamic objects.
- **Flexible handling of unrecognised text** – treat otherwise unparseable lines as undefined, as keys with empty values (flags), or as values with empty keys (line continuations).
- **Duplicate key control** – choose whether reading a duplicated key returns the first occurrence or the last (override mode).
- **Preserve formatting** – changes modify only the necessary parts, leaving the rest of the file intact.
- **Static helper methods** – quick one‑liners for reading/writing a single value without creating an instance.
- **Escape characters** – optional support for `\n`, `\t`, etc.
- **Auto‑detection** of line endings and encoding.
- **Flexible configuration** – centralised settings via `IniSettings` class (delimiters, comment characters, case sensitivity, undefined text mode, duplicate key override, etc.).

---

## Contents.  

1. [Installation](#installation)
2. [Usage](#usage)
    - [Loading and Saving](#loading-and-saving)
    - [Reading and Writing Simple Values](#reading-and-writing-simple-values)
    - [Working with Multiple Values (Arrays)](#working-with-multiple-values-arrays)
    - [Deleting Entries](#deleting-entries)
3. [JSON Support](#json-support)
    - [Read/Write JSON as Raw String](#readwrite-json-as-raw-string)
    - [Read/Write JSON as Object](#readwrite-json-as-object)
4. [Object Serialization with Attributes](#object-serialization-with-attributes)
5. [Static Helper Methods](#static-helper-methods)
6. [Configuration with `IniSettings`](#configuration-with-inisettings)
7. [Embedded Parser Settings (Directives)](#embedded-parser-settings-directives)
8. [Full API Reference](#full-api-reference)
9. [Background](#background)
    - [INI File Format](#ini-file-format)
    - [Regular Expression](#regular-expression)
    - [C# Implementation](#c-implementation)
10. [License](#license)

## Installation

Simply add `IniFile.cs` to your project and start using it. No external dependencies.

---

## Usage

### Loading and Saving

```csharp
using System.Ini;

// Load from file using default settings.
var ini = IniFile.Load("config.ini");

// Or create a new empty instance.
ini = IniFile.Create();

// Load the file or create a new one if it does not exist.
ini = IniFile.LoadOrCreate("config.ini");

// Save changes.
ini.Save("config.ini");
```

You can customize the parser behavior by passing an `IniSettings` object. The settings control string comparison, escape character processing, multiline support, allowed delimiters, comment styles, handling of spaces in keys, interpretation of unrecognised text, and duplicate key behaviour.

```csharp
using System.Text;
using System.Ini;

// Create custom settings.
var settings = new IniSettings
{
    Comparison = StringComparison.OrdinalIgnoreCase,
    AllowEscapeChars = false,
    AllowMultiLine = true,
    Delimiters = IniDelimiterMode.Equals,      // only '='
    Comments = IniCommentMode.Hash,            // only '#'
    AllowSpacesInKey = true,
    UndefinedTextMode = IniUndefinedTextMode.Key,  // bare words become flags
    DuplicateKeyOverride = true                     // last value wins on duplicates
};

// Load with custom settings.
var ini = IniFile.Load("config.ini", settings);

// Or with encoding.
ini = IniFile.Load("config.ini", Encoding.UTF8, settings);

// Create an empty file with custom settings.
ini = IniFile.Create(settings);

// Save with encoding.
ini.Save("config.ini", Encoding.UTF8);
```

For convenience, legacy overloads are still available but marked as obsolete. They internally use `IniSettings` with default values.

### Reading and Writing Simple Values

**Example INI content:**

```ini
; Application configuration

[Host]
Network = localhost
Port = 8080

[Environment]
LogDirectory = /var/log/myapp

; Multiline shell script
Script =
{
#!/bin/sh

echo "Starting..."

mkdir -p /var/cache/myapp
cp -r /opt/data/* /var/cache/myapp/

echo "Done."
}

[SearchPaths]
; Duplicate keys are supported
Path = /opt/data/
Path = /mnt/backup/
Path = /var/cache/myapp
```

**Working with file:**

```csharp
// Read values.
string network = ini.ReadString("Host", "Network", "localhost");
int port = ini.ReadInt32("Host", "Port", 8080);

// Returns the default value (true) because the key is not found.
bool enabled = ini.ReadBoolean("Network", "Enabled", true);

// The surrounding braces are removed automatically.
string script = ini.ReadString("Environment", "Script"); 

// Reads all values with the same key.
string[] paths = ini.ReadStrings("SearchPaths", "Path");

// Write values.
ini.WriteString("Host", "Network", "192.168.1.1");
ini.WriteInt32("Host", "Port", 9090);
ini.WriteBoolean("Network", "Enabled", false);
```

### Working with Multiple Values (Arrays)

```csharp
// Write array.
ini.WriteStrings("Servers", "Address", "10.0.0.1", "10.0.0.2", "10.0.0.3");

// Read array.
string[] addresses = ini.ReadStrings("Servers", "Address");
```

### Deleting Entries

```csharp
// Remove first occurrence of a key.
ini.RemoveKey("Network", "Port");

// Remove all occurrences of a key.
ini.RemoveKeys("Servers", "Address");

// Remove entire section (all occurrences).
ini.RemoveSection("Servers");
```

---

## JSON Support

Although the INI format does not define support for structured data, many applications store custom blocks inside INI files. `IniFile` extends the format by supporting embedded JSON and multiline brace-enclosed values, while keeping the original INI structure intact.

**Example INI content:**

```ini
[App]
config = {
  "timeout": 30,
  "retry": 5
}

[Data]
json = [1, 2, 3, 4]
```

### Read/Write JSON as Raw String

```csharp
string json = ini.ReadJsonString("App", "config", "{}");
ini.WriteJsonString("App", "config", "{\"timeout\":60,\"retry\":10}");
```

### Read/Write JSON as Object

```csharp
// Read JSON as a dictionary/object.
object obj = ini.ReadJsonObject("App", "config");
if (obj is IDictionary<string, object> dict)
{
    int timeout = Convert.ToInt32(dict["timeout"]);
    dict["retry"] = 10;
    ini.WriteJsonObject("App", "config", dict, beautify: true);
}

// Alternatively, use dynamic for convenience.
dynamic dyn = ini.ReadJsonDynamicObject("App", "config");
int timeout = dyn.timeout;
dyn.retry = 10;
ini.WriteJsonDynamicObject("App", "config", dyn, beautify: true);
```

The `beautify` option formats the JSON with indentation and newlines for better readability.

---

## Object Serialization with Attributes

Automatically map INI sections to classes and properties.

```csharp
[IniSection("Network")]
class NetworkSettings
{
    public string Host { get; set; } = "localhost";
    [IniEntry("Port")]  // Maps the property to a different INI key name.
    public int ConnectionPort { get; set; } = 8080;
    [IniIgnore] // Prevents the property from being read or written.
    public string Comment { get; set; }
}

var ini = IniFile.Load("config.ini");
var settings = new NetworkSettings();
ini.ReadSettings(settings);   // Reads values from the INI file.
// ... make changes to settings.
ini.WriteSettings(settings);  // Writes values back to the INI file.
```

---

## Static Helper Methods

For quick access to file data without creating an instance:

```csharp
// Read/write a single value.
int port = IniFile.ReadFromFile<int>("config.ini", "Network", "Port", 8080);
IniFile.WriteToFile("config.ini", "Network", "Port", 9090);

// Convert the file contents to a dictionary representation
// (returns empty dictionary if file not found).
var dict = IniFile.ExportToDictionary("config.ini");
foreach (var section in dict)
{
    Console.WriteLine($"[{section.Key}]");
    foreach (var entry in section.Value)
    {
        Console.WriteLine($"  {entry.Key} = {string.Join(", ", entry.Value)}");
    }
}
```

Overloads with `Encoding` and `IniSettings` parameters are also available.

---

## Configuration with `IniSettings`

All parser behaviour is centralised in the `IniSettings` class. It allows you to fine‑tune how the INI file is interpreted.

### Settings Overview

| Property | Type | Description |
|----------|------|-------------|
| `Comparison` | `StringComparison` | Case sensitivity and culture rules (default: `InvariantCultureIgnoreCase`). |
| `AllowEscapeChars` | `bool` | If `true`, escape sequences like `\n` and `\t` are unescaped in values (default: `true`). |
| `AllowMultiLine` | `bool` | If `true`, values wrapped in `{ ... }` can span multiple lines (default: `true`). |
| `AllowSpacesInKey` | `bool` | If `true`, key names may contain spaces (default: `false`). |
| `DuplicateKeyOverride` | `bool` | If `true`, reading a duplicated key returns the last value (override mode); if `false` (default), returns the first. Does not affect `ReadStrings`. |
| `Delimiters` | `IniDelimiterMode` | Allowed delimiters: `Equals`, `Colon`, `Both` (default: `Both`). |
| `Comments` | `IniCommentMode` | Allowed comment characters: `Hash`, `Semicolon`, `Both` (default: `Both`). |
| `UndefinedTextMode` | `IniUndefinedTextMode` | How to interpret unrecognised text: `Undefined` (keep as undefined), `Key` (treat as key with empty value, i.e. flags), `Value` (treat as value with empty key). |

The `IniSettings.Default` property provides a preconfigured instance with the default settings, which you can use as a base for customisation.

### Example

```csharp
var settings = new IniSettings
{
    Comparison = StringComparison.Ordinal,
    Delimiters = IniDelimiterMode.Equals,
    Comments = IniCommentMode.Hash,
    AllowSpacesInKey = false,
    UndefinedTextMode = IniUndefinedTextMode.Key,   // bare words become flags
    DuplicateKeyOverride = true                      // last value wins
};
var ini = IniFile.Load("config.ini", settings);
```

## Embedded Parser Settings (Directives)

You can control parser behaviour directly from the INI file itself, without passing an `IniSettings` object in code. Settings are defined in the **global section** (entries outside any named section) using keys prefixed with `#`.

### Advantages

- The INI file becomes **self‑descriptive** – it carries its own parsing rules.
- No need to specify `IniSettings` in code; useful when distributing configuration files across different environments.
- But if you pass an explicit `IniSettings` object to the constructor, the embedded directives are **ignored** (code settings take precedence).

### Syntax

- **Boolean options** – just write the directive alone, e.g. `#space_in_key`. Presence means `true`, absence means `false`.
- **Other options** – use `#directive = value`, e.g. `#comparison = ordinal`.

### Supported Directives

| Directive | Type | Allowed Values | Default |
|-----------|------|----------------|---------|
| `#comparison` | `StringComparison` | `current`, `currentignorecase`, `invariant`, `invariantignorecase`, `ordinal`, `ordinalignorecase` | `invariantignorecase` |
| `#escape_chars` | `bool` | flag or `true`/`false` | `true` |
| `#muli_line` | `bool` | flag or `true`/`false` | `true` |
| `#space_in_key` | `bool` | flag or `true`/`false` | `false` |
| `#inline_comment` | `bool` | flag or `true`/`false` | `true` |
| `#dup_key_overrides` | `bool` | flag or `true`/`false` | `false` |
| `#delimiter` | `IniDelimiterMode` | `equals`, `colon`, `both` | `both` |
| `#comment` | `IniCommentMode` | `hash`, `semicolon`, `both` | `both` |
| `#undef_text` | `IniUndefinedTextMode` | `ignore`, `key`, `value` | `ignore` |

### Example

```ini
#comparison = ordinal
#space_in_key
#escape_chars = false

; Normal application settings
[General]
AppName = MyApp
Version = 1.0
```

Here:
- `#comparison = ordinal` → case‑sensitive keys and sections.
- `#space_in_key` → allows spaces in key names.
- `#escape_chars = false` → disables escape‑sequence processing.

```csharp
// Load without explicit settings – directives are applied automatically.
var ini = IniFile.Load("config.ini");

// Because #comparison = ordinal, this will be case-sensitive.
string appName = ini.ReadString("General", "AppName");  // works.
string appNameLower = ini.ReadString("general", "appname"); // returns null.

// #space_in_key is present, so keys with spaces are allowed.
ini.WriteString(null, "My Key", "Some Value");
```

If you want to **override** the directives and use your own settings:

```csharp
var customSettings = new IniSettings
{
    Comparison = StringComparison.InvariantCultureIgnoreCase
};
// Directives from the file will be ignored.
var ini = IniFile.Load("config.ini", customSettings);
```

---

## Full API Reference

| Category | Methods |
|----------|---------|
| **Read keys and sections** | `ReadSections()`, `ReadKeys(string section)` |
| **Read values** | `ReadString`, `ReadStrings`, `Read<T>`, `ReadArray<T>`<br>`ReadBoolean`, `ReadInt32`, `ReadDouble`, `ReadDateTime`, `ReadChar`, ... |
| **Write values** | `WriteString`, `WriteStrings`, `Write<T>`, `WriteArray<T>`<br>`WriteBoolean`, `WriteInt32`, `WriteDouble`, `WriteDateTime`, `WriteChar`, ... |
| **JSON support** | `ReadJsonString`, `WriteJsonString`<br>`ReadJsonObject`, `WriteJsonObject`<br>`ReadJsonDynamicObject`, `WriteJsonDynamicObject` |
| **Delete keys and sections** | `RemoveKey`, `RemoveKeys`, `RemoveSection` |
| **Serialization** | `ReadSettings`, `WriteSettings`, `ExportToDictionary` |
| **Indexer** | `this[string section, string key]` |
| **Static** | `Load`, `LoadOrCreate`, `Save`, `ReadFromFile<T>`, `WriteToFile<T>`, `ExportToDictionaryFile` |

All methods accept `section = null` for global entries. All methods that previously accepted separate parameters (`comparison`, `allowEscChars`, etc.) are now obsolete; use the overloads that accept `IniSettings`.

---

# Background

Parsing INI files is a fairly common task in programming when working with configurations. INI files are simple and easy to read by both humans and machines. There are several main ways to implement this:
- Manual parsing using string manipulation functions. This approach allows for maximum flexibility in handling various INI file formats, but requires more effort to implement.
- Using modules of various APIs. They provide ready-made functions for reading, writing, and processing data in the INI format. This is a simpler and faster way, but it is limited by the capabilities of the libraries themselves, and it also makes the project platform-dependent.
- Parsing using common libraries for working with configuration files, such as configparser in Python or .NET's ConfigurationManager. This approach is universal, but may be less flexible than specialized solutions.
- Processing using regular expression.

INI is a simple and widely used configuration format. However, despite its simplicity, there is no single strict standard, and real-world files often contain formatting variations, comments, duplicate keys, malformed lines, and application-specific extensions.

The goal of this project is to provide a flexible parser that can handle these variations while preserving the original file structure. Thus, using regular expressions to parse INI files provides high performance, flexibility, preservation of original formatting and ease of use, which makes this approach an effective solution for working with configuration data in the INI format.

## INI File Format

This format is quite simple and has long been known to most developers. In general, it is a list of key-value pairs separated by an equal sign, called parameters. For convenience, parameters are grouped into sections, which are enclosed by square brackets. However, despite this, there are still a number of nuances and small differences, since a single standard is not strictly defined. If I create a new parser, my goal is to make it universal, so that it extracts information as efficiently as possible, so when writing a universal parser for working with INI files, these features must be taken into account.

![image](https://github.com/user-attachments/assets/517e69ff-1a5a-44ce-912b-d1a21d43ad65)

For example, different symbols can be used to indicate comments, the most common options are a hash or a semicolon, as well as various separators between the key and value. In addition to the usual equal sign, a colon is sometimes used in such cases. There are also files in which there are no sections, only key-value pairs. Different systems may use different characters to terminate a line. It is not strongly defined whether the keys "Key" and "key" should be considered different or treated as the same, regardless of case. The file may contain syntax errors or undefined data, which, however, should not prevent the correct parsing of valid content.

There is also no consensus on storing arrays of strings. Some standards allow multiple keys with the same name, others - the use of escaped characters to separate strings within the parameter value. Although most often the parser extracts the single value that found first. Our parser can handle all these tasks equally well.

Here is an example of syntax highlighting using a popular text editor. As you can see, its format does not provide for a comment after the section name or entry value.

![image](https://github.com/user-attachments/assets/f0d7bfc9-fa28-4d3a-98f4-619e16a8a572)

## Regular Expression

The parser uses a dynamic regular expression generated by `IniSettings.BuildRegexPattern()`. This pattern is tailored to the current settings (delimiters, comment characters, etc.). The default pattern (with both delimiters and both comment characters) is shown below:

```
(?=\S)(?<text>(?<comment>(?<open>[#;]+)(?:[^\S\r\n]*)(?<value>.+))
|(?<section>(?<open>\[)(?:\s*)(?<value>[^\]]*\S+)(?:[^\S\r\n]*)(?<close>\]))
|(?<entry>(?<key>[^=:\r\n\[\]]*\S)(?:[^\S\r\n]*)(?<delimiter>:|=)(?:\s*(?<value>\{(?:(?>(?:""(?:\\.|[^""])*""|//[^\r\n]*|/\*[\s\S]*?\*/|[^{}""/]+|/(?![/*])))|(?<o>\{)|(?<-o>\}))*(?(o)(?!))\})|((?:[^\S\r\n]*)(?<value>[^#;\r\n]*))))
|(?<undefined>.+))(?<=\S)
|(?<linebreaker>\r\n|\n)
|(?<whitespace>(?>[^\S\r\n]+))
```

Before we move on to writing the code, I want to break down the parsing regular expression itself and explain what each piece is for.

1. **`(?=\S)`** Ensures that parsing starts at the first meaningful character of a line. Leading indentation is handled separately, allowing the parser to preserve the original formatting.

2. **`(?<text>....)`** Represents a complete logical text element. Every meaningful line is classified as one of the supported INI constructs: a comment, a section header, a key-value entry, or undefined text.

3. **`(?<comment>(?<open>[#;]+)(?:[^\S\r\n]*)(?<value>.+))`** Matches comment lines beginning with ; or #. The comment marker and its text are captured separately so that comments can be preserved or modified without affecting surrounding whitespace.
    - **`(?<open>[#;]+)`** captures the beginning of a comment.
    - **`(?:[^\S\r\n]*)`** captures whitespace characters, not including newline characters.
    - **`(?<value>.+)`** - captures the entire comment text.

4. **`(?<section>(?<open>\[)(?:\s*)(?<value>[^\]]*\S+)(?:[^\S\r\n]*)(?<close>\]))`** Matches section headers such as \[Section\]. The opening bracket, section name, and closing bracket are captured individually, allowing the original spacing to remain unchanged after editing.
    - **`(?<open>\[)`** - captures the beginning of a section.
    - **`(?:\s*)`** - captures whitespace characters.
    - **`(?<value>[^\]]*\S+)`** - captures name of the section.
    - **`(?<close>\])`** captures the "]" character, which marks the end of a section.

5. **`(?<entry>(?<key>[^=:\r\n\[\]]*\S)(?:[^\S\r\n]*)(?<delimiter>:|=)(?:\s*(?<value>\{(?:(?>(?:""(?:\\.|[^""])*""|//[^\r\n]*|/\*[\s\S]*?\*/|[^{}""/]+|/(?![/*])))|(?<o>\{)|(?<-o>\}))*(?(o)(?!))\})|((?:[^\S\r\n]*)(?<value>[^#;\r\n]*))))|`** Matches key-value entries. It extracts the key name, the delimiter (= or :), and either a regular single-line value or a multiline value enclosed in { ... }. Nested braces and quoted strings inside wrapped values are handled correctly, making the parser suitable for embedded JSON.
    - **`(?<key>[^=:\r\n\[\]]*\S)`** captures key of the entry.
    - **`(?<delimiter>:|=)`** captures the ":" or "=" character separating the key and value.
    - **`(?<value>\{(?:(?>(?:""(?:\\.|[^""])*""|//[^\r\n]*|/\*[\s\S]*?\*/|[^{}""/]+|/(?![/*])))|(?<o>\{)|(?<-o>\}))*(?(o)(?!))\})`** captures text enclosed in '{' and '}' with backtracking, ignoring comments and strings inside.
    - **`(?<value>[^#;\r\n]*)`** captures regular INI value.
6. **`(?<undefined>.+)`** captures any undefined parts of the text that did not match the previous groups.

7. **`(?<=\S)`** is a positive lookbehind that ensures the preceding character is not a whitespace, skipping trailing whitespace.

8. **`(?<linebreaker>\r\n|\n)`** captures newline characters.

9. **`(?<whitespace>(?>[^\S\r\n]+))`** captures one or more whitespace characters (spaces/tabs) not including newlines.

This is a very detailed and carefully designed regular expression designed to accurately parse the structure of an INI file and extract all the necessary components (sections, keys, values, comments, etc.) from it. It can handle various formatting variations of INI files and provides a robust and flexible way of parsing.

Take a look at the parsing of the above sample using this regular expression:

![image](https://github.com/user-attachments/assets/fa2929cf-93bd-43b9-b11a-2c0039c93fff)

You can experiment with this regular expression using this [link](https://regex101.com/r/mul0C2). Note that the actual pattern used by the library may differ slightly if you change the settings.

## C# Implementation

To solve the problem of editing INI files while preserving their original structure, I created the **IniFile** class.

The key idea behind the implementation is a layered architecture that separates parsing rules, internal processing logic, and the public API:

- **Regular expression layer** — defines the structure of the INI format and identifies sections, keys, values, comments, and multiline blocks.
- **Internal processing layer** — provides operations for searching, modifying, inserting, and removing data without exposing parsing details.
- **Public API layer** — provides a simple and stable interface for working with INI files.

This separation makes the project easier to maintain and extend. New features and parsing improvements can be implemented without breaking the public API or rewriting existing functionality.

For example, JSON support did not require redesigning the whole parser. It was implemented by extending the existing parsing rules and adding several methods on top of the existing infrastructure. Multiline value support was obtained naturally as part of the same mechanism, without requiring a separate storage model or additional complexity.

The result is a flexible INI editor that can handle different file variations, including comments, custom formatting, duplicate keys, syntax errors, and embedded structured data, while keeping the original file layout intact.

## License

MIT License © 2024 Pavel Bashkardin. See [License](https://github.com/ng256/IniFile/blob/main/LICENSE) file for details.
```
