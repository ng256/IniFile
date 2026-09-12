# IniFile — Convenient Single-file INI Editor for .NET

A single-file, dependency-free INI reader and editor that modifies configuration files without destroying their formatting.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![TestRegex](https://img.shields.io/badge/Test-Regex-blue)](https://regex101.com/r/mul0C2/13)

**IniFile** is a lightweight INI parser that is tolerant of malformed files. Unlike traditional dictionary-based implementations, it **preserves the original formatting** — including whitespace, comments, line endings, and entry order — by modifying the original text directly instead of rebuilding the file.

It provides a convenient API for reading, writing, and deleting values, handling multi-line **JSON blocks** embedded in INI files, navigating JSON structures by **path**, working with **dynamic objects**, expanding **environment variables**, tracking **model changes** via `INotifyPropertyChanged`, and parsing numbers in **different numeral systems**. The library consists of a single source file and has no external dependencies. Drop one file into your project and edit INI files without destroying their formatting.

See [Details](https://github.com/ng256/IniFile/blob/main/Details.md) document for more information.

---

## Key Features

* **Read & write INI files** — sections, keys, values, global entries, and duplicate keys.
* **Preserve formatting** — modify only the necessary parts while keeping the rest of the file intact.
* **Flexible parsing** — configurable delimiters, comments, case sensitivity, quoting, undefined text handling, and duplicate-key behaviour.
* **Automatic encoding and line-ending detection**.
* **Object serialization** — map INI data to classes using attributes.
* **Change tracking** — automatically write changed properties back to the INI file via `INotifyPropertyChanged`.
* **Environment variables** — expand standard and CMD-style pseudo-variables such as `%TEMP%`, `%RANDOM%`, `%CD%`, `%0`–`%9`, and `%*`.
* **Numbers in multiple radices** — decimal, hexadecimal, octal, and binary notation.
* **Culture-aware numbers** — floating-point parsing and formatting using the current or invariant culture.
* **JSON support** — read and write multi-line JSON blocks, including C-style comments, with support for raw strings, objects, and dynamic objects.
* **JSON path navigation** — access nested values using paths such as `root/nested/number`, including decimal, hexadecimal, octal, and binary array indices.
* **Dictionary round-trip** — export INI data to nested dictionaries and import it back.
* **Optional escape sequences** — support for `\n`, `\t`, and other escape characters.
* **Convenience helpers** — static methods for simple one-line read/write operations.

---

## Contents

1. [Installation](#installation)
2. [Usage](#usage)
    - [Loading and Saving](#loading-and-saving)
    - [Reading and Writing Simple Values](#reading-and-writing-simple-values)
    - [Working with Multiple Values (Arrays)](#working-with-multiple-values-arrays)
    - [Checking for Sections and Keys](#checking-for-sections-and-keys)
    - [Deleting Entries](#deleting-entries)
3. [JSON Support](#json-support)
    - [Read/Write JSON as Raw String](#readwrite-json-as-raw-string)
    - [Read/Write JSON as Object](#readwrite-json-as-object)
    - [Read/Write JSON as Dynamic Object](#readwrite-json-as-dynamic-object)
    - [Navigating JSON by Path](#navigating-json-by-path)
4. [Environment Variable Expansion](#environment-variable-expansion)
5. [Numbers in Different Radices](#numbers-in-different-radices)
    - [Culture and Decimal Separators](#culture-and-decimal-separators)
6. [Object Serialization with Attributes](#object-serialization-with-attributes)
7. [Change Tracking with `WatchSettings`](#change-tracking-with-watchsettings)
8. [Dictionary Import and Export](#dictionary-import-and-export)
9. [Normalized Output with `Justify`](#normalized-output-with-justify)
10. [Static Helper Methods](#static-helper-methods)
11. [Configuration with `IniSettings`](#configuration-with-inisettings)
12. [Embedded Parser Settings (Directives)](#embedded-parser-settings-directives)
13. [Full API Reference](#full-api-reference)
14. [Background](#background)
    - [INI File Format](#ini-file-format)
    - [Regular Expression](#regular-expression)
    - [C# Implementation](#c-implementation)
15. [License](#license)

## Installation

Simply add `IniFile.cs` to your project and start using it. No external dependencies.

---

## Usage

### Loading and Saving

```ini
; config.ini
[Host]
Network = localhost
Port = 8080

[Environment]
LogDirectory = "/var/log/myapp"
```

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

You can customize the parser behavior by passing an `IniSettings` object. The settings control string comparison, escape character processing, multiline support, quoted values, allowed delimiters, comment styles, handling of spaces in keys, interpretation of unrecognised text, and duplicate key behaviour.

```csharp
using System.Text;
using System.Ini;

// Create custom settings.
var settings = new IniSettings
{
    Comparison = StringComparison.OrdinalIgnoreCase,
    AllowEscapeChars = false,
    AllowMultiLine = true,
    AllowQuotedValues = true,
    Delimiters = IniDelimiterMode.Equals,      // only '='
    Comments = IniCommentMode.Hash,            // only '#'
    AllowSpacesInKey = true,
    UndefinedText = IniUndefinedTextMode.Key,  // bare words become flags
    DuplicateKeyOverride = true                // last value wins on duplicates
};

// Load with custom settings.
var ini = IniFile.Load("config.ini", settings);

// Or with encoding.
ini = IniFile.Load("config.ini", Encoding.UTF8, settings);

// Create an empty file with custom settings.
ini = IniFile.Create(settings);

// Save with encoding.
ini.Save("config.ini", Encoding.UTF8);

// Save a normalized representation instead of the raw content.
ini.Save("config.clean.ini", justify: true);
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
; Surrounding quotes are removed
LogDirectory = "/var/log/myapp"

; Multiline shell script preserve whitespace, comments, and line breaks
Script = "

#!/bin/sh

echo \"Starting...\"

mkdir -p /var/cache/myapp
cp -r /opt/data/* /var/cache/myapp/

echo \"Done.\"
"

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

```ini
[Servers]
Address = 10.0.0.1
Address = 10.0.0.2
Address = 10.0.0.3
```

```csharp
// Write array.
ini.WriteStrings("Servers", "Address", "10.0.0.1", "10.0.0.2", "10.0.0.3");

// Read array.
string[] addresses = ini.ReadStrings("Servers", "Address");
```

### Checking for Sections and Keys

```ini
[Server]
Port = 8080

theme = dark
```

```csharp
bool hasServer  = ini.ContainsSection("Server");       // true if the section exists
bool hasPort    = ini.Contains("Server", "Port");      // true if the key exists in the section
bool hasGlobal  = ini.Contains(null, "theme");         // checks entries above all sections
bool emptySec   = ini.ContainsSection(null);           // always false — global is not a section
```

`ContainsSection` and `Contains` are cheap: they only scan the cached match list and don't parse values. Use them to distinguish "key not present" from "key present with an empty value".

### Deleting Entries

```ini
[Network]
Host = localhost
Port = 8080

[Servers]
Address = 10.0.0.1
Address = 10.0.0.2
Address = 10.0.0.3
```

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
```

The `beautify` option formats the JSON with indentation and newlines for better readability.

### Read/Write JSON as Dynamic Object

Dynamic objects (`ExpandoObject`, custom `DynamicObject` subclasses) are fully supported for both reading and writing. Nested dictionaries and arrays are converted recursively, so accessing members via `dynamic` works at any depth.

```ini
[App]
config = { "timeout": 30, "retry": 5 }
```

```csharp
// Read JSON as a dynamic object.
dynamic dyn = ini.ReadJsonDynamicObject("App", "config");
int timeout = dyn.timeout;
dyn.retry = 10;

// Write it back (nested dictionaries/arrays are handled automatically).
ini.WriteJsonDynamicObject("App", "config", dyn, beautify: true);
```

You can also pass `dynamic` values through the generic `ReadObject` / `WriteObject` / `Read<T>` / `Write<T>` methods. When the target type is `ExpandoObject` or `DynamicObject`, `IniFile` automatically routes to the JSON‑dynamic path:

```csharp
// Write a dynamic value directly.
dynamic config = new ExpandoObject();
config.timeout = 30;
config.retry = 5;
ini.WriteObject("App", "config", config);

// Read it back as a dynamic object.
dynamic restored = ini.ReadObject("App", "config", typeof(ExpandoObject));
int retry = restored.retry;
```

Properties marked with `[Dynamic]` are also written as JSON automatically when using `WriteSettings`:

```csharp
public class AppConfig
{
    [Dynamic]
    public dynamic Extra { get; set; }
}
```

### Navigating JSON by Path

Reading and writing individual nodes inside a JSON block is done with a **slash- or backslash-separated path**. Array elements are addressed by their numeric index, which may be written in decimal, hexadecimal, octal, or binary notation — the same rules used everywhere else in the library.

```ini
[test]
json = { "root": { "nested": { "number": 42 }, "list": [10, 20, 30] } }
```

```csharp
var ini = IniFile.Load("test.ini");

// Read primitives at any depth.
int n = ini.ReadJsonObject("test", "json", "root/nested/number", -1);
// → 42

// Read a subtree as a plain object (dictionary / array / primitive).
object nested = ini.ReadJsonObject("test", "json", "root/nested");
// → Dictionary<string, object> { "number": 42 }

// Read as a dynamic object.
dynamic dyn = ini.ReadJsonDynamicObject("test", "json", "root");
int v = dyn.nested.number;

// Read the raw JSON fragment, preserving its original formatting.
string raw = ini.ReadJsonString("test", "json", "root/nested");
// → "{ \"number\": 42 }"

// Array indices: decimal, hex, octal, binary — all equivalent.
ini.ReadJsonString("test", "json", "root/list/0x1"); // → "20"
ini.ReadJsonString("test", "json", "root/list/0b10"); // → "30"

// Any failure returns the supplied default.
ini.ReadJsonObject("test", "json", "root/missing", "fallback");
ini.ReadJsonObject("test", "json", "root/list/99", "fallback");
```

Writing by path modifies only the addressed node, leaving the rest of the JSON structure untouched. Missing intermediate objects are created automatically.

```csharp
// Update an existing value.
ini.WriteJsonObject("test", "json", "root/nested/number", 100);

// Create a new subtree.
ini.WriteJsonObject("test", "json", "root/extra/deep/value", "hi");
// → json = { "root": { "nested": {...}, "extra": { "deep": { "value": "hi" } } } }

// Write into an array element.
ini.WriteJsonObject("test", "json", "root/list/1", 99);

// The dynamic overload accepts any dynamic value and converts it recursively.
dynamic patch = new ExpandoObject();
patch.enabled = true;
patch.retries = 3;
ini.WriteJsonDynamicObject("test", "json", "root/nested/options", patch);
```

**Behaviour notes:**

- The path uses `/` or `\` as separators. Empty segments are ignored (`"root//nested/"` is the same as `"root/nested"`).
- Array indices go through the shared `ParseNumber` routine, so `0x2`, `0b10`, `0o2`, `%10`, `$2`, `2h`, `2o` all resolve to the same index.
- If an intermediate node is a primitive (not an object or an array), or an array index is out of range, write operations perform no change; read operations return `defaultValue`. No exceptions are thrown.
- `ReadJsonString(section, key, path, defaultValue)` returns the raw JSON fragment with its original whitespace, comments, and line breaks — useful when you want to preserve formatting exactly.
- `WriteJsonObject` and `WriteJsonDynamicObject` accept a `beautify` flag. When `true`, the resulting JSON is indented for readability.

---

## Environment Variable Expansion

`ReadExpandedString` expands environment variables and pseudo‑variables before returning the value. This is useful when INI files contain paths and templates that depend on the runtime environment.

```ini
[Logging]
; Standard environment variables
Path = %TEMP%\app.log
UserProfileDir = %USERPROFILE%\Documents

; Pseudo-variables emulating CMD dynamic variables
Backup = %USERPROFILE%\backup\%DATE%_%TIME%.log
SessionDir = %CD%\session_%RANDOM%
Script = %0 --config "%CD%\app.ini" %*
```

```csharp
// Expands %TEMP% and other standard environment variables.
string path = ini.ReadExpandedString("Logging", "Path", @"%TEMP%\app.log");

// Expands pseudo-variables such as %DATE% and %RANDOM%.
string backup = ini.ReadExpandedString("Logging", "Backup");
// → C:\Users\Alice\backup\20260912_143022.log

// Standard .NET variables (invalid names) are left unchanged.
string literal = ini.ReadExpandedString("Logging", "Unknown", "%NOT_A_VAR%");
```

### Supported variables

| Variable | Replacement |
|----------|-------------|
| `%TEMP%`, `%USERPROFILE%`, ... | Any standard environment variable (via `Environment.ExpandEnvironmentVariables`). |
| `%RANDOM%` | Random 32‑bit unsigned integer (e.g. `1234567890`). |
| `%DATE%` | Current date in `yyyyMMdd` format. |
| `%TIME%` | Current time in `HHmmss` format. |
| `%CD%` | Current working directory (no trailing separator). |
| `%__CD__%` | Current working directory with trailing separator. |
| `%CMDCMDLINE%` | Full command line of the current process. |
| `%__APPDIR__%` | Directory of the executable file with trailing separator. |
| `%0` | Full path to the executable file (like `%0` in batch). |
| `%1` … `%9` | Command‑line arguments (missing arguments become empty strings). |
| `%*` | All command‑line arguments from `%1` onward, joined with spaces. |

If expansion fails (for example, the variable name is invalid for the platform), the original value is returned unchanged.

### Automatic expansion via `[IniExpanded]`

Apply `[IniExpanded]` to a property and `ReadSettings` will use `ReadExpandedString` instead of `ReadString`:

```ini
[App]
LogPath = %TEMP%\app.log
```

```csharp
public class AppConfig
{
    [IniEntry("LogPath")]
    [IniExpanded]
    public string LogPath { get; set; }
}

var config = new AppConfig();
ini.ReadSettings(config);
// config.LogPath now has %TEMP% expanded.
```

The attribute only affects reading; writing stores the value exactly as provided.

---

## Numbers in Different Radices

`IniFile` can parse and format integer and floating‑point values in several numeral systems. The parser recognises common prefixes and suffixes used by .NET, C/C++, Pascal, and assembler‑style notations.

### Integer notations

| Notation | Example | Value | Typical origin |
|----------|---------|-------|----------------|
| Decimal | `255` | 255 | universal |
| Decimal negative | `-42` | -42 | universal |
| Hex `0x` | `0xFF` | 255 | C, C++, C#, .NET |
| Hex `0x` | `0xDEAD` | 57005 | C, C++, C# |
| Hex `&h` | `&hFF` | 255 | BASIC, VBScript |
| Hex `$` | `$FF` | 255 | Pascal, Delphi |
| Hex `#` | `#1F` | 31 | some assemblers |
| Hex `&` | `&FF` | 255 | assemblers |
| Hex suffix `h` | `FFh` | 255 | Intel-style assembler |
| Binary `0b` | `0b1010` | 10 | C++14, C# 7, Python |
| Binary `0b` | `0b11111111` | 255 | C++14, C# 7, Python |
| Binary `%` | `%1010` | 10 | some assemblers |
| Binary suffix `b` | `1010b` | 10 | Intel-style assembler |
| Octal `0o` | `0o377` | 255 | C++, Python 3 |
| Octal `8#` | `8#377` | 255 | Visual Basic |
| Octal `&o` | `&o377` | 255 | VBScript |
| Octal suffix `o` | `377o` | 255 | some assemblers |

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
int read  = ini.ReadInt32("Masks", "Read");   // 1
int write = ini.ReadInt32("Masks", "Write");  // 2
int all   = ini.ReadInt32("Masks", "All");    // 7
int beta  = ini.ReadInt32("Flags", "Beta");   // 2
int top   = ini.ReadInt32("BitPattern", "Top");    // 8
int bottom= ini.ReadInt32("BitPattern", "Bottom"); // 15
```

The same parser is used for **array indices in JSON paths**, for **enum values**, and for all numeric `Read*` methods, so a bit mask written as `&hFF` will be understood identically wherever it appears.

The strongly‑typed `Write*` methods always emit the **standard decimal representation**, which keeps stored values portable across implementations. If a specific notation must be preserved, use `WriteString`.

### Enumerations

Enumerations benefit from the same numeric parser: values can be specified by name, by decimal index, or by any supported radix form. Flags combinations can be written as comma‑ or pipe‑separated lists, and mixing names with numeric parts is allowed.

```ini
[Permissions]
Access = Read, Write, 0x10
```

```csharp
[Flags]
public enum Permission
{
    None    = 0,
    Read    = 0x01,
    Write   = 0x02,
    Execute = 0x04,
    Admin   = 0x10
}

var p = ini.Read<Permission>("Permissions", "Access");
// p = Read | Write | Admin
```

### Culture and Decimal Separators

The culture used for parsing and formatting floating‑point values is derived from `IniSettings.Comparison`:

| `Comparison` value | `CultureInfo` used |
|--------------------|--------------------|
| `CurrentCulture` | `CultureInfo.CurrentCulture` |
| `CurrentCultureIgnoreCase` | `CultureInfo.CurrentCulture` |
| `InvariantCulture` | `CultureInfo.InvariantCulture` |
| `InvariantCultureIgnoreCase` (default) | `CultureInfo.InvariantCulture` |
| `Ordinal` | `CultureInfo.InvariantCulture` |
| `OrdinalIgnoreCase` | `CultureInfo.InvariantCulture` |

This affects `ReadDouble`, `ReadSingle`, `ReadDecimal` and their write counterparts. The decimal separator (`.` or `,`), group separators, and the sign character all depend on the chosen culture.

```ini
[Measurements]
; Parsed as 3.14 when Comparison = InvariantCulture... (decimal point is '.')
Invariant = 3.14

; Parsed as 3.14 when Comparison = CurrentCulture... in a German/Russian locale
; (decimal separator is ','). With InvariantCulture, this would fail to parse
; and fall back to the default value.
Localized = 3,14

; Scientific notation — supported by both cultures.
Scientific = 1.5e-3      ; 0.0015
Big = 2.5E+10            ; 25000000000

; Negative values.
BelowZero = -0.001
```

```csharp
// Invariant culture (the default): decimal point is '.'.
var invariant = new IniSettings
{
    Comparison = StringComparison.InvariantCultureIgnoreCase
};
var ini = IniFile.Load("measurements.ini", invariant);

double a = ini.ReadDouble("Measurements", "Invariant");   // 3.14
double b = ini.ReadDouble("Measurements", "Localized", 0.0); // 0.0 — "3,14" not parsed
double s = ini.ReadDouble("Measurements", "Scientific");  // 0.0015
double n = ini.ReadDouble("Measurements", "BelowZero");   // -0.001
```

```csharp
// Current culture (e.g. Russian, German): decimal separator is ','.
var current = new IniSettings
{
    Comparison = StringComparison.CurrentCultureIgnoreCase
};
var ini = IniFile.Load("measurements.ini", current);

double a = ini.ReadDouble("Measurements", "Invariant", 0.0);  // 0.0 — "3.14" not parsed
double b = ini.ReadDouble("Measurements", "Localized");       // 3.14
```

**Recommendation.** For portable configuration files use the default `InvariantCultureIgnoreCase`. It treats `.` as the decimal separator on every machine, regardless of the system locale. Use `CurrentCulture*` only when the file is intended for a single user community that consistently uses a specific locale.

---

## Object Serialization with Attributes

Automatically map INI sections to classes and properties.

```ini
[Network]
Host = localhost
Port = 8080
LogPath = %TEMP%\network.log
```

```csharp
[IniSection("Network")]
class NetworkSettings
{
    public string Host { get; set; } = "localhost";
    [IniEntry("Port")]  // Maps the property to a different INI key name.
    public int ConnectionPort { get; set; } = 8080;
    [IniIgnore] // Prevents the property from being read or written.
    public string Comment { get; set; }
    [IniExpanded] // Expands environment variables on read.
    public string LogPath { get; set; }
}

var ini = IniFile.Load("config.ini");
var settings = new NetworkSettings();
ini.ReadSettings(settings);   // Reads values from the INI file.
// ... make changes to settings.
ini.WriteSettings(settings);  // Writes values back to the INI file.
```

You can also read/write settings for a whole type, assembly, or an individual instance:

```csharp
ini.ReadSettings(typeof(NetworkSettings));       // static properties
ini.ReadSettings(Assembly.GetExecutingAssembly()); // all types in assembly
ini.ReadSettings(settings);                       // instance properties
```

---

## Change Tracking with `WatchSettings`

`WriteSettings` writes every property on demand. When the source object implements `INotifyPropertyChanged`, you can instead subscribe to its changes and let `IniFile` persist them as they happen.

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

    [IniSection("Server")]
    [IniEntry("Host")]
    public string Host
    {
        get => _host;
        set { _host = value; OnPropertyChanged(nameof(Host)); }
    }

    [IniSection("Server")]
    [IniEntry("Port")]
    public int Port
    {
        get => _port;
        set { _port = value; OnPropertyChanged(nameof(Port)); }
    }

    [IniIgnore]
    public string RuntimeOnly { get; set; }

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// ---

var ini = IniFile.LoadOrCreate("config.ini");
var config = new AppConfig();

// Optionally write the initial state once.
ini.WriteSettings(config);

// Watch future changes. Dispose to unsubscribe.
using (ini.WatchSettings(config))
{
    config.Host = "192.168.1.1";   // immediately written to the INI file
    config.Port = 9090;            // and so is this
    config.RuntimeOnly = "x";      // ignored — marked [IniIgnore]
}   // Dispose unsubscribes
```

**Behaviour notes:**

- `WatchSettings` does **not** write the current state on subscription. Call `WriteSettings(obj)` first if you want the initial state captured.
- Only changed properties are written. When `PropertyChanged` is raised with an empty or `null` `PropertyName`, all tracked properties are written.
- Properties marked with `[IniIgnore]` are never tracked.
- The watcher does not synchronise access to the file. If the source raises changes from multiple threads, the caller is responsible for serialising the writes.
- `Dispose` is idempotent; calling it twice is safe.

---

## Dictionary Import and Export

The full content of an INI file can be converted to a nested dictionary and back. This is useful for snapshotting, transferring configuration between environments, or moving between `IniFile` and other dictionary-based APIs.

```ini
; config.ini
theme = dark

[Server]
host = localhost
port = 8080

[Paths]
include = /opt/data/
include = /mnt/backup/
```

```csharp
// Export: section → key → list of values (preserves order and duplicates).
Dictionary<string, Dictionary<string, List<string>>> snapshot =
    ini.ExportToDictionary();

// Snapshot content:
//   ""       → { "theme" → ["dark"] }
//   "Server" → { "host"  → ["localhost"], "port" → ["8080"] }
//   "Paths"  → { "include" → ["/opt/data/", "/mnt/backup/"] }
//
// Global entries (above all named sections) are stored under the empty-string key.
// Multi-value keys become lists; the order in the file is preserved.
```

```csharp
// Import: merge into the existing content.
ini.ImportFromDictionary(snapshot);

// Or replace the entire content.
ini.ImportFromDictionary(snapshot, replace: true);
```

**Behaviour notes:**

- With `replace: false` (default), existing keys are overwritten in place, new keys and sections are appended. Formatting of untouched entries is preserved.
- With `replace: true`, the current content is cleared first, then the data is loaded.
- An empty value list in the dictionary removes all occurrences of the corresponding key (mirrors `RemoveKeys`).
- An empty string as the outer key represents global entries located above all named sections.
- Static file-based overloads are available: `ExportToDictionaryFile` and `ImportFromDictionaryFile`.

```csharp
// Copy content from one file to another, dropping comments and extra whitespace.
var snapshot = IniFile.ExportToDictionaryFile("messy.ini");
IniFile.ImportFromDictionaryFile("clean.ini", snapshot, replace: true);
```

---

## Normalized Output with `Justify`

`Justify()` returns a compact representation of the file containing only sections and key-value pairs — no comments, no empty lines, no extra whitespace. The original `Content` is not modified; `Justify()` returns a new string.

```ini
; Application configuration — messy.ini

theme    =    dark

[Server]
; The main server address
host     =    localhost
port=8080
port=9090

[Paths]
include = /opt/data/
include    =    /mnt/backup/
```

```csharp
string normalized = ini.Justify();
```

Result:

```ini
theme=dark

[Server]
host=localhost
port=8080
port=9090

[Paths]
include=/opt/data/
include=/mnt/backup/
```

The delimiter and line breaker come from the current instance:

- The delimiter is taken from `IniSettings.Delimiters` — `=` for `Equals` and `Both`, `:` for `Colon`.
- The line breaker is auto-detected from the source content in the constructor, so a file that used `\n` produces `\n`, a file that used `\r\n` produces `\r\n`.

To save a normalized version of the file, pass `justify: true` to any `Save` overload:

```csharp
ini.Save("config.clean.ini", justify: true);
ini.Save(stream, Encoding.UTF8, justify: true);
ini.Save(writer, justify: true);
```

The `IniFile` instance is never modified by `Save(..., justify: true)` — it writes the justified form to the destination and leaves `Content` intact.

---

## Static Helper Methods

For quick access to file data without creating an instance:

```ini
; config.ini
[Network]
Port = 8080
```

```csharp
// Read/write a single value.
int port = IniFile.ReadFromFile<int>("config.ini", "Network", "Port", 8080);
IniFile.WriteToFile("config.ini", "Network", "Port", 9090);

// Convert the file contents to a dictionary representation
// (returns empty dictionary if file not found).
var dict = IniFile.ExportToDictionaryFile("config.ini");
foreach (var section in dict)
{
    Console.WriteLine($"[{section.Key}]");
    foreach (var entry in section.Value)
    {
        Console.WriteLine($"  {entry.Key} = {string.Join(", ", entry.Value)}");
    }
}

// Import a dictionary back into a file.
IniFile.ImportFromDictionaryFile("config.ini", dict, replace: true);
```

Overloads with `Encoding` and `IniSettings` parameters are also available.

---

## Configuration with `IniSettings`

All parser behaviour is centralised in the `IniSettings` class. It allows you to fine‑tune how the INI file is interpreted.

### Settings Overview

| Property | Type | Description |
|----------|------|-------------|
| `Comparison` | `StringComparison` | Case sensitivity and culture rules (default: `InvariantCultureIgnoreCase`). Also determines the `CultureInfo` used for parsing and formatting floating‑point numbers. |
| `AllowEscapeChars` | `bool` | If `true`, escape sequences like `\n` and `\t` are unescaped in values (default: `true`). |
| `AllowMultiLine` | `bool` | If `true`, values wrapped in `{ ... }` can span multiple lines (default: `true`). |
| `AllowQuotedValues` | `bool` | If `true`, values enclosed in single or double quotes are read until the matching unescaped quote, preserving whitespace and line breaks (default: `true`). |
| `AllowInlineComments` | `bool` | If `true`, comments may follow values on the same line (default: `true`). |
| `AllowSpacesInKey` | `bool` | If `true`, key names may contain spaces (default: `false`). |
| `DuplicateKeyOverride` | `bool` | If `true`, reading a duplicated key returns the last value (override mode); if `false` (default), returns the first. Does not affect `ReadStrings`. |
| `Delimiters` | `IniDelimiterMode` | Allowed delimiters: `Equals`, `Colon`, `Both` (default: `Both`). |
| `Comments` | `IniCommentMode` | Allowed comment characters: `Hash`, `Semicolon`, `Both` (default: `Both`). |
| `UndefinedText` | `IniUndefinedTextMode` | How to interpret unrecognised text: `Ignore` (keep as undefined), `Key` (treat as key with empty value, i.e. flags), `Value` (treat as value with empty key). |

The `IniSettings.Default` property provides a preconfigured instance with the default settings, which you can use as a base for customisation.

### Example

```ini
; config.ini
; A bare word below is treated as a flag (key without a value).
verbose
log_to_file
```

```csharp
var settings = new IniSettings
{
    Comparison = StringComparison.Ordinal,
    Delimiters = IniDelimiterMode.Equals,
    Comments = IniCommentMode.Hash,
    AllowSpacesInKey = false,
    AllowQuotedValues = true,
    UndefinedText = IniUndefinedTextMode.Key,   // bare words become flags
    DuplicateKeyOverride = true                 // last value wins
};
var ini = IniFile.Load("config.ini", settings);

bool verbose = ini.ReadBoolean(null, "verbose", false); // true
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
| `#quoted_values` | `bool` | flag or `true`/`false` | `true` |
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
#quoted_values = true

; Normal application settings
[General]
AppName = MyApp
Version = 1.0
```

Here:
- `#comparison = ordinal` → case‑sensitive keys and sections.
- `#space_in_key` → allows spaces in key names.
- `#escape_chars = false` → disables escape‑sequence processing.
- `#quoted_values = true` → values can be wrapped in quotes.

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
| **Presence checks** | `ContainsSection(string section)`, `Contains(string section, string key)` |
| **Read values** | `ReadString`, `ReadExpandedString`, `ReadStrings`, `Read<T>`, `ReadArray<T>`<br>`ReadBoolean`, `ReadInt32`, `ReadDouble`, `ReadDateTime`, `ReadChar`, ... |
| **Read structured data** | `ReadJsonString`, `ReadJsonObject`, `ReadJsonDynamicObject`<br>Each has an overload with a `path` parameter for navigating nested JSON. |
| **Write values** | `WriteString`, `WriteStrings`, `Write<T>`, `WriteArray<T>`<br>`WriteBoolean`, `WriteInt32`, `WriteDouble`, `WriteDateTime`, `WriteChar`, ... |
| **Write structured data** | `WriteJsonString`, `WriteJsonObject`, `WriteJsonDynamicObject`<br>`WriteJsonObject` and `WriteJsonDynamicObject` accept a `path` parameter for updating a single node. |
| **Delete keys and sections** | `RemoveKey`, `RemoveKeys`, `RemoveSection` |
| **Serialization** | `ReadSettings`, `WriteSettings`, `WatchSettings`<br>`ExportToDictionary`, `ImportFromDictionary` |
| **Normalization** | `Justify()` |
| **Indexer** | `this[string section, string key]` |
| **Static** | `Load`, `LoadOrCreate`, `Save`, `ReadFromFile<T>`, `WriteToFile<T>`<br>`ExportToDictionaryFile`, `ImportFromDictionaryFile` |

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

The same approach made it possible to add:

- **JSON path navigation** — reuses the existing tokenizer (`_jsonRegex`) and the existing path splitter (`GetPathSegments`). Tokens carry `Match.Index` and `Match.Length`, so the raw JSON fragment at any path can be returned without re-serialising it and without losing formatting.
- **Environment variable expansion** — a thin wrapper (`ReadExpandedString`) over the existing read path that substitutes variables before returning the value, plus the `[IniExpanded]` attribute for declarative use in serialization.
- **Dynamic object support** — a lightweight adapter (`SafeExpandoObject`) that bridges `IDictionary<string, object>` and `ExpandoObject`, allowing JSON entries to be read and written as dynamic objects at any nesting depth.
- **Multi‑radix number parsing** — an extended `ParseNumber` routine used by all numeric `Read*` methods, by enum parsing, and by the JSON path navigator when resolving array indices. Floating‑point parsing additionally respects the culture derived from `IniSettings.Comparison`.
- **Change tracking** — `WatchSettings` builds a private property-name → `PropertyInfo` map once, then subscribes to `INotifyPropertyChanged` and writes only the changed properties. No reflection lookups on every event.
- **Dictionary round-trip** — `ImportFromDictionary` is a thin layer over the existing `WriteStrings` / `RemoveKeys`, so it inherits all formatting-preservation and multi-value behaviour for free.
- **Normalized output** — `Justify()` reuses the cached matches and the auto-detected line breaker, so it does not re-parse the content and respects the source file's conventions.

The result is a flexible INI editor that can handle different file variations, including comments, custom formatting, duplicate keys, syntax errors, and embedded structured data, while keeping the original file layout intact.

## License

MIT License © 2024 Pavel Bashkardin. See [License](https://github.com/ng256/IniFile/blob/main/LICENSE) file for details.
