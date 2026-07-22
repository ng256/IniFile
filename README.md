# IniFile — Convenient Parser for INI Files

**IniFile** is a lightweight, fault-tolerant INI file parser built on regular expressions. Unlike classic dictionary-based implementations, it **preserves the original formatting** (whitespace, comments, line endings, order) when modifying entries, as it operates directly on text coordinates.

The class provides a convenient API for reading, writing, and deleting both simple values and multi‑line JSON structures embedded in INI files.

---

## Key Features

- **Read & write sections, keys, and values** – standard operations with configurable case sensitivity.
- **Multiple values** – supports duplicate keys in the same section (e.g., for arrays).
- **Deletion** – remove a single key, all keys with the same name, or entire sections.
- **Global entries** – work with key‑value pairs outside any section by passing `null` or an empty string as the section name.
- **Object serialization** – automatically map INI data to classes using attributes.
- **Embedded JSON support** – read and write JSON blocks that may span multiple lines and include comments before the block. Work with JSON as raw strings or as dynamic objects.
- **Preserve formatting** – changes modify only the necessary parts, leaving the rest of the file intact.
- **Static helper methods** – quick one‑liners for reading/writing a single value without creating an instance.
- **Escape characters** – optional support for `\n`, `\t`, etc.
- **Auto‑detection** of line endings and encoding (BOM).

---

## Quick Start

### Loading and Saving

```csharp
using System.Ini;

// Load from file
var ini = IniFile.Load("config.ini");

// Or create a new empty one
var ini = IniFile.Create();

// Save changes
ini.Save("config.ini");
```

### Reading and Writing Simple Values

```csharp
// Read
string host = ini.ReadString("Network", "Host", "localhost");
int port = ini.ReadInt32("Network", "Port", 8080);
bool enabled = ini.ReadBoolean("Network", "Enabled", true);

// Write
ini.WriteString("Network", "Host", "192.168.1.1");
ini.WriteInt32("Network", "Port", 9090);
ini.WriteBoolean("Network", "Enabled", false);
```

### Working with Multiple Values (Arrays)

```csharp
// Write array
ini.WriteStrings("Servers", "Address", "10.0.0.1", "10.0.0.2", "10.0.0.3");

// Read array
string[] addresses = ini.ReadStrings("Servers", "Address");
```

### Deleting Entries

```csharp
// Remove first occurrence of a key
ini.RemoveKey("Network", "Port");

// Remove all occurrences of a key
ini.RemoveKeys("Servers", "Address");

// Remove entire section (all occurrences)
ini.RemoveSection("Servers");
```

---

## JSON Support

INI files often contain JSON‑like structures. `IniFile` can extract and replace such blocks, even if they span several lines and are preceded by comments.

**Example INI content:**

```ini
[App]
config = {
  "timeout": 30,
  "retry": 5
}

[Data]
json =
; comment before JSON
[1, 2, 3, 4]
```

### Read/Write JSON as Raw String

```csharp
string json = ini.ReadJsonString("App", "config", "{}");
ini.WriteJsonString("App", "config", "{\"timeout\":60,\"retry\":10}");
```

### Read/Write JSON as Dynamic Object

```csharp
dynamic obj = ini.ReadJsonObject("App", "config");
int timeout = obj.timeout;
obj.retry = 10;

ini.WriteJsonObject("App", "config", obj, beautify: true);
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
    public int Port { get; set; } = 8080;
    [IniIgnore]
    public string Comment { get; set; }
}

var ini = IniFile.Load("config.ini");
var settings = new NetworkSettings();
ini.ReadSettings(settings);   // fills from file
ini.WriteSettings(settings);  // writes back to file
```

---

## Static Helper Methods

For quick access to a single value without creating an instance:

```csharp
int port = IniFile.ReadFromFile<int>("config.ini", "Network", "Port", 8080);
IniFile.WriteToFile("config.ini", "Network", "Port", 9090);
```

Overloads with `Encoding` parameter are also available.

---

## Customization Options

- **String comparison** – pass `StringComparison` (default: `InvariantCultureIgnoreCase`) to the constructor or static methods.
- **Escape characters** – enable `allowEscChars` to support `\n`, `\t`, etc.
- **Encoding** – specify encoding when loading/saving; auto‑detection of BOM is supported.

---

## Full API Reference

| Category | Methods |
|----------|---------|
| **Read** | `ReadSections()`, `ReadKeys(string section)` |
| **Read values** | `ReadString`, `ReadStrings`, `Read<T>`, `ReadArray<T>`<br>`ReadBoolean`, `ReadInt32`, `ReadDouble`, `ReadDateTime`, `ReadChar`, ... |
| **Write** | `WriteString`, `WriteStrings`, `Write<T>`, `WriteArray<T>`<br>`WriteBoolean`, `WriteInt32`, `WriteDouble`, `WriteDateTime`, `WriteChar`, ... |
| **JSON** | `ReadJsonString`, `WriteJsonString`<br>`ReadJsonObject`, `WriteJsonObject` |
| **Delete** | `RemoveKey`, `RemoveKeys`, `RemoveSection` |
| **Serialization** | `ReadSettings`, `WriteSettings` (for objects) |
| **Indexer** | `this[string section, string key]` |
| **Static** | `Load`, `LoadOrCreate`, `Save`, `ReadFromFile<T>`, `WriteToFile<T>` |

All methods accept `section = null` for global entries.

---

## Installation

Simply add `IniFile.cs` to your project and start using it. No external dependencies.

---

# Background
 
Parsing INI files is a fairly common task in programming when working with configurations. INI files are simple and easy to read by both humans and machines. There are several main ways to implement this:
- Manual parsing using string manipulation functions. This approach allows for maximum flexibility in handling various INI file formats, but requires more effort to implement.
- Using modules of various APIs. They provide ready-made functions for reading, writing, and processing data in the INI format. This is a simpler and faster way, but it is limited by the capabilities of the libraries themselves, and it also makes the project platform-dependent.
- Parsing using common libraries for working with configuration files, such as configparser in Python or .NET's ConfigurationManager. This approach is universal, but may be less flexible than specialized solutions.
- Processing using regular expression.

In this article, I plan to talk about parsing INI files using regular expressions in C#. This is an interesting and powerful approach that allows you to customize the processing logic as much as possible for your needs. Regular expressions provide greater flexibility in parsing file structure, but require a deeper understanding of regular expression syntax. This article will certainly be useful to readers who need customized INI file processing. This approach allows you to preserve the original file formatting, modify existing entries, and add new ones without using collections.
Thus, using regular expressions to parse INI files provides high performance, flexibility, preservation of original formatting and ease of use, which makes this approach an effective solution for working with configuration data in the INI format.

## INI file format

This format is quite simple and has long been known to most developers. In general, it is a list of key-value pairs separated by an equal sign, called parameters. For convenience, parameters are grouped into sections, which are enclosed by square brackets. However, despite this, there are still a number of nuances and small differences, since a single standard is not strictly defined. If I create a new parser, my goal is to make it universal, so that it extracts information as efficiently as possible, so when writing a universal parser for working with INI files, these features must be taken into account.

![image](https://github.com/user-attachments/assets/517e69ff-1a5a-44ce-912b-d1a21d43ad65)

For example, different symbols can be used to indicate comments, the most common options are a hash or a semicolon, as well as various separators between the key and value. In addition to the usual equal sign, a colon is sometimes used in such cases. There are also files in which there are no sections, only key-value pairs. Different systems may use different characters to terminate a line. It is not strongly defined whether the keys "Key" and "key" should be considered different or treated as the same, regardless of case. The file may contain syntax errors or undefined data, which, however, should not prevent the correct parsing of valid content.

There is also no consensus on storing arrays of strings. Some standards allow multiple keys with the same name, others - the use of escaped characters to separate strings within the parameter value. Although most often the parser extracts the single value that found first. Our parser can handle all these tasks equally well.

Here is an example of syntax highlighting using a popular text editor. As you can see, its format does not provide for a comment after the section name or entry value.

![image](https://github.com/user-attachments/assets/f0d7bfc9-fa28-4d3a-98f4-619e16a8a572)

## Regular expression

After much research, I came up with the following regular expression that allows you to determine the meaning of each character in an ini file. In its entirety it looks like this:  
```regex
(?=\S)(?<text>(?<comment>(?<open>[#;]+)(?:[^\S\r\n]*)(?<value>.+))
|(?<section>(?<open>\[)(?:\s*)(?<value>[^\]]*\S+)(?:[^\S\r\n]*)(?<close>\]))
|(?<entry>(?<key>[^=\r\n\ [\]]*\S)(?:[^\S\r\n]*)(?<delimiter>:|=)(?:[^\S\r\n]*)(?<value>[^#;\r\n]*))
|(?<undefined>.+))(?<=\S)|(?<linebreaker>\r\n|\n)|(?<whitespace>[^\S\r\n]+)
```

Before we move on to writing the code, I want to break down the parsing regular expression itself and explain what each piece is for.

1. **`(?=\S)`** is a positive lookahead condition that checks that the next character is not a whitespace. This is necessary to skip leading whitespace in the file.

2. **`(?<text>....)`** is a named group that captures the text block of the file.. This will allow us to get the entire content of the file for further analysis.

3. **`(?<comment>(?<open>[#;]+)(?:[^\S\r\n]*)(?<value>.+))`** is a named group that captures comments in the file. It consists of:
    - **`(?<open>[#;]+)`** is a group that captures one or more "#" or ";" characters, denoting the beginning of a comment.
    - **`(?:[^\S\r\n]*)`** - a group that captures zero or more non-whitespace characters, not including newline characters.
    - **`(?<value>.+)`** - a group that captures all characters up to the end of the line, i.e. the entire comment text.
    - 
4. **`(?<section>(?<open>\[)(?:\s*)(?<value>[^\]]*\S+)(?:[^\S\r\n]*)(?<close>\]))`** - is a named group that captures sections. It consists of:
    - **`(?<open>\[)`** - a group that captures the "[" character, which denotes the beginning of a section.
    - **`(?:\s*)`** - a group that captures zero or more whitespace characters.
    - **`(?<value>[^\]]*\S+)`** - a group that captures one or more non-whitespace characters, not including the "]" character.
    - **`(?:[^\S\r\n]*)`** is a group that captures zero or more non-whitespace characters, not including newline characters.
    - **`(?<close>\])`** is a group that captures the "]" character, which marks the end of a section.

5. **`(?<entry>(?<key>[^=\r\n\[\]]*\S)(?:[^\S\r\n]*)(?<delimiter>:|=)(?:[^\S\r\n]*)(?<value>[^#;\r\n]*))`** is a named group that captures entries (key-value). It consists of:
    - **`(?<key>[^=\r\n\[\]]*\S)`** is a group that captures one or more non-whitespace characters, not including the "=", newline, and "[" characters.
    - **`(?:[^\S\r\n]*)`** is a group that captures zero or more non-whitespace characters, not including newline characters.
    - **`(?<delimiter>:|=)`** is a group that captures the ":" or "=" character separating the key and value.
    - **`(?:[^\S\r\n]*)`** is a group that captures zero or more non-whitespace characters, not including newline characters.
    - **`(?<value>[^#;\r\n]*)`** is a group that captures zero or more characters, not including "#", ";", newline characters.
6. **`(?<undefined>.+)`** is a named group that captures any undefined parts of the text that did not match the previous groups.

7. **`(?<=\S)`** is a positive lookahead condition that checks that the preceding character is not a whitespace character. This is necessary to skip trailing whitespace in the file.

8. **`(?<linebreaker>\r\n|\n)`** is a named group that captures newline characters ("\r\n" or "\n").

9. **`(?<whitespace>[^\S\r\n]+)`** is a named group that captures one or more whitespace characters, not including newline characters.

  This is a very detailed and carefully designed regular expression designed to accurately parse the structure of an INI file and extract all the necessary components (sections, keys, values, comments, etc.) from it. It can handle various formatting variations of INI files and provides a robust and flexible way of parsing.

Take a look at the parsing of the above sample using this regular expression:

![image](https://github.com/user-attachments/assets/fa2929cf-93bd-43b9-b11a-2c0039c93fff)

You can experiment with this regular expression using this [link](https://regex101.com/r/mul0C2).

## C-Sharp coding

To solve the problem of parsing INI files using regular expressions, I created the **IniFile** class. This class will be responsible for reading and parsing the contents of an INI file using regular expressions to extract keys, values, and sections. The class has methods for loading a file, getting a list of sections, getting values ​​by keys, and writing changes back to the file. Using regular expressions, IniFile will be able to handle various configuration file formats, including files with comments, indents, spaces, syntax errors, and other features. This will make the parser more flexible and universal. To use the class, you need to pass it a string or stream containing the INI file data and parsing settings.
### Key features of the class
1. Support for various loading and saving methods: The class provides methods for loading INI files from a string, stream, or file, as well as saving them to a stream or file.
2. Using regular expressions: Using regular expressions allows for flexible and efficient handling of various INI file formats, including support for comments, sections, and key-values. This makes the code more compact and easily extensible compared to using manual string processing.
3. No dependence on collections: The class does not use collections to store INI file data, which makes it more memory efficient and simplifies working with large files.
4. Preserving original formatting: When modifying existing entries or adding new ones, the class preserves the original INI file formatting, including the location of comments, spaces, and line breaks. This helps maintain the readability and structure of the file.
5. Support for escape characters: The class provides the ability to work with escape characters in key values. This allows for the correct handling of special characters such as tabs, line feeds, etc.
6. Automatic detection of line break characters: The class automatically detects the type of line break characters (CRLF, LF, or CR) in the INI file and uses them when saving changes.
7. Flexible customization of string comparison: The class allows you to customize the string comparison rules (case sensitivity, cultural specificity) according to the requirements of the application.
8. Support for various loading and saving methods: The class provides methods for loading INI files from a string, stream, or file, as well as saving them to a stream or file.
9. Convenient API for working with INI files: The class offers a simple and intuitive API for reading and writing values ​​to INI files, including support for various data types.
Thus, using the IniFileRegexParser class allows you to efficiently and flexibly work with INI files, preserving their structure and formatting, and also provides ample opportunities for customization and expansion of functionality.

## License

MIT License © 2024 Pavel Bashkardin. See the header in the source file for details.
