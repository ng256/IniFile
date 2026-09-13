/******************************************************************************
   
   •   File: IniFile.cs
   
   •   Description:
   
       IniFile is a class that provides parsing, editing,  and  serialization 
       of INI files using regular expressions.
   
       The class provides functionality for:
          - parsing INI files;
          - reading and writing sections, keys, and values;
          - supporting multiple values for the same key;
          - adding, updating, and removing keys and sections;
          - checking for the presence of sections and keys (ContainsSection,
            ContainsKey) with O(1) lookup via a cached section index;
          - automatically mapping objects to and from INI files;
          - tracking object changes via INotifyPropertyChanged and persisting
            them to the INI file automatically (WatchSettings);
          - reading and writing multiline values enclosed in quotes;
          - reading and writing embedded JSON blocks as raw strings, plain
            objects, or dynamic objects (ExpandoObject, DynamicObject);
          - navigating JSON structures by path (e.g. "root/nested/value"),
            with array indices expressed in decimal, hexadecimal, octal, or
            binary notation;
          - expanding environment variables and pseudo-variables when reading
            (e.g., %TEMP%, %RANDOM%, %DATE%, %TIME%, %CD%, %0..%9, %*);
          - parsing and formatting numbers in decimal, hexadecimal, octal,
            and binary notation using common prefixes and suffixes
            (0x, 0b, 0o, &h, &o, 8#, %, $, #, h, b, o, etc.);
          - exporting content to dictionaries and importing it back
            (ExportToDictionary / ImportFromDictionary and their file-based
            counterparts);
          - producing a normalized (justified) representation of the content
            using the configured delimiter and line breaker;
          - flexible interpretation of otherwise unrecognised text:
            it can be treated as undefined, as a key with an empty value
            (flags), or as a value with an empty key (continuation lines);
          - controlling whether the first or last duplicate key value is
            returned.
   
       All  modifications  preserve  the  original  formatting of the file,
       including  whitespace,  comments,  and  line  endings,  by  operating
       directly on the original text.
   
       INI parsing behaviour can be configured through the IniSettings class,
       including string comparison rules, multiline values, quoted values,
       escape sequences,  allowed delimiters,  comment characters,  handling
       of spaces in keys, undefined text mode, duplicate key override, and
       other parser options.
   
       Performance characteristics:
          - compiled Regex instances and their group indices are cached
            per settings signature (bounded at 32 entries), so the first
            IniFile with a given configuration pays the compilation cost
            and every subsequent instance reuses the compiled bundle;
          - sections are indexed by name into contiguous ranges of the
            match list, so reads never scan the whole file;
          - the most recently resolved section is cached in a single slot,
            skipping the dictionary lookup for consecutive queries;
          - all reading methods operate on cached numeric group indices
            instead of performing Groups["name"] lookups per token.
   
       The class can load INI data from strings,  text readers,  streams, or
       files, and can save the modified content back without reformatting.
   
   •   License:
   
       This software is distributed under the MIT License (MIT)
   
       © 2024-2026 Pavel Bashkardin.
   
       See https://github.com/ng256/IniFile/blob/main/LICENSE for details.
   
   ******************************************************************************/

using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.ComponentModel;
using System.Reflection;
using System.Diagnostics;
using System.Collections;
using System.Dynamic;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

//#nullable disable

namespace System.Ini
{
    #region INI settings

    /// <summary>
    /// Specifies how byte arrays are encoded when stored in an INI file.
    /// </summary>
    public enum IniByteEncoding
    {
        /// <summary>Bytes are written as space-separated hexadecimal pairs (e.g. "01 02 FF").</summary>
        Hexadecimal,
        /// <summary>Bytes are written as a Base64 string (e.g. "AQL/").</summary>
        Base64
    }

    /// <summary>
    /// Specifies allowed delimiter characters between key and value in an INI file.
    /// </summary>
    [Flags]
    public enum IniDelimiterMode
    {
        /// <summary>No delimiter explicitly selected; defaults to Both.</summary>
        Default = 0,
        /// <summary>Use '=' as delimiter.</summary>
        Equals = 1,
        /// <summary>Use ':' as delimiter.</summary>
        Colon = 2,
        /// <summary>Use both '=' and ':' as delimiters.</summary>
        Both = Equals | Colon
    }

    /// <summary>
    /// Specifies allowed comment-start characters in an INI file.
    /// </summary>
    [Flags]
    public enum IniCommentMode
    {
        /// <summary>No comment character explicitly selected; defaults to Both.</summary>
        Default = 0,
        /// <summary>Use '#' as comment character.</summary>
        Hash = 1,
        /// <summary>Use ';' as comment character.</summary>
        Semicolon = 2,
        /// <summary>Use both '#' and ';' as comment characters.</summary>
        Both = Hash | Semicolon
    }

    /// <summary>
    /// Specifies how unrecognised text should be captured.
    /// </summary>
    public enum IniUndefinedTextMode
    {
        /// <summary>
        /// Unrecognised text is captured as 'undefined'.
        /// This is the default behaviour - the text is treated as an error or ignored.
        /// </summary>
        Ignore,

        /// <summary>
        /// Unrecognised text is treated as a key without a value (a flag).
        /// It produces an 'entry' group containing a 'key' group with the text
        /// and an empty 'value' group.
        /// </summary>
        Key,

        /// <summary>
        /// Unrecognised text is treated as a value with an empty key.
        /// It produces an 'entry' group containing an empty 'key' group
        /// and a 'value' group with the text.
        /// </summary>
        Value
    }
    
    /***********************************************************************
      Parameters can be specified in the global section of the INI file.
      #comparison=Ordinal|OrdinalIgnoreCase|InvariantCulture|InvariantCultureIgnoreCase|CurrentCulture|CurrentCultureIgnoreCase
      #escape_chars=True|False
      #muli_line=True|False
      #quoted_values=True|False
      #space_in_key=True|False
      #inline_comment=True|False
      #dup_key_override=True|False
      #delimiter=Default|Equals|Colon|Both
      #comment=Default|Hash|Semicolon|Both
      #undef_text=Ignore|Key|Value
    ***********************************************************************/
    /// <summary>
    /// Configuration settings for parsing INI files.
    /// </summary>
    [IniSection("")]
    public sealed class IniSettings
    {
        // Gets the default settings instance.
        [IniIgnore]
        internal static IniSettings Default { get; } = new IniSettings();

        /// <summary>
        /// String comparison rules (case sensitivity, culture).
        /// </summary>
        [IniEntry("#comparison")]
        public StringComparison Comparison { get; set; } = StringComparison.InvariantCultureIgnoreCase;

        /// <summary>
        /// Whether escape sequences (e.g., \n, \t) are processed in values.
        /// </summary>
        [IniEntry("#escape_chars")]
        public bool AllowEscapeChars { get; set; } = true;

        /// <summary>
        /// Whether multiline values wrapped in { } are supported.
        /// </summary>
        [IniEntry("#muli_line")]
        public bool AllowMultiLine { get; set; } = true;

        /// <summary>
        /// Whether values can be enclosed in double or single quotes to preserve whitespace and line breaks.
        /// When <c>true</c>, values starting with a quote are read until the matching unescaped quote,
        /// allowing multi-line values and preserving spaces and comments after the closing quote.
        /// </summary>
        [IniEntry("#quoted_values")]
        public bool AllowQuotedValues { get; set; } = true;


        /// <summary>
        /// Whether spaces are allowed within key names.
        /// </summary>
        [IniEntry("#space_in_key")]
        public bool AllowSpacesInKey { get; set; } = false;

        /// <summary>
        /// Whether comments are allowed after values on the same line.
        /// </summary>
        [IniEntry("#inline_comment")]
        public bool AllowInlineComments { get; set; } = true;

        /// <summary>
        /// Controls which value is returned when the same key appears more than once.
        /// When <c>false</c> (default), <see cref="IniFile.ReadString"/> and similar methods
        /// return the first occurrence. When <c>true</c>, they return the last occurrence
        /// (later values override earlier ones). This setting does not affect
        /// <see cref="IniFile.ReadStrings"/>, which always returns all values.
        /// </summary>
        [IniEntry("#dup_key_override")]
        public bool DuplicateKeyOverride { get; set; } = false;

        /// <summary>
        /// Delimiter characters allowed between key and value.
        /// </summary>
        [IniEntry("#delimiter")]
        public IniDelimiterMode Delimiters { get; set; } = IniDelimiterMode.Both;

        /// <summary>
        /// Comment-start characters recognised in the file.
        /// </summary>
        [IniEntry("#comment")]
        public IniCommentMode Comments { get; set; } = IniCommentMode.Both;

        /// <summary>
        /// Controls how text that does not match comment, section or entry is captured.
        /// </summary>
        [IniEntry("#undef_text")]
        public IniUndefinedTextMode UndefinedText { get; set; } = IniUndefinedTextMode.Ignore;


        /// <summary>
        /// Initializes a new instance with default settings.
        /// </summary>
        public IniSettings()
        {
        }

        /// <summary>
        /// Initializes a new instance with specified settings.
        /// </summary>
        /// <param name="comparison">String comparison rules.</param>
        /// <param name="allowEscapeChars">Whether escape sequences are processed.</param>
        /// <param name="allowMultiLine">Whether multiline values are supported.</param>
        /// <param name="allowQuotedValues">Whether quoted values are supported.</param>
        /// <param name="allowSpacesInKey">Whether spaces are allowed in key names.</param>
        /// <param name="allowInlineComments">Whether comments are allowed after values on the same line.</param>
        /// <param name="delimiters">Allowed delimiter characters.</param>
        /// <param name="comments">Allowed comment-start characters.</param>
        /// <param name="undefinedTextMode">How unrecognised text is captured.</param>
        /// <param name="duplicateKeyOverride">
        /// When <c>true</c>, later duplicate key values override earlier ones;
        /// when <c>false</c>, the first occurrence is returned.
        /// </param>
        public IniSettings(
            StringComparison comparison = StringComparison.InvariantCultureIgnoreCase,
            bool allowEscapeChars = true,
            bool allowMultiLine = true,
            bool allowQuotedValues = true,
            bool allowSpacesInKey = false,
            bool allowInlineComments = true,
            bool duplicateKeyOverride = false,
            IniDelimiterMode delimiters = IniDelimiterMode.Both,
            IniCommentMode comments = IniCommentMode.Both,
            IniUndefinedTextMode undefinedTextMode = IniUndefinedTextMode.Ignore
            )
        {
            AllowInlineComments = allowInlineComments;
            Comparison = comparison;
            AllowEscapeChars = allowEscapeChars;
            AllowMultiLine = allowMultiLine;
            AllowQuotedValues = allowQuotedValues;
            DuplicateKeyOverride = duplicateKeyOverride;
            Delimiters = delimiters;
            Comments = comments;
            AllowSpacesInKey = allowSpacesInKey;
            UndefinedText = undefinedTextMode;
        }

        // Reads the INI settings from the specified string content and returns a new instance
        // populated with the values from the content.
        internal static IniSettings Parse(string content)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            var settings = new IniSettings();
            if (content != string.Empty)
            {
                var tmpSettings = new IniSettings
                {
                    Comments = IniCommentMode.Semicolon,
                    UndefinedText = IniUndefinedTextMode.Key
                };
                var ini = IniFile.Create(content, tmpSettings);
                ini.ReadSettings(settings);

            }

            return settings;
        }

        // ********* Builds the regular expression pattern based on the current settings. *********

        /*
         FILE
         ├── TEXT
         │    ├── COMMENT
         │    ├── SECTION
         │    ├── ENTRY
         │    │    └── VALUE
         │    │         ├── SINGLE LINE
         │    │         └── MULTI LINE BLOCK
         │    └── UNDEFINED
         │
         ├── LINE BREAK
         └── WHITESPACE
         */

        // Configurable regex builder for INI pattern.
        internal string BuildIniPatternEx()
        {
            // 1. Text token - must start and end with a non‑whitespace character.
            string textPattern = $@"(?=\S)(?<text>{BuildTextPattern()})(?<=\S)";

            // 2. Line breaker: captures CRLF or LF.
            // 3. Whitespace: captures any sequence of spaces/tabs (non‑line‑break whitespace).
            return $"{textPattern}|{BuildLineBreakerPattern()}|{BuildWhitespacePattern()}";
        }

        // ---- Grammar fragments ----

        // Text pattern - combines comment, section, entry, and undefined.
        private string BuildTextPattern()
        {
            return $"{BuildCommentPattern()}|{BuildSectionPattern()}|{BuildEntryPattern()}|{BuildUndefinedPattern()}";
        }

        // Comment pattern.
        // The full comment is captured in group 'comment'.
        private string BuildCommentPattern()
        {
            string commentChars = BuildCommentCharacters();   // e.g. "#;"
            return
                @"(?<comment>" +
                    @"(?<open>[" + commentChars + @"]+)" +  // One or more comment characters.
                    @"(?:[^\S\r\n]*)" +                     // Trailing whitespaces.
                    @"(?<value>.*)" +                       // Comment text.
                @")";
        }

        // Section pattern.
        private string BuildSectionPattern()
        {
            return
                @"(?<section>" +
                    @"(?<open>\[)" +                         // Opening bracket '['.
                    @"(?:[^\S\r\n]*)" +
                    @"(?<value>[^\]\r\n]*?\S)" +             // Section name: any chars except ']', CR, LF;
                                                             // Lazy match, must end with a non‑whitespace character.
                    @"(?:[^\S\r\n]*)" +
                    @"(?<close>\])" +                        // Closing bracket ']'.
                @")";
        }

        // Full entry pattern.
        private string BuildEntryPattern()
        {
            return
                @"(?<entry>" +
                    BuildKeyPattern() +                     // Key pattern.
                    @"(?:[^\S\r\n]*)" +
                    BuildDelimiterPattern() +               // Delimiter pattern.
                    BuildValuePattern() +                   // Value pattern.
                @")";
        }

        // Key pattern.
        private string BuildKeyPattern()
        {
            // Build the actual delimiter characters for the forbidden class.
            string delimChars;
            var delimiter = Delimiters & IniDelimiterMode.Both;

            if (delimiter == IniDelimiterMode.Equals)
                delimChars = "=";
            else if (delimiter == IniDelimiterMode.Colon)
                delimChars = ":";
            else
                delimChars = ":=";   // Both or Default

            // Always forbidden: delimiter chars, line breaks, brackets.
            string forbidden = delimChars + @"\r\n\[\]";

            // Spaces in keys are controlled exclusively by AllowSpacesInKey.
            if (!AllowSpacesInKey)
                forbidden += " ";

            return
                @"(?<key>" +
                    @"[^" + forbidden + @"]*" +             // Zero or more characters that are NOT in the forbidden set.
                    @"\S" +                                 // The key must end with at least one non‑whitespace character.
                @")";
        }

        // Delimiter pattern.
        private string BuildDelimiterPattern()
        {
            // Use an alternation, exactly as the original regex does.
            switch (Delimiters)
            {
                case IniDelimiterMode.Equals: return @"(?<delimiter>=)";
                case IniDelimiterMode.Colon: return @"(?<delimiter>:)";
                default: return @"(?<delimiter>:|=)";   // Both.
            }
        }

        // Value pattern (plain, quoted, or multiline object).
        private string BuildValuePattern()
        {
            string commentChars = BuildCommentCharacters();
            string exclude = AllowInlineComments
                ? @"[^\r\n]*"
                : $@"[^{commentChars}\r\n]*";

            string quotedPattern = null;

            if (AllowQuotedValues)
            {
                // Build the quoted value pattern with correct escaping and multi-line support.
                // The pattern ensures:
                //   - Leading whitespace is consumed before the opening quote.
                //   - The opening quote (single or double) is captured in group 'quoted'.
                //   - The content (without quotes) is captured in group 'value'.
                //   - Escaped sequences (e.g. \", \\, \n) are consumed atomically.
                //   - The closing quote must match the opening quote and must not be escaped.
                if (AllowMultiLine)
                {
                    // Multi-line: allow any character inside, including newlines.
                    quotedPattern =
                        @"(?<quoted>[""'])" +
                        @"(?<value>(?:\\[\s\S]|(?!\k<quoted>)[\s\S])*)" +
                        @"\k<quoted>";
                }
                else
                {
                    // Single-line: forbid newlines inside.
                    quotedPattern =
                        @"(?<quoted>[""'])" +
                        @"(?<value>(?:\\[^\r\n]|(?!\k<quoted>)[^\r\n])*)" +
                        @"\k<quoted>";
                }
            }

            if (AllowMultiLine)
            {
                string obj = BuildObjectPattern();

                if (quotedPattern != null)
                {
                    // Priority: quoted → braced object → plain text.
                    return
                        $@"(?:" +
                            $@"[^\S\r\n]*{quotedPattern}" +     // quoted value (with optional leading spaces)
                            $@"|\s*(?<value>{obj})" +           // braced JSON-like object
                            $@"|[^\S\r\n]*(?<value>{exclude})" + // plain text
                        $@")";
                }

                return
                    $@"(?:" +
                        $@"\s*(?<value>{obj})" +
                        $@"|[^\S\r\n]*(?<value>{exclude})" +
                    $@")";
            }
            else
            {
                if (quotedPattern != null)
                {
                    return
                        $@"(?:" +
                            $@"[^\S\r\n]*{quotedPattern}" +     // quoted value (with optional leading spaces)
                            $@"|[^\S\r\n]*(?<value>{exclude})" + // plain text
                        $@")";
                }

                return
                    $@"[^\S\r\n]*(?<value>{exclude})";
            }
        }

        // Multiline JSON‑like object (balanced braces with embedded comments).
        private string BuildObjectPattern()
        {
            // Balanced braces with support for comments and strings.
            return @"\{" +
                   @"(?:" +
                       @"(?>" +
                           @"""(?:\\.|[^""])*""|" +           // Double‑quoted string.
                           @"//[^\r\n]*|" +                   // Single‑line comment.
                           @"/\*[\s\S]*?\*/|" +               // Multi‑line comment.
                           @"[^{}""/]+|" +                    // Ordinary text.
                           @"/(?![/*])" +                     // A slash not starting a comment.
                       @")" +
                       @"|(?<o>\{)" +                         // Opening brace → push.
                       @"|(?<-o>\})" +                        // Closing brace → pop.
                   @")*" +
                   @"(?(o)(?!))" +                            // Fail if unbalanced.
                   @"\}";
        }

        // Undefined pattern - handles all text that doesn't match comment, section or entry.
        // The behaviour depends on UndefinedTextMode:
        //   - Undefined → captures the text in a plain 'undefined' group.
        //   - Key       → creates a complete 'entry' with a 'key' group and an empty 'value'.
        //   - Value     → creates a complete 'entry' with an empty 'key' and a 'value' group.
        private string BuildUndefinedPattern()
        {
            switch (UndefinedText)
            {
                // Treat as an entry with a key only.
                case IniUndefinedTextMode.Key:
                    return @"(?<entry>(?<key>.+))";

                // Treat as an entry with a value only.
                case IniUndefinedTextMode.Value:
                    return @"(?<entry>(?<value>.+))";

                // Treat as undefined text, not an entry.
                default:
                    return @"(?<undefined>.+)";
            }
        }

        // Line breaker pattern.
        private string BuildLineBreakerPattern()
        {
            return @"(?<linebreaker>\r\n|\n)";
        }

        // Whitespace pattern (non‑line‑break spaces and tabs).
        private string BuildWhitespacePattern()
        {
            return @"(?<whitespace>(?>[^\S\r\n]+))";
        }

        // Comment characters based on CommentMode.
        private string BuildCommentCharacters()
        {
            var comments = Comments & IniCommentMode.Both;
            switch (comments)
            {
                case IniCommentMode.Hash:
                    return "#";
                case IniCommentMode.Semicolon:
                    return ";";
                default:
                    return "#;";
            }
        }

        /*
        FILE
        ├── COMMENT
        ├── KEY
        ├── SEPARATOR
        ├── VALUE
        │    ├── BOOLEAN
        │    │    ├── TRUE
        │    │    └── FALSE
        │    ├── NULL
        │    ├── STRING
        │    └── NUMBER
        │
        ├── ARRAY
        │    ├── OPEN
        │    └── CLOSE
        │
        ├── OBJECT
        │    ├── OPEN
        │    └── CLOSE
        │
        ├── WHITESPACE
        ├── NEWLINE
        └── UNDEFINED
        */

        // Configurable regex builder for JSON pattern.
        internal string BuildJsonPattern()
        {
            return
                // 1. Comment token - single‑line // ... or multi‑line /* ... */
                @"(?<Comment>//.*|/\*[\s\S]*?\*/)|" +

                // 2. Key token - a double‑quoted string immediately followed (after optional
                //    whitespace/comments) by a colon. The colon is not consumed.
                @"(?<key>""[^""\\]*(?:\\.[^""\\]*)*"")(?=(?:\s|//.*|/\*.*?\*/)*:)|" +

                // 3. Value token - boolean, null, string, or number
                @"(?<value>" +
                    @"(?<bool>true)|(?<bool>false)" + // Boolean.
                    @"(?<null>null)|" +               // Null.
                    @"""(?<string>[^""\\]*" +         // String: opening quote, content...
                    @"(?:\\.[^""\\]*)*)""|" +         // ...with escapes, then closing quote
                    @"(?<number>" +                   // Number:
                        @"-?" +                       // - optional minus;
                        @"(?:0|[1-9][0-9]*)" +        // - integer part;
                        @"(?:\.[0-9]+)?" +            // - optional fractional part;
                        @"(?:[eE][+-]?[0-9]+)?" +     // - optional exponent.
                    @")" +
                @")|" +

                // 4. Structural tokens
                @"(?<value_sep>:)|" +           // Colon that separates key and value
                @"(?<array_open>\[)|" +         // Array opening bracket
                @"(?<array_sep>,)|" +           // Array element separator
                @"(?<array_close>\])|" +        // Array closing bracket

                // 5. Object braces
                @"(?<object_open>{)|" +         // Object opening brace
                @"(?<object_close>})|" +        // Object closing brace

                // 6. Whitespace - any sequence of spaces or tabs (no line breaks).
                @"(?<whitespace>[^\S\r\n]+)|" +

                // 7. Newline - CRLF or LF.
                @"(?<newline>[\r\n]+)|" +

                // 8. Undefined - catch‑all for any other non‑whitespace content.
                @"(?<undefined>.+)";
        }
    }

    #endregion

    #region INI serialization attributes

    /// <summary>
    /// Indicates that the property value should be expanded when reading from an INI file.
    /// When applied to a property, the <see cref="IniFile.ReadSettings"/> method will use
    /// <see cref="IniFile.ReadExpandedString"/> instead of <see cref="IniFile.ReadString"/>,
    /// so that environment variables and pseudo‑variables (e.g., %TEMP%, %RANDOM%, %DATE%)
    /// are automatically replaced.
    /// </summary>
    /// <remarks>
    /// This attribute only affects reading. Writing is unaffected – values are stored as provided.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class IniExpandedAttribute : Attribute
    {
    }

    /// <summary>
    /// Indicates that a property should be ignored by the INI serialization methods.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    [Serializable]
    public sealed class IniIgnoreAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IniIgnoreAttribute"/> class.
        /// </summary>
        public IniIgnoreAttribute()
        {
        }
    }

    /// <summary>
    /// Attribute that associates a class or property with a specific section in the INI file.
    /// Used by the <see cref="IniFile.ReadSettings"/> and <see cref="IniFile.WriteSettings"/> methods
    /// to identify and process INI file sections.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    [Serializable]
    public sealed class IniSectionAttribute : Attribute
    {
        private readonly string _sectionName = null;

        /// <summary>
        /// Initializes a new instance of the <see cref="IniSectionAttribute"/> class with a specified section name.
        /// </summary>
        /// <param name="sectionName">The name of the INI section.</param>
        public IniSectionAttribute(string sectionName)
        {
            _sectionName = sectionName;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IniSectionAttribute"/> class with the default section name.
        /// </summary>
        public IniSectionAttribute()
        {
        }

        /// <summary>
        /// Gets the name of the INI section.
        /// </summary>
        public string Name => _sectionName;

        /// <inheritdoc />
        public override bool IsDefaultAttribute()
        {
            return _sectionName == null;
        }

        /// <inheritdoc />
        public override bool Match(object obj)
        {
            return obj is IniSectionAttribute attribute && attribute.Name.Equals(_sectionName);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return _sectionName;
        }
    }

    /// <summary>
    /// Attribute that associates a property with a specific entry in the INI file.
    /// Used by the <see cref="IniFile.ReadSettings"/> and <see cref="IniFile.WriteSettings"/> methods
    /// to identify and process individual INI file entries.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    [Serializable]
    public sealed class IniEntryAttribute : Attribute
    {
        private readonly string _entryName = null;

        /// <summary>
        /// Initializes a new instance of the <see cref="IniEntryAttribute"/> class with a specified entry name.
        /// </summary>
        /// <param name="entryName">The name of the INI entry.</param>
        public IniEntryAttribute(string entryName)
        {
            _entryName = entryName;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IniEntryAttribute"/> class with the default entry name.
        /// </summary>
        public IniEntryAttribute()
        {
        }

        /// <summary>
        /// Gets the name of the INI entry.
        /// </summary>
        public string Name => _entryName;

        /// <inheritdoc />
        public override bool IsDefaultAttribute()
        {
            return _entryName == null;
        }

        /// <inheritdoc />
        public override bool Match(object obj)
        {
            return obj is IniEntryAttribute attribute && attribute.Name.Equals(_entryName);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return _entryName;
        }

    }

    #endregion

    /// <summary>
    /// Represents a regular expression-based, collection-free INI file parser that preserves the original file formatting when editing entries.
    /// </summary>
    [Serializable]
    [DebuggerDisplay("{Content}")]
    public sealed class IniFile
    {
        /*********************************************** Class structure ***********************************************/

        #region Private fields

        // Maximum allowed nesting depth for recursive processing.
        [NonSerialized]
        private const int MaxNestingDepth = 64;

        // Default capacity for newly created internal collections.
        [NonSerialized]
        private const int DefaultCapacity = 16;

        // Private field for storing the content of the INI file.
        private string _content;

        // Cache of found matches, which improves performance.
        [NonSerialized]
        private List<Match> _matches;

        // Cached INI group indexes.
        [NonSerialized] private readonly int _iniValue;
        [NonSerialized] private readonly int _iniSection;
        [NonSerialized] private readonly int _iniKey;
        [NonSerialized] private readonly int _iniEntry;

        // Cached JSON group indexes.
        [NonSerialized] private readonly int _jsonComment;
        [NonSerialized] private readonly int _jsonWhitespace;
        [NonSerialized] private readonly int _jsonNewline;
        [NonSerialized] private readonly int _jsonObjectOpen;
        [NonSerialized] private readonly int _jsonObjectClose;
        [NonSerialized] private readonly int _jsonArrayOpen;
        [NonSerialized] private readonly int _jsonArrayClose;
        [NonSerialized] private readonly int _jsonArraySep;
        [NonSerialized] private readonly int _jsonValueSep;
        [NonSerialized] private readonly int _jsonKey;
        [NonSerialized] private readonly int _jsonValue;
        [NonSerialized] private readonly int _jsonBool;
        [NonSerialized] private readonly int _jsonNull;
        [NonSerialized] private readonly int _jsonString;
        [NonSerialized] private readonly int _jsonNumber;

        // Regular expression used for parsing the INI file.
        [NonSerialized]
        private readonly Regex _iniRegex;

        // Regular expression used for parsing the JSON entries.
        [NonSerialized]
        private readonly Regex _jsonRegex;

        // Indicates whether escape characters are allowed in the INI file.
        [NonSerialized]
        private readonly bool _allowEscapeChars;

        // Indicates whether multi line values are allowed in the INI file.
        [NonSerialized]
        private readonly bool _allowMultiLine;

        // Indicates whether value has been overriden when the same key appears more than once.
        [NonSerialized]
        private readonly bool _allowOverrides;

        // String used to represent line breaks in the INI file.
        [NonSerialized]
        private readonly string _lineBreaker = Environment.NewLine;

        // String used as delimiter between key and value in new entries.
        [NonSerialized]
        private readonly string _defaultDelimiter;

        // Contains culture-specific information for parsing.
        [NonSerialized]
        private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

        // Determines how string comparisons are performed in the INI file.
        // Configured based on settings passed to the constructor.
        [NonSerialized]
        private readonly StringComparison _comparison = StringComparison.InvariantCultureIgnoreCase;

        // Boolean values aliases.
        [NonSerialized]
        private readonly HashSet<string> _trueValues;

        [NonSerialized]
        private readonly HashSet<string> _falseValues;

            // Index of sections by name.Each key maps to one or more ranges in
        // _matches: the position of the section header and the position just past
        // its last entry. Multiple ranges per name occur when a section is
        // declared more than once in the file.
        [NonSerialized] private Dictionary<string, List<SectionRange>> _sectionIndex;

        // Index in _matches of the first named section header, or _matches.Count
        // if the file contains no named sections. Entries before this index belong
        // to the global section (empty section name).
        [NonSerialized]
        private int _firstSectionIndex;

        // One-slot cache of the most recently resolved section. Used to skip the
        // dictionary lookup when consecutive queries target the same section.
        // Invalidated whenever the content changes. _lastSectionRanges == null
        // with matching _lastSectionName means a cached miss.
        [NonSerialized]
        private string _lastSectionName;

        [NonSerialized]
        private List<SectionRange> _lastSectionRanges;

        // Characters used to separate enum flag names in a string representation.
        [NonSerialized] 
        private static readonly char[] _enumSeparator = new[] { ',', '|' };

        // Array containing the characters that are not allowed in path names.
        [NonSerialized]
        private static readonly char[] _invalidPathChars = Path.GetInvalidPathChars();

        // Characters used to separate segments of a path.
        [NonSerialized]
        private static readonly char[] _pathSeparatorChars = new[] { '/', '\\' };

        [NonSerialized]
        private static readonly Dictionary<string, Regex> _regexCache = new Dictionary<string, Regex>();

        // Bounded cache of regex bundles keyed by a compact signature of the
        // settings that influence the patterns. Concurrency-safe via lock, since
        // bundle construction is expensive and should happen at most once per key.
        [NonSerialized]
        private static readonly Dictionary<string, RegexBundle> _regexBundles =
            new Dictionary<string, RegexBundle>(StringComparer.Ordinal);

        [NonSerialized]
        private const int MaxRegexBundles = 32;

        #endregion

        #region Public properties

        /// <summary>
        /// Returns a string representing the contents of the INI file.
        /// </summary>
        public string Content
        {
            get
            {
                return _content ?? (_content = string.Empty);
            }
            set
            {
                _content = value ?? (_content = string.Empty);
                _matches.Clear();
                _sectionIndex.Clear();
                _firstSectionIndex = 0;
                _lastSectionName = null;
                _lastSectionRanges = null;

                if (string.IsNullOrEmpty(_content))
                    return;

                // Iterate over matches using the regex pattern and collect sections and entries names.
                for (Match match = _iniRegex.Match(_content); match.Success; match = match.NextMatch())
                {
                    GroupCollection groups = match.Groups;
                    if (groups[_iniSection].Success || groups[_iniEntry].Success)
                        _matches.Add(match);
                }

                BuildSectionIndex();
            }
        }

        #endregion

        /*********************************************** File operations ***********************************************/

        #region Constructors

        // Private constructor to prevent direct instantiation.
        private IniFile()
        { }

        private IniFile(string content, IniSettings settings)
        {

            if(content == null)
                throw new ArgumentNullException(nameof(content));

            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            // Store settings that are used throughout the class.
            var comparison = settings.Comparison;
            if ((uint)comparison > (uint)StringComparison.OrdinalIgnoreCase)
                throw new ArgumentOutOfRangeException(nameof(settings.Comparison));
            var comparer = GetComparer(comparison);
            var iniPattern = settings.BuildIniPatternEx();
            var jsonPattern = settings.BuildJsonPattern();
            var regexOptions = GetRegexOptions(comparison, RegexOptions.Compiled | RegexOptions.ExplicitCapture);

            _comparison = comparison;
            _allowEscapeChars = settings.AllowEscapeChars;
            _allowMultiLine = settings.AllowMultiLine;
            _allowOverrides = settings.DuplicateKeyOverride;
            _defaultDelimiter = GetDelimiter(settings.Delimiters);
            _culture = GetCultureInfo(comparison);
            _lineBreaker = AutoDetectLineBreaker(content);
            _matches = new List<Match>(DefaultCapacity);
            _sectionIndex = new Dictionary<string, List<SectionRange>>(comparer);
            _firstSectionIndex = 0;
            _lastSectionName = null;
            _lastSectionRanges = null;
            _trueValues = new HashSet<string>(comparer) { "true", "yes", "on", "enable", "1" };
            _falseValues = new HashSet<string>(comparer) { "false", "no", "off", "disable", "0" };


            // Initialize parsing engine.
            RegexBundle bundle = GetOrCreateRegexBundle(settings);
            _iniRegex = bundle.Ini;
            _jsonRegex = bundle.Json;

            _iniSection = bundle.IniSection;
            _iniEntry = bundle.IniEntry;
            _iniKey = bundle.IniKey;
            _iniValue = bundle.IniValue;

            _jsonComment = bundle.JsonComment;
            _jsonWhitespace = bundle.JsonWhitespace;
            _jsonNewline = bundle.JsonNewline;
            _jsonObjectOpen = bundle.JsonObjectOpen;
            _jsonObjectClose = bundle.JsonObjectClose;
            _jsonArrayOpen = bundle.JsonArrayOpen;
            _jsonArrayClose = bundle.JsonArrayClose;
            _jsonArraySep = bundle.JsonArraySep;
            _jsonValueSep = bundle.JsonValueSep;
            _jsonKey = bundle.JsonKey;
            _jsonValue = bundle.JsonValue;
            _jsonBool = bundle.JsonBool;
            _jsonNull = bundle.JsonNull;
            _jsonString = bundle.JsonString;
            _jsonNumber = bundle.JsonNumber;

            // Start parsing the content.
            Content = content;
        }

        // Constructor accepting ini content as a string and settings.
        // Initializes the parser settings, setting the comparison rules,
        // regular expression pattern, escape character allowance, and delimiter
        // based on the provided settings.
        [Obsolete("This method is obsolete. Use the overload with IniSettings parameter instead. This method will be removed in a future version.")]
        private IniFile(string content,
            StringComparison comparison = StringComparison.InvariantCultureIgnoreCase,
            bool allowEscChars = true, bool allowMultiLine = true) 
            : this(content, new IniSettings()
            {
                Comparison = comparison, 
                AllowEscapeChars = allowEscChars, 
                AllowMultiLine = allowMultiLine
            })
        {
        }

        #endregion

        #region Factory methods

        /// <summary>
        /// Creates a new empty <see cref="IniFile"/> with the specified settings.
        /// If <paramref name="settings"/> is null, default settings are used.
        /// </summary>
        /// <param name="content">The string containing data of the INI file.</param>
        /// <param name="settings">The parsing settings, or null for defaults.</param>
        /// <returns>A new <see cref="IniFile"/> instance.</returns>
        public static IniFile Create(string content, IniSettings settings = null)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            return new IniFile(content, settings ?? IniSettings.Parse(content));
        }

        /// <summary>
        /// Creates a new empty <see cref="IniFile"/> with the specified settings.
        /// If <paramref name="settings"/> is null, default settings are used.
        /// </summary>
        /// <param name="settings">The parsing settings, or null for defaults.</param>
        /// <returns>A new <see cref="IniFile"/> instance.</returns>
        public static IniFile Create(IniSettings settings = null)
        {
            return new IniFile(string.Empty, settings ?? IniSettings.Default);
        }

        /// <summary>
        /// Loads an INI file from a <see cref="TextReader"/> with the specified settings.
        /// If <paramref name="settings"/> is null, default settings are used.
        /// </summary>
        /// <param name="reader">The <see cref="TextReader"/> containing the INI data.</param>
        /// <param name="settings">The parsing settings, or null for defaults.</param>
        /// <returns>A new <see cref="IniFile"/> instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="reader"/> is null.</exception>
        public static IniFile Load(TextReader reader, IniSettings settings = null)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            string content = reader.ReadToEnd();

            return new IniFile(content, settings ?? IniSettings.Parse(content));
        }

        /// <summary>
        /// Loads an INI file from a <see cref="Stream"/> with the specified settings.
        /// If <paramref name="settings"/> is null, default settings are used.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> containing the INI data.</param>
        /// <param name="encoding">The encoding to use; if null, UTF-8 is used.</param>
        /// <param name="settings">The parsing settings, or null for defaults.</param>
        /// <returns>A new <see cref="IniFile"/> instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is null.</exception>
        public static IniFile Load(Stream stream, Encoding encoding, IniSettings settings = null)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            using (var reader = new StreamReader(stream, encoding ?? Encoding.UTF8))
            {
                string content = reader.ReadToEnd();
                return new IniFile(content, settings ?? IniSettings.Parse(content));
            }
        }

        /// <summary>
        /// Loads an INI file from a file path with the specified encoding and settings.
        /// If <paramref name="settings"/> is null, default settings are used.
        /// </summary>
        /// <param name="fileName">The path to the INI file.</param>
        /// <param name="encoding">The encoding to use; if null, auto-detection is attempted.</param>
        /// <param name="settings">The parsing settings, or null for defaults.</param>
        /// <returns>A new <see cref="IniFile"/> instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="fileName"/> is null.</exception>
        public static IniFile Load(string fileName, Encoding encoding, IniSettings settings = null)
        {
            if (fileName == null)
                throw new ArgumentNullException(nameof(fileName));

            string fullPath = GetFullPath(fileName, true);
            string content = File.ReadAllText(fullPath, encoding ?? AutoDetectEncoding(fullPath, Encoding.UTF8));

            return new IniFile(content, settings ?? IniSettings.Parse(content));
        }

        /// <summary>
        /// Loads an INI file from a file path with the specified settings (auto-detects encoding).
        /// If <paramref name="settings"/> is null, default settings are used.
        /// </summary>
        /// <param name="fileName">The path to the INI file.</param>
        /// <param name="settings">The parsing settings, or null for defaults.</param>
        /// <returns>A new <see cref="IniFile"/> instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="fileName"/> is null.</exception>
        public static IniFile Load(string fileName, IniSettings settings = null)
        {
            if (fileName == null)
                throw new ArgumentNullException(nameof(fileName));

            string fullPath = GetFullPath(fileName, true);
            string content = File.ReadAllText(fullPath, AutoDetectEncoding(fullPath, Encoding.UTF8));

            return new IniFile(content, settings ?? IniSettings.Parse(content));
        }

        /// <summary>
        /// Loads an INI file if it exists; otherwise creates an empty file with the specified settings.
        /// If <paramref name="settings"/> is null, default settings are used.
        /// </summary>
        /// <param name="fileName">The path to the INI file.</param>
        /// <param name="encoding">The encoding to use; if null, auto-detection is attempted.</param>
        /// <param name="settings">The parsing settings, or null for defaults.</param>
        /// <returns>A new <see cref="IniFile"/> instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="fileName"/> is null.</exception>
        public static IniFile LoadOrCreate(string fileName, Encoding encoding, IniSettings settings = null)
        {
            if (fileName == null)
                throw new ArgumentNullException(nameof(fileName));

            string fullPath = GetFullPath(fileName);
            string content = File.Exists(fullPath) 
                ? File.ReadAllText(fullPath, encoding ?? AutoDetectEncoding(fullPath, Encoding.UTF8)) 
                : string.Empty;

            return new IniFile(content, settings ?? IniSettings.Parse(content));
        }

        /// <summary>
        /// Loads an INI file if it exists; otherwise creates an empty file with the specified settings (auto-detects encoding).
        /// If <paramref name="settings"/> is null, default settings are used.
        /// </summary>
        /// <param name="fileName">The path to the INI file.</param>
        /// <param name="settings">The parsing settings, or null for defaults.</param>
        /// <returns>A new <see cref="IniFile"/> instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="fileName"/> is null.</exception>
        public static IniFile LoadOrCreate(string fileName, IniSettings settings = null)
        {
            if (fileName == null)
                throw new ArgumentNullException(nameof(fileName));

            string fullPath = GetFullPath(fileName);
            string content = File.Exists(fullPath) 
                ? File.ReadAllText(fullPath, AutoDetectEncoding(fullPath, Encoding.UTF8)) 
                : string.Empty;

            return new IniFile(content, settings ?? IniSettings.Parse(content));
        }

        /// <summary>
        /// Create a new instance of <see cref="IniFile"/> with empty content.
        /// </summary>
        [Obsolete("This method is obsolete. Use the overload with IniSettings parameter instead. This method will be removed in a future version.")]
        public static IniFile Create(StringComparison comparison,
            bool allowEscChars, bool allowMultiLine)
        {
            return new IniFile(string.Empty, comparison, allowEscChars, allowMultiLine);
        }

        /// <summary>
        /// Loads an INI file from a <see cref="TextReader"/> and initializes a new <see cref="IniFile"/> instance.
        /// </summary>
        [Obsolete("This method is obsolete. Use the overload with IniSettings parameter instead. This method will be removed in a future version.")]
        public static IniFile Load(TextReader reader,
            StringComparison comparison,
            bool allowEscChars, bool allowMultiLine)
        {
            return new IniFile(reader.ReadToEnd(), comparison, allowEscChars, allowMultiLine);
        }

        /// <summary>
        /// Loads an INI file from a <see cref="Stream"/> and initializes a new <see cref="IniFile"/> instance.
        /// </summary>
        [Obsolete("This method is obsolete. Use the overload with IniSettings parameter instead. This method will be removed in a future version.")]
        public static IniFile Load(Stream stream, Encoding encoding,
            StringComparison comparison,
            bool allowEscChars, bool allowMultiLine)
        {
            using (StreamReader reader = new StreamReader(stream ?? throw new ArgumentNullException(nameof(stream)), encoding ?? Encoding.UTF8))
                return new IniFile(reader.ReadToEnd(), comparison, allowEscChars, allowMultiLine);
        }

        /// <summary>
        /// Loads an INI file and initializes a new <see cref="IniFile"/> instance.
        /// </summary>
        [Obsolete("This method is obsolete. Use the overload with IniSettings parameter instead. This method will be removed in a future version.")]
        public static IniFile Load(string fileName,
            Encoding encoding,
            StringComparison comparison,
            bool allowEscChars, bool allowMultiLine)
        {
            string filePath = GetFullPath(fileName, true);
            return new IniFile(File.ReadAllText(filePath, encoding ?? AutoDetectEncoding(filePath, Encoding.UTF8)),
                comparison, allowEscChars, allowMultiLine);
        }

        /// <summary>
        /// Loads an INI file and initializes a new <see cref="IniFile"/> instance.
        /// </summary>
        [Obsolete("This method is obsolete. Use the overload with IniSettings parameter instead. This method will be removed in a future version.")]
        public static IniFile Load(string fileName,
            StringComparison comparison,
            bool allowEscChars, bool allowMultiLine)
        {
            if (fileName == null)
                throw new ArgumentNullException(nameof(fileName));

            string filePath = GetFullPath(fileName, true);
            Encoding encoding = AutoDetectEncoding(filePath, Encoding.UTF8);

            return new IniFile(File.ReadAllText(filePath, encoding),
                comparison, allowEscChars, allowMultiLine);
        }

        /// <summary>
        /// Loads an INI file if it exists; otherwise, creates an empty <see cref="IniFile"/>.
        /// </summary>
        [Obsolete("This method is obsolete. Use the overload with IniSettings parameter instead. This method will be removed in a future version.")]
        public static IniFile LoadOrCreate(string fileName, Encoding encoding,
            StringComparison comparison,
            bool allowEscChars, bool allowMultiLine)
        {
            if (fileName == null)
                throw new ArgumentNullException(nameof(fileName));

            string filePath = GetFullPath(fileName);
            if (encoding == null)
                encoding = AutoDetectEncoding(filePath, Encoding.UTF8);

            return new IniFile(
                File.Exists(filePath)
                    ? File.ReadAllText(filePath, encoding)
                    : string.Empty,
                comparison, allowEscChars, allowMultiLine);
        }

        /// <summary>
        /// Loads an INI file if it exists; otherwise, creates an empty <see cref="IniFile"/>.
        /// </summary>
        [Obsolete("This method is obsolete. Use the overload with IniSettings parameter instead. This method will be removed in a future version.")]
        public static IniFile LoadOrCreate(string fileName,
            StringComparison comparison,
            bool allowEscChars, bool allowMultiLine)
        {
            if (fileName == null)
                throw new ArgumentNullException(nameof(fileName));

            string filePath = GetFullPath(fileName);
            Encoding encoding = AutoDetectEncoding(filePath, Encoding.UTF8);

            return new IniFile(
                File.Exists(filePath)
                    ? File.ReadAllText(filePath, encoding)
                    : string.Empty,
                comparison, allowEscChars, allowMultiLine);
        }

        #endregion

        #region Save methods

        /// <summary>
        /// Saves the INI file content to a <see cref="TextWriter"/>.
        /// </summary>
        /// <param name="writer">The <see cref="TextWriter"/> where the INI file data will be written.</param>
        /// <param name="justify">
        /// When <c>true</c>, a normalized (justified) representation of the content
        /// is written instead of the raw content. The justified form contains only
        /// sections and key-value pairs, without comments, empty lines, and extra
        /// whitespace. The <see cref="IniFile"/> instance itself is not modified.
        /// </param>
        public void Save(TextWriter writer, bool justify = false)
        {
            writer.Write(justify ? Justify() : Content);
        }

        /// <summary>
        /// Saves the INI file content to a <see cref="Stream"/> using the specified encoding.
        /// </summary>
        /// <param name="stream">
        /// The <see cref="Stream"/> where the INI file data will be written.
        /// </param>
        /// <param name="encoding">
        /// The <see cref="Encoding"/> used to write the data to the stream.
        /// </param>
        /// <param name="justify">
        /// When <c>true</c>, a normalized (justified) representation of the content
        /// is written instead of the raw content. The justified form contains only
        /// sections and key-value pairs, without comments, empty lines, and extra
        /// whitespace. The <see cref="IniFile"/> instance itself is not modified.
        /// </param>
        public void Save(Stream stream, Encoding encoding = null, bool justify = false)
        {
            using (StreamWriter writer = new StreamWriter(stream, encoding ?? Encoding.UTF8))
            {
                writer.Write(justify ? Justify() : Content);
            }
        }

        /// <summary>
        /// Saves the INI file content to a file specified by its path using the specified encoding.
        /// </summary>
        /// <param name="fileName">
        /// The path to the file where the INI data will be saved.
        /// </param>
        /// <param name="encoding">
        /// The <see cref="Encoding"/> used to write the file.
        /// </param>
        /// <param name="justify">
        /// When <c>true</c>, a normalized (justified) representation of the content
        /// is written instead of the raw content. The justified form contains only
        /// sections and key-value pairs, without comments, empty lines, and extra
        /// whitespace. The <see cref="IniFile"/> instance itself is not modified.
        /// </param>
        public void Save(string fileName, Encoding encoding = null, bool justify = false)
        {
            string fullPath = GetFullPath(fileName);
            File.WriteAllText(fullPath, justify ? Justify() : Content, encoding ?? Encoding.UTF8);
        }

        #endregion

        #region Static file access methods

        /// <summary>
        /// Reads a value of type <typeparamref name="T"/> from the specified INI file,
        /// section, and key. If the file does not exist, returns <paramref name="defaultValue"/>.
        /// </summary>
        /// <typeparam name="T">The type of the value to read.</typeparam>
        /// <param name="fileName">Path to the INI file.</param>
        /// <param name="section">Section name. Pass <c>null</c> for global entries.</param>
        /// <param name="key">Key name.</param>
        /// <param name="defaultValue">Default value returned if the entry is not found.</param>
        /// <param name="allowEscChars">
        /// Indicates whether escape characters are allowed in the INI file.
        ///</param>
        /// <param name="allowMultiLine">
        /// Indicates whether multiline blocks enclosed in '{' and '}' are allowed in the INI file.
        ///</param>
        /// <returns>The read value, or <paramref name="defaultValue"/> if not found.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <c>null</c>.</exception>
        public static T ReadFromFile<T>(string fileName, string section, string key, T defaultValue = default,
            bool allowEscChars = false, bool allowMultiLine = false)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (!File.Exists(fileName))
                return defaultValue;

            using (var reader = new StreamReader(fileName, AutoDetectEncoding(fileName, Encoding.UTF8)))
            {
                var ini = Load(reader,
                    StringComparison.InvariantCultureIgnoreCase,
                    allowEscChars,
                    allowMultiLine);

                return ini.Read<T>(section, key, defaultValue);
            }
        }

        /// <summary>
        /// Writes a value of type <typeparamref name="T"/> to the specified INI file,
        /// section, and key. If the file does not exist, it is created.
        /// </summary>
        /// <typeparam name="T">The type of the value to write.</typeparam>
        /// <param name="fileName">Path to the INI file.</param>
        /// <param name="section">Section name. Pass <c>null</c> for global entries.</param>
        /// <param name="key">Key name.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="allowEscChars">
        /// Indicates whether escape characters are allowed in the INI file.
        ///</param>
        /// <param name="allowMultiLine">
        /// Indicates whether multiline blocks enclosed in '{' and '}' are allowed in the INI file.
        ///</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <c>null</c>.</exception>
        public static void WriteToFile<T>(string fileName, string section, string key, T value,
            bool allowEscChars = false, bool allowMultiLine = false)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            var ini = LoadOrCreate(fileName,
                Encoding.UTF8,
                StringComparison.InvariantCultureIgnoreCase,
                allowEscChars,
                allowMultiLine);

            ini.Write<T>(section, key, value);
            ini.Save(fileName, Encoding.UTF8);
        }

        /// <summary>
        /// Exports the content of the specified INI file to a dictionary mapping section names to a dictionary
        /// of keys with lists of their associated values (preserving order and duplicates).
        /// </summary>
        /// <param name="fileName">Path to the INI file.</param>
        /// <param name="encoding">The encoding to use when reading the file. If <c>null</c>, auto-detection is attempted.</param>
        /// <param name="comparison">String comparison rules for case sensitivity.</param>
        /// <param name="allowEscChars">Whether to process escape sequences in values.</param>
        /// <param name="allowMultiLine">Whether to support multiline values wrapped in braces.</param>
        /// <returns>
        /// A dictionary where the key is the section name (empty string for global entries)
        /// and the value is a dictionary of key > list of values for that section.
        /// Returns an empty dictionary if the file does not exist or cannot be read.
        /// </returns>
        public static Dictionary<string, Dictionary<string, List<string>>> ExportToDictionaryFile(
            string fileName,
            Encoding encoding = null,
            StringComparison comparison = StringComparison.InvariantCultureIgnoreCase,
            bool allowEscChars = true,
            bool allowMultiLine = true)
        {
            if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName))
                return new Dictionary<string, Dictionary<string, List<string>>>(DefaultCapacity, GetComparer(comparison));

            try
            {
                using (var reader = new StreamReader(fileName, encoding ?? AutoDetectEncoding(fileName, Encoding.UTF8)))
                {
                    var ini = Load(reader, comparison, allowEscChars, allowMultiLine);
                    return ini.ExportToDictionary();
                }
            }
            catch
            {
                return new Dictionary<string, Dictionary<string, List<string>>>(DefaultCapacity, GetComparer(comparison));
            }
        }

        /// <summary>
        /// Imports the specified dictionary into the INI file at the given path.
        /// If the file does not exist, it is created.
        /// </summary>
        /// <param name="fileName">Path to the INI file.</param>
        /// <param name="data">
        /// The dictionary to import. See <see cref="ImportFromDictionary"/> for
        /// the expected format and semantics.
        /// </param>
        /// <param name="encoding">
        /// The encoding used to read and write the file. If <c>null</c>, auto-detection
        /// is attempted on read and UTF-8 is used on write.
        /// </param>
        /// <param name="comparison">String comparison rules for case sensitivity.</param>
        /// <param name="allowEscChars">Whether to process escape sequences in values.</param>
        /// <param name="allowMultiLine">Whether to support multiline values wrapped in braces.</param>
        /// <param name="replace">
        /// When <c>false</c> (default), the data is merged into the existing content.
        /// When <c>true</c>, the existing content is cleared first.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="fileName"/> or <paramref name="data"/> is <c>null</c>.
        /// </exception>
        public static void ImportFromDictionaryFile(
            string fileName,
            IDictionary<string, Dictionary<string, List<string>>> data,
            Encoding encoding = null,
            StringComparison comparison = StringComparison.InvariantCultureIgnoreCase,
            bool allowEscChars = true,
            bool allowMultiLine = true,
            bool replace = false)
        {
            if (fileName == null)
                throw new ArgumentNullException(nameof(fileName));
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            var settings = new IniSettings
            {
                Comparison = comparison,
                AllowEscapeChars = allowEscChars,
                AllowMultiLine = allowMultiLine
            };

            IniFile ini = LoadOrCreate(fileName, encoding, settings);
            ini.ImportFromDictionary(data, replace);
            ini.Save(fileName, encoding);
        }

        #endregion

        /****************************************** Core of content processing *****************************************/

        #region Embeded classes

        // A contiguous slice of _matches representing one occurrence of a section:
        // the section header at Start followed by its entries up to (but not
        // including) End. End is either the index of the next section header or
        // _matches.Count for the last section.
        private readonly struct SectionRange
        {
            public readonly int Start;
            public readonly int End;

            public SectionRange(int start, int end)
            {
                Start = start;
                End = end;
            }
        }

        // Watches an object implementing INotifyPropertyChanged and writes changed
        // properties to the owning IniFile. Disposing unsubscribes from the event.
        private sealed class SettingsWatcher : IDisposable
        {
            private readonly IniFile _ini;
            private readonly INotifyPropertyChanged _obj;
            private readonly Dictionary<string, PropertyInfo> _properties;
            private bool _disposed;

            public SettingsWatcher(IniFile ini, INotifyPropertyChanged obj)
            {
                _ini = ini;
                _obj = obj;

                // Build a map of property name → PropertyInfo, skipping properties
                // that are not supposed to participate in serialization.
                StringComparer comparer = GetComparer(ini._comparison);
                PropertyInfo[] properties = obj.GetType().GetProperties(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                int propertiesLength = properties.Length;
                _properties = new Dictionary<string, PropertyInfo>(propertiesLength, comparer);

                for (int i = 0; i < propertiesLength; i++)
                {
                    PropertyInfo property = properties[i];

                    // Skip write-only properties (their getter would throw).
                    if (!property.CanRead)
                        continue;

                    // Skip properties marked with [IniIgnore].
                    if (property.GetCustomAttributes(typeof(IniIgnoreAttribute), false).Length > 0)
                        continue;

                    _properties[property.Name] = property;
                }

                _obj.PropertyChanged += OnPropertyChanged;
            }

            private void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
            {
                if (_disposed)
                    return;

                // An empty or null property name means "all properties changed" —
                // a common convention (e.g. WPF, MVVM helpers).
                if (string.IsNullOrEmpty(e.PropertyName))
                {
                    foreach (PropertyInfo property in _properties.Values)
                        _ini.WriteProperty(property, _obj);

                    return;
                }

                // Write only the property that has actually changed.
                if (_properties.TryGetValue(e.PropertyName, out PropertyInfo changed))
                    _ini.WriteProperty(changed, _obj);
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _obj.PropertyChanged -= OnPropertyChanged;
            }
        }

        // Dynamic object wrapper that behaves similarly to ExpandoObject,
        // but allows custom handling of missing members and provides dictionary access.
        private class SafeExpandoObject : DynamicObject, IDictionary<string, object>
        {
            // Internal storage for dynamic properties.
            private readonly Dictionary<string, object> _values =
                new Dictionary<string, object>();

            // Gets a dynamic property value by its name.
            // Returns false when the property does not exist, causing the dynamic binder
            // to handle the missing member according to the default behavior.
            public override bool TryGetMember(GetMemberBinder binder, out object result)
            {
                return _values.TryGetValue(binder.Name, out result)
                       || (result = null) == null;
            }

            // Sets a dynamic property value by its name.
            public override bool TrySetMember(SetMemberBinder binder, object value)
            {
                try
                {
                    _values[binder.Name] = value;
                }
                catch
                {
                    return false;
                }
                return true;
            }

            // Provides dictionary-style access to dynamic properties.
            // Returns null instead of throwing an exception when the key is missing.
            public object this[string key]
            {
                get
                {
                    object value;
                    return _values.TryGetValue(key, out value) ? value : null;
                }
                set
                {
                    _values[key] = value;
                }
            }

            // Returns a collection of all property names.
            public ICollection<string> Keys => _values.Keys;

            // Returns a collection of all property values.
            public ICollection<object> Values => _values.Values;

            // Returns the number of stored properties.
            public int Count => _values.Count;

            // Indicates whether the collection can be modified.
            public bool IsReadOnly => false;

            // Adds a new property with the specified name and value.
            public void Add(string key, object value)
            {
                _values.Add(key, value);
            }

            // Adds a new property using a key-value pair.
            public void Add(KeyValuePair<string, object> item)
            {
                _values.Add(item.Key, item.Value);
            }

            // Checks whether a property with the specified name exists.
            public bool ContainsKey(string key)
            {
                return _values.ContainsKey(key);
            }

            // Removes a property by its name.
            public bool Remove(string key)
            {
                return _values.Remove(key);
            }

            // Gets a property value by its name.
            public bool TryGetValue(string key, out object value)
            {
                return _values.TryGetValue(key, out value);
            }

            // Removes all stored properties.
            public void Clear()
            {
                _values.Clear();
            }

            // Checks whether the collection contains the specified key-value pair.
            public bool Contains(KeyValuePair<string, object> item)
            {
                return ((ICollection<KeyValuePair<string, object>>)_values)
                    .Contains(item);
            }

            // Copies all properties to an array starting from the specified index.
            public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex)
            {
                ((ICollection<KeyValuePair<string, object>>)_values)
                    .CopyTo(array, arrayIndex);
            }

            // Removes the specified key-value pair from the collection.
            public bool Remove(KeyValuePair<string, object> item)
            {
                return ((ICollection<KeyValuePair<string, object>>)_values)
                    .Remove(item);
            }

            // Returns an enumerator for iterating through stored properties.
            public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
            {
                return _values.GetEnumerator();
            }

            // Returns a non-generic enumerator for IEnumerable compatibility.
            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            // Converts SafeExpandoObject to a standard ExpandoObject instance.
            public static explicit operator ExpandoObject(SafeExpandoObject source)
            {
                var result = new ExpandoObject();
                var dict = (IDictionary<string, object>)result;

                foreach (var pair in source)
                {
                    dict[pair.Key] = pair.Value;
                }

                return result;
            }
        }

        // A shared bundle of compiled regular expressions and cached group indices.
        // Bundles are keyed by the effective IniSettings signature and are reused
        // across IniFile instances with identical settings, so the expensive
        // pattern construction and Regex compilation happen at most once per unique
        // configuration.
        private sealed class RegexBundle
        {
            public readonly Regex Ini;
            public readonly Regex Json;

            // INI group indices.
            public readonly int IniSection;
            public readonly int IniEntry;
            public readonly int IniKey;
            public readonly int IniValue;

            // JSON group indices.
            public readonly int JsonComment;
            public readonly int JsonWhitespace;
            public readonly int JsonNewline;
            public readonly int JsonObjectOpen;
            public readonly int JsonObjectClose;
            public readonly int JsonArrayOpen;
            public readonly int JsonArrayClose;
            public readonly int JsonArraySep;
            public readonly int JsonValueSep;
            public readonly int JsonKey;
            public readonly int JsonValue;
            public readonly int JsonBool;
            public readonly int JsonNull;
            public readonly int JsonString;
            public readonly int JsonNumber;

            public RegexBundle(Regex ini, Regex json)
            {
                Ini = ini;
                Json = json;

                IniSection = ini.GroupNumberFromName("section");
                IniEntry = ini.GroupNumberFromName("entry");
                IniKey = ini.GroupNumberFromName("key");
                IniValue = ini.GroupNumberFromName("value");

                JsonComment = json.GroupNumberFromName("Comment");
                JsonWhitespace = json.GroupNumberFromName("whitespace");
                JsonNewline = json.GroupNumberFromName("newline");
                JsonObjectOpen = json.GroupNumberFromName("object_open");
                JsonObjectClose = json.GroupNumberFromName("object_close");
                JsonArrayOpen = json.GroupNumberFromName("array_open");
                JsonArrayClose = json.GroupNumberFromName("array_close");
                JsonArraySep = json.GroupNumberFromName("array_sep");
                JsonValueSep = json.GroupNumberFromName("value_sep");
                JsonKey = json.GroupNumberFromName("key");
                JsonValue = json.GroupNumberFromName("value");
                JsonBool = json.GroupNumberFromName("bool");
                JsonNull = json.GroupNumberFromName("null");
                JsonString = json.GroupNumberFromName("string");
                JsonNumber = json.GroupNumberFromName("number");
            }
        }

        #endregion

        #region Internal data access methods

        // Rebuilds _sectionIndex and _firstSectionIndex from the current _matches.
        // Called whenever the content changes, so the index always reflects the
        // latest state of the file.
        private void BuildSectionIndex()
        {
            _firstSectionIndex = _matches.Count;

            int currentStart = -1;
            string currentName = null;

            for (int i = 0; i < _matches.Count; i++)
            {
                Match match = _matches[i];
                if (!match.Groups[_iniSection].Success)
                    continue;

                if (currentStart < 0)
                    _firstSectionIndex = i;
                else
                    AddSectionRange(currentName, currentStart, i);

                currentName = match.Groups[_iniValue].Value;
                currentStart = i;
            }

            if (currentStart >= 0)
                AddSectionRange(currentName, currentStart, _matches.Count);
        }

        private void AddSectionRange(string name, int start, int end)
        {
            if (!_sectionIndex.TryGetValue(name, out List<SectionRange> list))
            {
                list = new List<SectionRange>(1);
                _sectionIndex[name] = list;
            }
            list.Add(new SectionRange(start, end));
        }

        // Resolves a section name to its ranges in _matches, using a one-slot
        // cache to skip the dictionary lookup when consecutive queries target
        // the same section. Returns false for a null, empty, or missing section;
        // the miss is also cached so that repeated probes for the same unknown
        // name do not re-enter the dictionary.
        private bool TryGetSectionRanges(string section, out List<SectionRange> ranges)
        {
            ranges = null;

            if (string.IsNullOrEmpty(section))
                return false;

            if (string.Equals(section, _lastSectionName, _comparison))
            {
                ranges = _lastSectionRanges;
                return ranges != null;
            }

            if (_sectionIndex.TryGetValue(section, out ranges))
            {
                _lastSectionName = section;
                _lastSectionRanges = ranges;
                return true;
            }

            _lastSectionName = section;
            _lastSectionRanges = null;
            return false;
        }

        // Tries to get the name of the specified section as it appears in the file,
        // preserving the original casing. Returns true if the section exists.
        private bool TryGetSection(string section, out string sectionName)
        {
            sectionName = null;

            if (!TryGetSectionRanges(section, out List<SectionRange> ranges))
                return false;

            // Return the original casing from the first occurrence.
            sectionName = _matches[ranges[0].Start].Groups[_iniValue].Value;
            return true;
        }

        // Method to retrieve all sections in the INI file.
        private IEnumerable<string> GetSections()
        {
            HashSet<string> sections = new HashSet<string>(GetComparer(_comparison));

            for (int i = 0; i < _matches.Count; i++)
            {
                Match match = _matches[i];

                if (match.Groups[_iniSection].Success)
                {
                    // Convert to lowercase if ignore case mode is enabled.
                    Group group = match.Groups[_iniValue];
                    string section = NormalizeSubstring(_content, group.Index, group.Length, _comparison);
                    sections.Add(section);
                }
            }

            return sections;
        }

        // Method to retrieve all keys in a specific section.
        private IEnumerable<string> GetKeys(string section)
        {
            HashSet<string> keys = new HashSet<string>(GetComparer(_comparison));

            if (string.IsNullOrEmpty(section))
            {
                // Global entries: those located before the first named section.
                for (int i = 0; i < _firstSectionIndex; i++)
                {
                    Match match = _matches[i];
                    if (!match.Groups[_iniEntry].Success)
                        continue;

                    Group g = match.Groups[_iniKey];
                    keys.Add(NormalizeSubstring(_content, g.Index, g.Length, _comparison));
                }
                return keys;
            }

            if (!TryGetSectionRanges(section, out List<SectionRange> ranges))
                return keys;

            for (int r = 0; r < ranges.Count; r++)
            {
                SectionRange range = ranges[r];
                // Start + 1 skips the section header itself.
                for (int i = range.Start + 1; i < range.End; i++)
                {
                    Match match = _matches[i];
                    if (!match.Groups[_iniEntry].Success)
                        continue;

                    Group g = match.Groups[_iniKey];
                    keys.Add(NormalizeSubstring(_content, g.Index, g.Length, _comparison));
                }
            }

            return keys;
        }

        // Tries to get the name of the specified key as it appears in the file,
        // preserving the original casing. Returns true if the key exists.
        private bool TryGetKey(string section, string key, out string keyName)
        {
            keyName = null;
            if (key == null)
                return false;

            if (string.IsNullOrEmpty(section))
            {
                for (int i = 0; i < _firstSectionIndex; i++)
                {
                    Match match = _matches[i];
                    if (!match.Groups[_iniEntry].Success)
                        continue;

                    Group keyGroup = match.Groups[_iniKey];
                    if (SubstringEquals(_content, keyGroup.Index, keyGroup.Length, key, _comparison))
                    {
                        keyName = keyGroup.Value;
                        return true;
                    }
                }
                return false;
            }

            if (!TryGetSectionRanges(section, out List<SectionRange> ranges))
                return false;

            for (int r = 0; r < ranges.Count; r++)
            {
                SectionRange range = ranges[r];
                for (int i = range.Start + 1; i < range.End; i++)
                {
                    Match match = _matches[i];
                    if (!match.Groups[_iniEntry].Success)
                        continue;

                    Group keyGroup = match.Groups[_iniKey];
                    if (SubstringEquals(_content, keyGroup.Index, keyGroup.Length, key, _comparison))
                    {
                        keyName = keyGroup.Value;
                        return true;
                    }
                }
            }

            return false;
        }

        // Tries to get a value associated with the specified section and key.
        // Returns true if the entry exists. When DuplicateKeyOverride is enabled,
        // the last occurrence is returned; otherwise the first.
        private bool TryGetValue(string section, string key, out string value)
        {
            value = null;
            bool found = false;
			
			// Global entries: scan the region before the first named section.
            if (string.IsNullOrEmpty(section))
            {
                for (int i = 0; i < _firstSectionIndex; i++)
                {
                    Match match = _matches[i];
                    if (!match.Groups[_iniEntry].Success)
                        continue;

					Group keyGroup = match.Groups[_iniKey];
                    if (!SubstringEquals(_content, keyGroup.Index, keyGroup.Length, key, _comparison))
                        continue;

                    // Found key.
					value = match.Groups[_iniValue].Value;
                    found = true;

                    // First match wins unless override mode is enabled.
                    if (!_allowOverrides)
                        return true;
                }
                return found;
            }
			
			 // Named section: use the precomputed ranges, one entry loop per occurrence.
			if (!TryGetSectionRanges(section, out List<SectionRange> ranges))
                return false;

            for (int r = 0; r < ranges.Count; r++)
            {
                SectionRange range = ranges[r];
                for (int i = range.Start + 1; i < range.End; i++)
                {
                    Match match = _matches[i];
                    if (!match.Groups[_iniEntry].Success)
                        continue;

                    Group keyGroup = match.Groups[_iniKey];
                    if (!SubstringEquals(_content, keyGroup.Index, keyGroup.Length, key, _comparison))
                        continue;

                    // Found key.
					value = match.Groups[_iniValue].Value;
                    found = true;

                    if (!_allowOverrides)
                        return true;
                }
            }

            return found;
        }

        // Method to get a value from a specific section and key, with an optional default value.
        private string GetValue(string section, string key, string defaultValue = null)
        {
            return TryGetValue(section, key, out string value) ? value : defaultValue;
        }

        // Tries to get all values associated with the specified section and key.
        // Returns true if at least one value is found. Preserves file order.
        private bool TryGetValues(string section, string key, out string[] values)
        {
            values = null;
            List<string> list = null;

			// Global entries: scan the region before the first named section.
            if (string.IsNullOrEmpty(section))
            {
                for (int i = 0; i < _firstSectionIndex; i++)
                {
                    Match match = _matches[i];
                    if (!match.Groups[_iniEntry].Success)
                        continue;

                    Group keyGroup = match.Groups[_iniKey];
                    if (!SubstringEquals(_content, keyGroup.Index, keyGroup.Length, key, _comparison))
                        continue;

                    // Found next key.
					if (list == null)
                        list = new List<string>(DefaultCapacity);

                    list.Add(match.Groups[_iniValue].Value);
                }
            }
			
			// Named section: use the precomputed ranges, one entry loop per occurrence.
            else if (TryGetSectionRanges(section, out List<SectionRange> ranges))
            {
                for (int r = 0; r < ranges.Count; r++)
                {
                    SectionRange range = ranges[r];
                    for (int i = range.Start + 1; i < range.End; i++)
                    {
                        Match match = _matches[i];
                        if (!match.Groups[_iniEntry].Success)
                            continue;

                        Group keyGroup = match.Groups[_iniKey];
                        if (!SubstringEquals(_content, keyGroup.Index, keyGroup.Length, key, _comparison))
                            continue;

						// Found next key.
                        if (list == null)
                            list = new List<string>(DefaultCapacity);

                        list.Add(match.Groups[_iniValue].Value);
                    }
                }
            }

            if (list == null)
                return false;

            values = list.ToArray();
            return true;
        }

        // Method to get all values in a specific section.
        private IEnumerable<string> GetValues(string section)
        {
            List<string> values = new List<string>(DefaultCapacity);

            if (string.IsNullOrEmpty(section))
            {
                for (int i = 0; i < _firstSectionIndex; i++)
                {
                    Match match = _matches[i];
                    if (!match.Groups[_iniEntry].Success)
                        continue;

                    values.Add(match.Groups[_iniValue].Value);
                }
                return values;
            }

            if (!TryGetSectionRanges(section, out List<SectionRange> ranges))
                return values;

            for (int r = 0; r < ranges.Count; r++)
            {
                SectionRange range = ranges[r];
                for (int i = range.Start + 1; i < range.End; i++)
                {
                    Match match = _matches[i];
                    if (!match.Groups[_iniEntry].Success)
                        continue;

                    values.Add(match.Groups[_iniValue].Value);
                }
            }

            return values;
        }

        // Method to get all values associated with a specific key in a section.
        private IEnumerable<string> GetValues(string section, string key)
        {
            // If the key is empty, return all the values in the section.
            if (string.IsNullOrEmpty(key))
                return GetValues(section);

            List<string> values = new List<string>(DefaultCapacity);

            if (string.IsNullOrEmpty(section))
            {
                for (int i = 0; i < _firstSectionIndex; i++)
                {
                    Match match = _matches[i];
                    if (!match.Groups[_iniEntry].Success)
                        continue;

                    Group keyGroup = match.Groups[_iniKey];
                    if (!SubstringEquals(_content, keyGroup.Index, keyGroup.Length, key, _comparison))
                        continue;

                    values.Add(match.Groups[_iniValue].Value);
                }
                return values;
            }

            if (!TryGetSectionRanges(section, out List<SectionRange> ranges))
                return values;

            for (int r = 0; r < ranges.Count; r++)
            {
                SectionRange range = ranges[r];
                for (int i = range.Start + 1; i < range.End; i++)
                {
                    Match match = _matches[i];
                    if (!match.Groups[_iniEntry].Success)
                        continue;

                    Group keyGroup = match.Groups[_iniKey];
                    if (!SubstringEquals(_content, keyGroup.Index, keyGroup.Length, key, _comparison))
                        continue;

                    values.Add(match.Groups[_iniValue].Value);
                }
            }

            return values;
        }

        #endregion

        #region Internal data modification methods

        // Sets a single value for a specified key in a given section.
        private void SetValue(string section, string key, string value = null)
        {
            bool emptySection = string.IsNullOrEmpty(section);
            bool expectedValue = !string.IsNullOrEmpty(value); // Indicates that a value should be inserted or updated.
            bool inSection = emptySection;
            Match lastMatch = null; // Keep track of the last match for future reference.
            StringBuilder sb = new StringBuilder(_content);

            /*if (_allowEscapeChars && escape && expectedValue)
                value = ToEscape(value);
            else
            {
                string lineBreaker = _allowMultiLine ? _lineBreaker : " ";
                value = NormalizeLineBreaker(value, lineBreaker);
                if (_allowMultiLine && wrap) value = ToQuote(value);
            }*/

            // Iterate over the content to find the section and key, and set the value.
            for (int i = 0; i < _matches.Count; i++)
            {
                Match match = _matches[i];

                if (match.Groups[_iniSection].Success)
                {
                    Group group = match.Groups[_iniValue];
                    inSection = SubstringEquals(_content, group.Index, group.Length, section, _comparison);

                    if (emptySection) break;
                    continue;
                }

                // If inside the correct section and the match is an entry.
                if (inSection && match.Groups[_iniEntry].Success)
                {
                    lastMatch = match;

                    // Continue if the key doesn't match.
                    Group keyGroup = match.Groups[_iniKey];
                    if (!SubstringEquals(_content, keyGroup.Index, keyGroup.Length, key, _comparison))
                        continue;

                    Group valueGroup = match.Groups[_iniValue];

                    int index = valueGroup.Index;
                    int length = valueGroup.Length;

                    if (expectedValue)
                    {
                        // Remove the old value and insert the new value in its place.
                        sb.Remove(index, length);
                        sb.Insert(index, value);
                    }
                    else
                    {
                        // Remove the entire entry.
                        sb.Remove(match.Index, match.Length);
                    }

                    // The operation has been completed.
                    expectedValue = false;
                    break;
                }
            }

            // If the key doesn't exist, append the new value at the correct position.
            if (expectedValue)
            {
                int index = 0;

                // If a match was found previously, append after the last match.
                if (lastMatch != null)
                {
                    index = lastMatch.Index + lastMatch.Length;
                }

                // If no match was found, append a new section and then insert the key-value pair.
                else if (!emptySection)
                {
                    // Add the section header.
                    sb.Append(_lineBreaker);
                    sb.Append($"[{section}]{_lineBreaker}");
                    index = sb.Length;
                }

                // Insert the new key-value pair into the content.
                string line = $"{key}={value}";
                InsertLine(sb, ref index, _lineBreaker, line);
            }

            Content = sb.ToString();
        }

        // Sets multiple values for a specific key in a section.
        private void SetValues(string section, string key, bool wrap = true, params string[] values)
        {
            if (values == null) values = new string[0];

            int valueIndex = 0;  // Track the index of the current value being processed.
            bool emptySection = string.IsNullOrEmpty(section);
            bool inSection = emptySection;
            Match lastMatch = null;      // Keep track of the last entry (any key) in the section.
            Match lastSection = null;    // Keep track of the last occurrence of the section.
            StringBuilder sb = new StringBuilder(_content);  // Create a StringBuilder to modify the ini content.
            int offset = 0; // Offset to account for changes in length during replacements.

            // List to store all matches of the target key within the target section.
            List<Match> keyMatches = new List<Match>(DefaultCapacity);

            // Iterate over the ini content and process each match for section and entry.
            for (int i = 0; i < _matches.Count; i++)
            {
                Match match = _matches[i];

                if (match.Groups[_iniSection].Success)  // Check if the current match is a section.
                {
                    // Set the inSection flag based on whether the section matches the target section.
                    Group sectionGroup = match.Groups[_iniValue];
                    bool sectionMatch = SubstringEquals(_content, sectionGroup.Index, sectionGroup.Length, section, _comparison);

                    if (sectionMatch)
                        lastSection = match;  // Remember only the matching section header.

                    inSection = sectionMatch;
                    if (emptySection) break;  // If there is no section, break out of the loop.
                    continue;
                }

                // Check if inside the correct section and the current match is an entry.
                if (inSection && match.Groups[_iniEntry].Success)
                {
                    lastMatch = match;  // Remember the last entry in the section.

                    // Check if the key matches.
                    Group keyGroup = match.Groups[_iniKey];
                    if (SubstringEquals(_content, keyGroup.Index, keyGroup.Length, key, _comparison))
                    {
                        keyMatches.Add(match);

                        // If there are still values left, replace the current entry.
                        if (valueIndex < values.Length)
                        {
                            // Get the group representing the value.
                            Group valueGroup = match.Groups[_iniValue];

                            // Get the new value to insert.
                            string newValue = values[valueIndex++] ?? string.Empty;
                            string oldValue = valueGroup.Value;

                            // Calculate the index considering previous modifications.
                            int index = valueGroup.Index + offset;
                            int length = valueGroup.Length;

                            // Remove the old value and insert the new one.
                            sb.Remove(index, length);

                            /*if (_allowEscapeChars)
                                newValue = ToEscape(newValue);
                            else
                            {
                                string lineBreaker = _allowMultiLine ? _lineBreaker : " ";
                                newValue = NormalizeLineBreaker(newValue, lineBreaker);

                                if (_allowMultiLine && wrap)
                                    newValue = ToQuote(newValue);
                            }*/

                            sb.Insert(index, newValue);

                            // Update the offset for future replacements.
                            offset += newValue.Length - oldValue.Length;
                        }
                        // else: this is an extra occurrence of the key that will be removed later.
                    }
                }
            }

            // Determine the number of existing entries for the key.
            int existingCount = keyMatches.Count;

            // If there are more existing entries than provided values, remove the excess.
            if (existingCount > values.Length)
            {
                // Remove extra entries from the end to preserve order.
                for (int j = existingCount - 1; j >= values.Length; j--)
                {
                    Match match = keyMatches[j];
                    int index = match.Index + offset;
                    int length = match.Length;

                    sb.Remove(index, length);
                    offset -= length; // Adjust offset for the removal.
                }
            }
            // If there are fewer existing entries, add the remaining values.
            else if (existingCount < values.Length)
            {
                int insertIndex;

                // If a last entry was found, insert after it.
                if (lastMatch != null)
                {
                    insertIndex = lastMatch.Index + lastMatch.Length + offset;
                }
                // If no entry but a matching section header exists, insert right after the header.
                else if (lastSection != null)
                {
                    insertIndex = lastSection.Index + lastSection.Length + offset;
                }
                // If the section doesn't exist, create a new section header.
                else if (!emptySection)
                {
                    sb.Append(_lineBreaker);
                    sb.Append($"[{section}]{_lineBreaker}");
                    insertIndex = sb.Length;
                }
                else
                {
                    // For global section (empty section name), insert at the end of the file.
                    insertIndex = sb.Length;
                }

                // Insert the remaining values as new entries in the section.
                while (valueIndex < values.Length)
                {
                    string value = values[valueIndex++];

                    /*if (_allowEscapeChars)
                        value = ToEscape(value);
                    else
                    {
                        string lineBreaker = _allowMultiLine ? _lineBreaker : " ";
                        value = NormalizeLineBreaker(value, lineBreaker);

                        if (_allowMultiLine && wrap)
                            value = ToQuote(value);
                    }*/

                    // Insert the new key-value pair into the content.
                    string line = $"{key}={value}";
                    InsertLine(sb, ref insertIndex, _lineBreaker, line);
                }
            }

            // Update the content with the modified StringBuilder content.
            Content = sb.ToString();
        }

        #endregion

        #region Internal JSON parsing and serialization methods

        // Skips comments, whitespace, and newlines, advancing the index.
        private void SkipWhitespaceAndComments(MatchCollection matches, ref int index)
        {
            while (index < matches.Count)
            {
                Match m = matches[index];
                if (!m.Groups[_jsonComment].Success
                    && !m.Groups[_jsonWhitespace].Success
                    && !m.Groups[_jsonNewline].Success)
                    break;
                index++;
            }
        }

        // Parses the string containing JSON data.
        private object ParseJson(string json)
        {
            var matches = _jsonRegex.Matches(json);
            int index = 0;

            // Begin parsing JSON.
            try
            {
                if (!ParseValue(matches, ref index, 0, out object result))
                    return null;

                // Skip any remaining whitespace, comments, newlines
                SkipWhitespaceAndComments(matches, ref index);
                /*if (index >= matches.Count) return false;
                Match m = matches[index];*/


                return result;
            }

            // If empty string or syntax errors.
            catch
            {
                return null;
            }
        }

        // Skips a nested JSON structure (object or array) without parsing.
        private void SkipStructure(MatchCollection matches, ref int index)
        {
            if (index >= matches.Count)
                return;

            bool openedStructure = false;

            for (int nesting = 0; index < matches.Count; index++)
            {
                Match m = matches[index];

                // Skip comments, whitespace, newlines.
                if(m.Groups[_jsonComment].Success
                    || m.Groups[_jsonWhitespace].Success
                    || m.Groups[_jsonNewline].Success)
                    continue;

                if (m.Groups[_jsonObjectOpen].Success || m.Groups[_jsonArrayOpen].Success)
                {
                    nesting++;
                    openedStructure = true;
                }
                else if (m.Groups[_jsonObjectClose].Success || m.Groups[_jsonArrayClose].Success)
                {
                    nesting--;
                    if (openedStructure && nesting == 0)
                    {
                        index++; // Consume closing token.
                        break;
                    }
                }

                // For any other token (strings, numbers, etc.) just skip
            }
        }

        // Advances the token index past a complete JSON value: a nested object,
        // a nested array, or a primitive token.
        private void SkipJsonValue(MatchCollection matches, ref int index)
        {
            if (index >= matches.Count)
                return;

            Match m = matches[index];

            if (m.Groups[_jsonObjectOpen].Success || m.Groups[_jsonArrayOpen].Success)
            {
                SkipStructure(matches, ref index);
                return;
            }

            // Primitive or unexpected token — advance by one.
            index++;
        }

        // Returns the (start, length) span of the JSON value at the current token
        // index in the original string, and advances the index past the value.
        // Handles nested objects/arrays via SkipStructure.
        private bool GetJsonValueSpan(MatchCollection matches, ref int index, out int start, out int length)
        {
            start = 0;
            length = 0;

            if (index >= matches.Count)
                return false;

            Match m = matches[index];

            if (m.Groups[_jsonObjectOpen].Success || m.Groups[_jsonArrayOpen].Success)
            {
                int begin = m.Index;
                SkipStructure(matches, ref index);
                if (index <= 0)
                    return false;

                Match last = matches[index - 1];
                start = begin;
                length = last.Index + last.Length - begin;
                return true;
            }

            if (m.Groups[_jsonValue].Success)
            {
                start = m.Index;
                length = m.Length;
                index++;
                return true;
            }

            return false;
        }

        // Positions the token index at the value whose key matches targetKey
        // inside the current JSON object. Returns false if the key is not found.
        private bool DescendJsonObject(
            MatchCollection matches,
            ref int index,
            string targetKey)
        {
            bool first = true;

            while (true)
            {
                SkipWhitespaceAndComments(matches, ref index);
                if (index >= matches.Count)
                    return false;

                Match m = matches[index];

                if (m.Groups[_jsonObjectClose].Success)
                    return false;

                if (!first)
                {
                    if (!m.Groups[_jsonArraySep].Success)
                        return false;

                    index++;
                    SkipWhitespaceAndComments(matches, ref index);
                    if (index >= matches.Count)
                        return false;

                    m = matches[index];

                    // Trailing comma before closing brace.
                    if (m.Groups[_jsonObjectClose].Success)
                        return false;
                }
                first = false;

                if (!m.Groups[_jsonKey].Success)
                    return false;

                // Strip surrounding quotes and unescape the key.
                string rawKey = m.Groups[_jsonKey].Value;
                string keyName = rawKey.Length >= 2
                    ? UnEscape(rawKey.Substring(1, rawKey.Length - 2))
                    : rawKey;

                index++;

                SkipWhitespaceAndComments(matches, ref index);
                if (index >= matches.Count)
                    return false;

                m = matches[index];
                if (!m.Groups[_jsonValueSep].Success)
                    return false;

                index++;

                SkipWhitespaceAndComments(matches, ref index);
                if (index >= matches.Count)
                    return false;

                if (string.Equals(keyName, targetKey, _comparison))
                    return true;

                SkipJsonValue(matches, ref index);
            }
        }

        // Positions the token index at the element with the given index inside
        // the current JSON array. Supports decimal, hex, octal, and binary indices.
        private bool DescendJsonArray(
            MatchCollection matches,
            ref int index,
            int targetIndex)
        {
            int current = 0;
            bool first = true;

            while (true)
            {
                SkipWhitespaceAndComments(matches, ref index);
                if (index >= matches.Count)
                    return false;

                Match m = matches[index];

                if (m.Groups[_jsonArrayClose].Success)
                    return false;

                if (!first)
                {
                    if (!m.Groups[_jsonArraySep].Success)
                        return false;

                    index++;
                    SkipWhitespaceAndComments(matches, ref index);
                    if (index >= matches.Count)
                        return false;

                    m = matches[index];

                    // Trailing comma before closing bracket.
                    if (m.Groups[_jsonArrayClose].Success)
                        return false;
                }
                first = false;

                if (current == targetIndex)
                    return true;

                SkipJsonValue(matches, ref index);
                current++;
            }
        }



        // Parses the JSON value.
        private bool ParseValue(MatchCollection matches, ref int index, int depth, out object result)
        {
            result = null;

            if (index >= matches.Count)
                return false;

            // Skip comments and whitespace.
            SkipWhitespaceAndComments(matches, ref index);
            if (index >= matches.Count) return false;
            Match m = matches[index];

            // Parse difference type of values.

            // Object { ... }
            if (m.Groups[_jsonObjectOpen].Success)
            {
                // Check depth limit before entering
                if (depth + 1 >= MaxNestingDepth)
                {
                    SkipStructure(matches, ref index); // index currently at '{'
                    result = null;
                    return true; // truncated to null
                }

                index++;
                if (ParseObject(matches, ref index, depth + 1, out IDictionary<string, object> dict))
                {
                    result = dict;
                    return true;
                }
                return false;
            }

            // Array [ ... ]
            else if (m.Groups[_jsonArrayOpen].Success)
            {
                if (depth + 1 >= MaxNestingDepth)
                {
                    SkipStructure(matches, ref index);
                    result = null;
                    return true; // truncated to null
                }

                index++;
                if (ParseArray(matches, ref index, depth + 1, out object[] arr))
                {
                    result = arr;
                    return true;
                }
                return false;
            }

            // Primitive value.
            else if (m.Groups[_jsonValue].Success)
            {
                if (!ParsePrimitive(m, out result))
                    return false;
                index++;
                return true;
            }

            // Unexpected token.
            else
            {
                return false;
            }
        }

        // Parses an object (Dictionary) from JSON.
        private bool ParseObject(MatchCollection matches, ref int index, int depth, out IDictionary<string, object> result)
        {
            result = null;

            var dict = new Dictionary<string, object>(DefaultCapacity, GetComparer(_comparison));
            bool first = true;

            while (index < matches.Count)
            {

                // Skip whitespace/comments.
                SkipWhitespaceAndComments(matches, ref index);
                if (index >= matches.Count) return false;
                Match m = matches[index];

                // End of the object.
                if (m.Groups[_jsonObjectClose].Success)
                {
                    index++;
                    result = dict;
                    return true;
                }

                if (first)
                {
                    // Expect key.
                    if (!m.Groups[_jsonKey].Success)
                        return false;
                    first = false;
                }
                else
                {
                    // Expect comma or close.
                    if (m.Groups[_jsonArraySep].Success)
                    {
                        index++;
                        // Skip whitespace.
                        SkipWhitespaceAndComments(matches, ref index);
                        if (index >= matches.Count) return false;
                        m = matches[index];

                        // Trailing comma, skip to close...
                        if (m.Groups[_jsonObjectClose].Success)
                        {
                            index++;
                            result = dict;
                            return true;
                        }

                        // ...else expect key.
                        if (!m.Groups[_jsonKey].Success)
                            return false;
                    }

                    // End of the object.
                    else if (m.Groups[_jsonObjectClose].Success)
                    {
                        index++;
                        result = dict;
                        return true;
                    }
                    else
                    {
                        return false; // Unexpected token.
                    }
                }

                // Parse key.
                string key = UnEscape(m.Groups[_jsonKey].Value.Substring(1, m.Groups[_jsonKey].Value.Length - 2));
                index++;

                // Skip whitespace.
                SkipWhitespaceAndComments(matches, ref index);
                if (index >= matches.Count) return false;
                m = matches[index];

                // Expect delimiter.
                if (!m.Groups[_jsonValueSep].Success)
                    return false;
                index++;

                // Parse value (value is at the same depth, no increase)
                if (!ParseValue(matches, ref index, depth, out object val))
                    return false;

                dict[key] = val;
            }
            return false;
        }

        // Parses an array from JSON.
        private bool ParseArray(MatchCollection matches, ref int index, int depth, out object[] result)
        {
            result = null;

            List<object> list = new List<object>(DefaultCapacity);

            // Indicates whether the next element is the first one in the array.
            // The first element is not expected to be preceded by a comma.
            bool first = true;

            while (index < matches.Count)
            {
                // Skip whitespace/comments
                SkipWhitespaceAndComments(matches, ref index);
                if (index >= matches.Count) return false;
                Match m = matches[index];

                // End of array.
                if (m.Groups[_jsonArrayClose].Success)
                {
                    index++;
                    result = list.ToArray();
                    return true;
                }

                // The first element can appear immediately after '['.
                if (first)
                {
                    first = false;
                }
                else
                {
                    // Expect comma or close.
                    if (m.Groups[_jsonArraySep].Success)
                    {
                        // Skip whitespace.
                        index++;
                        SkipWhitespaceAndComments(matches, ref index);
                        if (index >= matches.Count) return false;
                        m = matches[index];

                        // Trailing comma, skip to close.
                        if (m.Groups[_jsonArrayClose].Success)
                        {
                            index++;
                            result = list.ToArray();
                            return true;
                        }

                        // ...else parse value
                    }
                    else if (m.Groups[_jsonArrayClose].Success)
                    {
                        index++;
                        result = list.ToArray();
                        return true;
                    }
                    else
                    {
                        return false; // Unexpected token.
                    }
                }

                // Parse value (value is at the same depth, no increase)
                if (!ParseValue(matches, ref index, depth, out object val))
                    return false;

                list.Add(val);
            }
            return false;
        }

        // Parses primitive values from JSON.
        private bool ParsePrimitive(Match match, out object result)
        {
            result = null;

            // Null.
            if (match.Groups[_jsonNull].Success)
            {
                result = null;
                return true;
            }

            // Boolean.
            if (match.Groups[_jsonBool].Success)
            {
                if (bool.TryParse(match.Groups[_jsonBool].Value, out bool value))
                {
                    result = value;
                    return true;
                }
                return false;
            }

            // String.
            if (match.Groups[_jsonString].Success)
            {
                string value = match.Groups[_jsonString].Value;
                value = UnEscape(value);
                result = value;
                return true;
            }

            // Number.
            if (match.Groups[_jsonNumber].Success)
            {
                if (double.TryParse(
                    match.Groups[_jsonNumber].Value,
                    NumberStyles.Float,
                    _culture,
                    out double value))
                {
                    result = value;
                    return true;
                }
                return false;
            }

            return false; // Unknown token.
        }

        // Serializes an object to a JSON string.
        // Supports IDictionary<string, object>, IEnumerable (non-string), and primitives.
        private string SerializeJson(object value, bool beautify = false)
        {
            var sb = new StringBuilder();
            SerializeValue(value, sb, beautify, 0);
            return sb.ToString();
        }

        // Serializes a regular value to JSON format.
        private void SerializeValue(object value, StringBuilder sb, bool beautify, int depth)
        {
            // Depth limit check
            if (depth >= MaxNestingDepth)
            {
                sb.Append("null");
                return;
            }

            // Null.
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            Type type = value.GetType();

            // String.
            if (type == typeof(string))
            {
                string str = (string)value;
                str = ToEscape(str);
                sb.Append('"').Append(str).Append('"');
                return;
            }

            // Boolean.
            if (type == typeof(bool))
            {
                sb.Append((bool)value ? "true" : "false");
                return;
            }

            // Numeric types.
            if (type == typeof(int) || type == typeof(long) || type == typeof(short) ||
                type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) ||
                type == typeof(double) || type == typeof(float) || type == typeof(decimal))
            {
                string s = Convert.ToString(value, _culture);
                sb.Append(s);
                return;
            }

            // Object (IDictionary<string, object>).
            if (value is IDictionary<string, object> dict)
            {
                SerializeObject(dict, sb, beautify, depth + 1);
                return;
            }

            // Array or enumerable (except string).
            if (value is IEnumerable enumerable && !(value is string))
            {
                SerializeArray(enumerable, sb, beautify, depth + 1);
                return;
            }

            // Fallback: ToString() with escaping.
            string text = Convert.ToString(value, _culture);
            text = ToEscape(text);
            sb.Append('"').Append(text).Append('"');
        }

        // Serializes a dictionary (object) to JSON format.
        private void SerializeObject(IDictionary<string, object> dict, StringBuilder sb, bool beautify, int depth)
        {
            if (depth >= MaxNestingDepth)
            {
                sb.Append("null");
                return;
            }

            // Open object.
            sb.Append('{');

            // The first property is written without a leading comma.
            // All subsequent properties are prefixed with a comma.
            bool first = true;

            foreach (var kvp in dict)
            {
                // Separate properties with commas.
                if (!first)
                    sb.Append(',');

                // Append indents.
                if (beautify)
                {
                    // Start each element on a new indented line.
                    sb.Append('\n').Append(' ', (depth) * 2);
                }
                else if (!first)
                {
                    sb.Append(' ');
                }

                // All remaining properties are no longer the first.
                first = false;

                // Append a key.
                string key = kvp.Key;
                key = ToEscape(key);
                sb.Append('"').Append(key).Append('"').Append(':');

                // Append a value.
                SerializeValue(kvp.Value, sb, beautify, depth + 1);
            }

            // Append indents.
            if (beautify && dict.Count > 0)
                sb.Append('\n').Append(' ', (depth - 1) * 2);

            // Close object.
            sb.Append('}');
        }

        // Serializes an enumerable (array) to JSON format.
        private void SerializeArray(IEnumerable enumerable, StringBuilder sb, bool beautify, int depth)
        {
            if (depth >= MaxNestingDepth)
            {
                sb.Append("null");
                return;
            }

            // Open array.
            sb.Append('[');

            // Indicates whether the next element is the first one.
            // Used to suppress the leading comma and control spacing.
            bool first = true;

            foreach (object item in enumerable)
            {
                // Separate array elements with commas.
                if (!first)
                    sb.Append(',');

                // Append indents.
                if (beautify)
                {
                    sb.Append('\n').Append(' ', depth * 2);
                }
                else if (!first)
                {
                    sb.Append(' ');
                }

                // All subsequent elements require a separator.
                first = false;

                // Append a value.
                SerializeValue(item, sb, beautify, depth + 1);
            }

            // Align the closing bracket with the opening one.
            if (beautify && !first)
                sb.Append('\n').Append(' ', (depth - 1) * 2);

            // Close array.
            sb.Append(']');
        }

        // Navigates a JSON structure along the given path segments and retrieves
        // the value at the end. Returns false if any segment cannot be resolved
        // (missing key, index out of range, or a primitive encountered mid-path).
        private bool TryNavigateJsonPath(object root, string[] segments, out object value)
        {
            value = null;
            if (root == null || segments == null)
                return false;

            object current = root;
            for (int i = 0; i < segments.Length; i++)
            {
                if (current == null)
                    return false;

                // JSON object → index by key.
                if (current is IDictionary<string, object> dict)
                {
                    if (!dict.TryGetValue(segments[i], out object next))
                        return false;
                    current = next;
                    continue;
                }

                // JSON array → index by integer (supports decimal, hex, octal, binary).
                if (current is object[] array)
                {
                    object parsed = ParseNumber(segments[i], typeof(int), _culture);
                    if (parsed == null)
                        return false;

                    int index = (int)parsed;
                    if (index < 0 || index >= array.Length)
                        return false;

                    current = array[index];
                    continue;
                }

                // Cannot descend into a primitive value.
                return false;
            }

            value = current;
            return true;
        }

        // Sets a value at the given path inside a JSON structure, creating intermediate
        // dictionaries as needed. Returns false on failure (empty path, primitive
        // encountered mid-path, missing array, or index out of range).
        private bool TrySetJsonPathValue(object root, string[] segments, object value)
        {
            if (root == null || segments == null || segments.Length == 0)
                return false;

            object current = root;
            int lastIndex = segments.Length - 1;

            // Walk to the parent container, creating intermediate dictionaries as needed.
            for (int i = 0; i < lastIndex; i++)
            {
                string segment = segments[i];
				
				// Current node is dictionary.
                if (current is IDictionary<string, object> dict)
                {
                    if (!dict.TryGetValue(segment, out object next) || next == null)
                    {
                        next = new Dictionary<string, object>(DefaultCapacity, GetComparer(_comparison));
                        dict[segment] = next;
                    }
                    current = next;
                    continue;
                }
				
				// Current node is an array.
                if (current is object[] array)
                {
                    object parsed = ParseNumber(segment, typeof(int), _culture);
                    if (parsed == null)
                        return false;

                    int index = (int)parsed;
                    if (index < 0 || index >= array.Length)
                        return false;

                    object next = array[index];
                    if (next == null)
                    {
                        next = new Dictionary<string, object>(DefaultCapacity, GetComparer(_comparison));
                        array[index] = next;
                    }
                    current = next;
                    continue;
                }

                return false;
            }

            // Set the value in the resulting container.
            string last = segments[lastIndex];

            if (current is IDictionary<string, object> targetDict)
            {
                targetDict[last] = value;
                return true;
            }

            // The index must parse and be in range; arrays are not extended.
            if (current is object[] targetArray)
            {
                object parsed = ParseNumber(last, typeof(int), _culture);
                if (parsed == null)
                    return false;

                int index = (int)parsed;
                if (index < 0 || index >= targetArray.Length)
                    return false;

                targetArray[index] = value;
                return true;
            }

            return false;
        }

        // Navigates the JSON token stream along the given path segments and
        // returns the raw span of the value at the end.
        private bool TryNavigateJsonPathRaw(
            MatchCollection matches,
            string[] segments,
            out int start,
            out int length)
        {
            start = 0;
            length = 0;

            // Path is empty.
            if (segments == null || segments.Length == 0)
                return false;

            int index = 0;
            SkipWhitespaceAndComments(matches, ref index);
            if (index >= matches.Count)
                return false;

            // Walk to the parent container.
            for (int s = 0; s < segments.Length; s++)
            {
                if (index >= matches.Count)
                    return false;

                Match m = matches[index];
                string segment = segments[s];

                if (m.Groups[_jsonObjectOpen].Success)
                {
                    index++;
                    if (!DescendJsonObject(matches, ref index, segment))
                        return false;
                }
                else if (m.Groups[_jsonArrayOpen].Success)
                {
                    object parsed = ParseNumber(segment, typeof(int), _culture);
                    if (parsed == null)
                        return false;

                    int target = (int)parsed;
                    if (target < 0)
                        return false;

                    index++;
                    if (!DescendJsonArray(matches, ref index, target))
                        return false;
                }
                else
                {
                    // A primitive with more segments to consume — cannot descend.
                    return false;
                }
            }

            return GetJsonValueSpan(matches, ref index, out start, out length);
        }

        #endregion

        #region Internal utility and helper methods

        // Builds a compact string signature for the settings that affect the
        // compiled patterns. Order and count must be kept in sync with the fields
        // of IniSettings used by BuildIniPatternEx / BuildJsonPattern and by
        // GetRegexOptions.
        private static string MakeRegexBundleKey(IniSettings s)
        {
            return string.Concat(
                ((int)s.Comparison).ToString(),
                s.AllowEscapeChars ? "1" : "0",
                s.AllowMultiLine ? "1" : "0",
                s.AllowQuotedValues ? "1" : "0",
                s.AllowSpacesInKey ? "1" : "0",
                s.AllowInlineComments ? "1" : "0",
                s.DuplicateKeyOverride ? "1" : "0",
                "|",
                ((int)s.Delimiters).ToString(),
                ((int)s.Comments).ToString(),
                ((int)s.UndefinedText).ToString());
        }

        // Returns a cached bundle for the given settings, constructing it on the
        // first call for each unique settings signature. The signature covers every
        // IniSettings property that affects the compiled pattern or the
        // RegexOptions, so a bundle is never reused across incompatible settings.
        private static RegexBundle GetOrCreateRegexBundle(IniSettings settings)
        {
            string key = MakeRegexBundleKey(settings);

            lock (_regexBundles)
            {
                if (_regexBundles.TryGetValue(key, out RegexBundle cached))
                    return cached;

                var options = GetRegexOptions(
                    settings.Comparison,
                    RegexOptions.Compiled | RegexOptions.ExplicitCapture);

                var bundle = new RegexBundle(
                    new Regex(settings.BuildIniPatternEx(), options),
                    new Regex(settings.BuildJsonPattern(), options));

                // Bounded cache; clear when full rather than LRU, since the number
                // of distinct settings signatures is tiny in practice.
                if (_regexBundles.Count >= MaxRegexBundles)
                    _regexBundles.Clear();

                _regexBundles[key] = bundle;
                return bundle;
            }
        }


        // Converts a dictionary representation of an object into a SafeExpandoObject.
        private static SafeExpandoObject ConvertToExpando(IDictionary<string, object> dict)
        {
            var expando = new SafeExpandoObject();
            var expandoDict = (IDictionary<string, object>)expando;

            foreach (var kvp in dict)
            {
                // Convert nested objects recursively.
                if (kvp.Value is IDictionary<string, object> nestedDict)
                    expandoDict[kvp.Key] = ConvertToExpando(nestedDict);

                // Convert arrays that may contain nested dictionaries or arrays.
                else if (kvp.Value is object[] array)
                    expandoDict[kvp.Key] = ConvertArray(array);

                // Copy primitive values and other objects as-is.
                else
                    expandoDict[kvp.Key] = kvp.Value;
            }

            return expando;
        }

        // Recursively converts nested objects and arrays inside an object array.
        private static object[] ConvertArray(object[] array)
        {
            for (int i = 0; i < array.Length; i++)
            {
                // Convert nested objects inside the array.
                if (array[i] is IDictionary<string, object> dict)
                    array[i] = ConvertToExpando(dict);

                // Convert nested arrays recursively.
                else if (array[i] is object[] nestedArray)
                    array[i] = ConvertArray(nestedArray);
            }

            return array;
        }

        // Helper: convert dynamic (which may be ExpandoObject) to a plain object (Dictionary, array, primitive).
        private static object ConvertFromDynamic(dynamic value)
        {
            if (value == null) return null;
            Type type = value.GetType();

            // If it's already an ExpandoObject, convert to Dictionary<string, object>.
            if (value is IDictionary<string, object> dict)
            {
                var result = new Dictionary<string, object>(DefaultCapacity);
                foreach (var kv in dict)
                    result[kv.Key] = ConvertFromDynamic(kv.Value);
                return result;
            }

            // If it's an array (object[]), convert each element.
            if (type.IsArray)
            {
                var arr = (object[])value;
                var newArr = new object[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                    newArr[i] = ConvertFromDynamic(arr[i]);
                return newArr;
            }

            // If it's a generic IEnumerable (like List<>), convert to array.
            if (value is IEnumerable enumerable && !(value is string))
            {
                var list = new List<object>(DefaultCapacity);
                foreach (var item in enumerable)
                    list.Add(ConvertFromDynamic(item));
                return list.ToArray();
            }

            // Primitive or other - return as is.
            return value;
        }

        // Returns a CultureInfo object that defines the string comparison rules for the specified StringComparison.
        private static CultureInfo GetCultureInfo(StringComparison comparison)
        {
            return comparison < StringComparison.InvariantCulture
                ? CultureInfo.CurrentCulture
                : CultureInfo.InvariantCulture;
        }

        /// <summary>
        /// Determines the default delimiter to use when writing new entries.
        /// </summary>
        private static string GetDelimiter(IniDelimiterMode delimiterMode)
        {
            // Resolve Default to Both
            if (delimiterMode == IniDelimiterMode.Default)
                delimiterMode = IniDelimiterMode.Both;

            // If only Colon is allowed, use ':'; otherwise use '=' (including Both)
            if (delimiterMode == IniDelimiterMode.Colon)
                return ":";
            return "=";
        }

        // Sets or clears the RegexOptions flags based on the specified StringComparison, returning the modified value.
        private static RegexOptions GetRegexOptions(StringComparison comparison, RegexOptions options = RegexOptions.Compiled | RegexOptions.ExplicitCapture)
        {
            // Bit 0 indicates IgnoreCase.
            if (((int)comparison & 1) != 0)
                options |= RegexOptions.IgnoreCase;
            else
                options &= ~RegexOptions.IgnoreCase;

            // Higher bits indicate the comparison type.
            switch (((int)comparison) >> 1)
            {
                case 0: // CurrentCulture
                    options &= ~RegexOptions.CultureInvariant;
                    break;

                case 1: // InvariantCulture
                    options |= RegexOptions.CultureInvariant;
                    break;

                case 2: // Ordinal
                    options &= ~RegexOptions.CultureInvariant;
                    break;
            }

            return options;
        }

        // Checks ignore case flag in the specified StringComparison.
        private static bool IsIgnoreCase(StringComparison comparison)
        {
            return ((int)comparison & 1) != 0;
        }

        // Returns the StringComparer based on the specified StringComparison.
        private static StringComparer GetComparer(StringComparison comparison)
        {
            switch (comparison)
            {
                case StringComparison.CurrentCulture:
                    return StringComparer.CurrentCulture;
                case StringComparison.CurrentCultureIgnoreCase:
                    return StringComparer.CurrentCultureIgnoreCase;
                case StringComparison.InvariantCulture:
                    return StringComparer.InvariantCulture;
                case StringComparison.InvariantCultureIgnoreCase:
                    return StringComparer.InvariantCultureIgnoreCase;
                case StringComparison.Ordinal:
                    return StringComparer.Ordinal;
                case StringComparison.OrdinalIgnoreCase:
                    return StringComparer.OrdinalIgnoreCase;
                default:
                    return StringComparer.InvariantCultureIgnoreCase;
            }
        }

        // Converts an enum value to its string representation.
        // For flags enums, returns a comma-separated list of names.
        private static string EnumToString(object enumValue)
        {
            if (enumValue == null) return null;

            Type enumType = enumValue.GetType();
            if (!enumType.IsEnum)
                return enumValue.ToString();

            // Get the underlying integral value.
            long longValue = Convert.ToInt64(enumValue);
            bool isFlags = enumType.GetCustomAttribute<FlagsAttribute>() != null;

            if (isFlags)
            {
                // For flags, we need to collect all set flag names.
                List<string> names = new List<string>(DefaultCapacity);
                Array values = Enum.GetValues(enumType);
                // Process in descending order to handle combined flags correctly.
                for (int i = values.Length - 1; i >= 0; i--)
                {
                    long flagValue = Convert.ToInt64(values.GetValue(i));
                    if (flagValue == 0)
                        continue; // Skip zero.

                    if ((longValue & flagValue) == flagValue)
                    {
                        string name = Enum.GetName(enumType, values.GetValue(i));
                        if (!string.IsNullOrEmpty(name))
                            names.Add(name);
                        longValue &= ~flagValue; // Remove the flag to avoid duplicates.
                    }
                }

                // If any bits remain (e.g., undefined combination), add them as numbers.
                if (longValue != 0)
                    names.Add(longValue.ToString());

                if (names.Count == 0)
                    return "0";

                return string.Join(", ", names);
            }
            else
            {
                // Non-flags enum: just get the name.
                string name = Enum.GetName(enumType, enumValue);
                return name ?? Convert.ToInt64(enumValue).ToString();
            }
        }

        // Parses a string into an enum value of the specified type.
        // Supports comma-separated flags and ignores case unless specified.
        private static object ParseEnum(string value, Type enumType, bool ignoreCase = true, CultureInfo culture = null)
        {
            if (string.IsNullOrWhiteSpace(value) || enumType == null)
                return null;

            if (culture == null)
                culture = CultureInfo.InvariantCulture;

            Type underlyingType = Enum.GetUnderlyingType(enumType);
            TypeCode typeCode = Type.GetTypeCode(underlyingType);

            string[] parts = value.Split(_enumSeparator, StringSplitOptions.RemoveEmptyEntries);

            ulong result = 0;

            foreach (string part in parts)
            {
                string trimmed = part.Trim();

                if (trimmed.Length == 0)
                    continue;

                // Try to parse by enum name first.
                try
                {
                    object parsed = Enum.Parse(enumType, trimmed, ignoreCase);

                    switch (typeCode)
                    {
                        case TypeCode.SByte:
                            result |= unchecked((ulong)(sbyte)parsed);
                            break;

                        case TypeCode.Byte:
                            result |= (byte)parsed;
                            break;

                        case TypeCode.Int16:
                            result |= unchecked((ulong)(short)parsed);
                            break;

                        case TypeCode.UInt16:
                            result |= (ushort)parsed;
                            break;

                        case TypeCode.Int32:
                            result |= unchecked((ulong)(int)parsed);
                            break;

                        case TypeCode.UInt32:
                            result |= (uint)parsed;
                            break;

                        case TypeCode.Int64:
                            result |= unchecked((ulong)(long)parsed);
                            break;

                        case TypeCode.UInt64:
                            result |= (ulong)parsed;
                            break;
                    }

                    continue;
                }
                catch
                {
                    // Name parsing failed. Try numeric conversion below.
                }

                // Try numeric conversion using the common numeric parser.
                object numeric = ParseNumber(trimmed, underlyingType, culture);

                if (numeric == null)
                    return null;

                switch (typeCode)
                {
                    case TypeCode.SByte:
                        result |= unchecked((ulong)(sbyte)numeric);
                        break;

                    case TypeCode.Byte:
                        result |= (byte)numeric;
                        break;

                    case TypeCode.Int16:
                        result |= unchecked((ulong)(short)numeric);
                        break;

                    case TypeCode.UInt16:
                        result |= (ushort)numeric;
                        break;

                    case TypeCode.Int32:
                        result |= unchecked((ulong)(int)numeric);
                        break;

                    case TypeCode.UInt32:
                        result |= (uint)numeric;
                        break;

                    case TypeCode.Int64:
                        result |= unchecked((ulong)(long)numeric);
                        break;

                    case TypeCode.UInt64:
                        result |= (ulong)numeric;
                        break;

                    default:
                        return null;
                }
            }

            switch (typeCode)
            {
                case TypeCode.SByte:
                    return Enum.ToObject(enumType, unchecked((sbyte)result));

                case TypeCode.Byte:
                    return Enum.ToObject(enumType, unchecked((byte)result));

                case TypeCode.Int16:
                    return Enum.ToObject(enumType, unchecked((short)result));

                case TypeCode.UInt16:
                    return Enum.ToObject(enumType, unchecked((ushort)result));

                case TypeCode.Int32:
                    return Enum.ToObject(enumType, unchecked((int)result));

                case TypeCode.UInt32:
                    return Enum.ToObject(enumType, unchecked((uint)result));

                case TypeCode.Int64:
                    return Enum.ToObject(enumType, unchecked((long)result));

                case TypeCode.UInt64:
                    return Enum.ToObject(enumType, result);

                default:
                    return null;
            }
        }

        // Returns a new array with transform applied to each element.
        // The source array is not modified.
        private static T[] TransformArray<T>(T[] source, Func<T, T> transform)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (transform == null)
                throw new ArgumentNullException(nameof(transform));

            int length = source.Length;
            var result = new T[length];
            for (int i = 0; i < length; i++)
                result[i] = transform(source[i]);

            return result;
        }

        // Convert primitive values (usefull for default value attribute).
        private static object ConvertPrimitive(object value, Type targetType, CultureInfo culture)
        {
            if (value == null || !targetType.IsPrimitive)
                return null;

            if (culture == null)
                culture = CultureInfo.InvariantCulture;

            try
            {
                return Convert.ChangeType(value, targetType, culture);
            }
            catch
            {
                return null;
            }
        }

        // Custom parsing for decimal, hex, octal and binary numeric values.
        private static object ParseNumber(string value, Type targetType, CultureInfo culture)
        {
            if (value == null)
                return null;

            if (culture == null)
                culture = CultureInfo.InvariantCulture;

            if (value.Length == 0)
                return null;

            int start;
            int length;

            // Trim whitespace.
            for (start = 0; start < value.Length && char.IsWhiteSpace(value[start]); start++)
                ;

            int end = value.Length - 1;

            for (; end >= start && char.IsWhiteSpace(value[end]); end--)
                ;

            if (start > end)
                return null;

            length = end - start + 1;

            int radix = 10;

            // Detect prefix.
            if (length >= 2)
            {
                char first = value[start];
                char second = value[start + 1];

                if (first == '0')
                {
                    // Hex
                    if (second == 'x' || second == 'X')
                    {
                        radix = 16;
                        start += 2;
                        length -= 2;
                    }
                    // Bin
                    else if (second == 'b' || second == 'B')
                    {
                        radix = 2;
                        start += 2;
                        length -= 2;
                    }
                    // Oct
                    else if (second == 'o' || second == 'O')
                    {
                        radix = 8;
                        start += 2;
                        length -= 2;
                    }
                }
                else if (first == '&')
                {
                    // Hex
                    if (second == 'h' || second == 'H')
                    {
                        radix = 16;
                        start += 2;
                        length -= 2;
                    }
                    // Oct
                    else if (second == 'o' || second == 'O')
                    {
                        radix = 8;
                        start += 2;
                        length -= 2;
                    }
                    // Hex
                    else
                    {
                        radix = 16;
                        start++;
                        length--;
                    }
                }
                // Oct
                else if (first == '8' && second == '#')
                {
                    radix = 8;
                    start += 2;
                    length -= 2;
                }
                // Bin
                else if (first == '%')
                {
                    radix = 2;
                    start++;
                    length--;
                }
                // Hex
                else if (first == '$' || first == '#')
                {
                    radix = 16;
                    start++;
                    length--;
                }
            }
            // Bin
            else if (length == 1 && value[start] == '%')
            {
                radix = 2;
                start++;
                length--;
            }

            // Detect suffix.
            if (length > 0)
            {
                char last = value[start + length - 1];

                // Hex
                if (last == 'h' || last == 'H')
                {
                    radix = 16;
                    length--;
                }
                // Bin
                else if (last == 'b' || last == 'B')
                {
                    radix = 2;
                    length--;
                }
                // Oct
                else if (last == 'o' || last == 'O')
                {
                    radix = 8;
                    length--;
                }
            }

            if (length <= 0) // Should never be.
                return null;

            string number = value.Substring(start, length);
            TypeCode typeCode = Type.GetTypeCode(targetType);

            if (radix == 10)
            {
                switch (typeCode)
                {
                    case TypeCode.Byte
                        when Byte.TryParse(number, NumberStyles.Integer, culture, out byte @byte):
                        return @byte;

                    case TypeCode.SByte
                        when SByte.TryParse(number, NumberStyles.Integer, culture, out sbyte @sbyte):
                        return @sbyte;

                    case TypeCode.Int16
                        when Int16.TryParse(number, NumberStyles.Integer, culture, out short int16):
                        return int16;

                    case TypeCode.UInt16
                        when UInt16.TryParse(number, NumberStyles.Integer, culture, out ushort uint16):
                        return uint16;

                    case TypeCode.Int32
                        when Int32.TryParse(number, NumberStyles.Integer, culture, out int int32):
                        return int32;

                    case TypeCode.UInt32
                        when UInt32.TryParse(number, NumberStyles.Integer, culture, out uint uint32):
                        return uint32;

                    case TypeCode.Int64
                        when Int64.TryParse(number, NumberStyles.Integer, culture, out long int64):
                        return int64;

                    case TypeCode.UInt64
                        when UInt64.TryParse(number, NumberStyles.Integer, culture, out ulong uint64):
                        return uint64;

                    case TypeCode.Single
                        when Single.TryParse(number, NumberStyles.Float, culture, out float single):
                        return single;

                    case TypeCode.Double
                        when Double.TryParse(number, NumberStyles.Float, culture, out double @double):
                        return @double;

                    case TypeCode.Decimal
                        when Decimal.TryParse(number, NumberStyles.Number, culture, out decimal decimalValue):
                        return decimalValue;
                }
            }
            else
            {
                /*System.Diagnostics.Debug.WriteLine(
                    "value=[" + value + "] number=[" + number + "] radix=" + radix);*/

                try
                {
                    switch (typeCode)
                    {
                        case TypeCode.Byte:
                            return Convert.ToByte(number, radix);

                        case TypeCode.SByte:
                            return Convert.ToSByte(number, radix);

                        case TypeCode.Int16:
                            return Convert.ToInt16(number, radix);

                        case TypeCode.UInt16:
                            return Convert.ToUInt16(number, radix);

                        case TypeCode.Int32:
                            return Convert.ToInt32(number, radix);

                        case TypeCode.UInt32:
                            return Convert.ToUInt32(number, radix);

                        case TypeCode.Int64:
                            return Convert.ToInt64(number, radix);

                        case TypeCode.UInt64:
                            return Convert.ToUInt64(number, radix);
                    }
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        // Expands environment variables
        private static string ExpandVariables(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            string cmdLine = Environment.CommandLine;
            string[] cmdArgs = Environment.GetCommandLineArgs();
            input = Environment.ExpandEnvironmentVariables(input);
            var random = new Random();

            // Path to EXE.
            if (input.Contains("%0"))
            {
                string arg0 = cmdArgs.Length > 0 ? cmdArgs[0] : "";
                input = input.Replace("%0", arg0);
            }

            // Command line arguments %1..%9
            for (int i = 1; i <= 9; i++)
            {
                string varName = $"%" + i;
                if (input.Contains(varName))
                {
                    string arg = i < cmdArgs.Length ? cmdArgs[i] : "";
                    input = input.Replace(varName, arg);
                }
            }

            // Command line.
            if (input.Contains("%*"))
            {
                string allArgs = cmdArgs.Length > 1
                    ? string.Join(" ", cmdArgs, 1, cmdArgs.Length - 1)
                    : "";
                input = input.Replace("%*", allArgs);
            }

            // Random integer.
            if (input.Contains("%RANDOM%"))
            {
                uint randomValue = (uint)random.Next(int.MinValue, int.MaxValue);
                input = input.Replace("%RANDOM%", randomValue.ToString());
            }

            // Current date.
            if (input.Contains("%DATE%"))
            {
                string date = DateTime.Now.ToString("yyyyMMdd");
                input = input.Replace("%DATE%", date);
            }
            
            // Current time.
            if (input.Contains("%TIME%"))
            {
                string time = DateTime.Now.ToString("HHmmss");
                input = input.Replace("%TIME%", time);
            }

            // Current directory.
            if (input.Contains("%CD%"))
            {
                string currentDir = Environment.CurrentDirectory;
                input = input.Replace("%CD%", currentDir);
            }

            // Current directory (with slash).
            if (input.Contains("%__CD__%"))
            {
                string currentDirWithSlash = Environment.CurrentDirectory;
                if (!currentDirWithSlash.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    currentDirWithSlash += Path.DirectorySeparatorChar;
                input = input.Replace("%__CD__%", currentDirWithSlash);
            }

            // Command line for this process.
            if (input.Contains("%CMDCMDLINE%"))
            {
                input = input.Replace("%CMDCMDLINE%", cmdLine);
            }

            // Directory contains EXE file.
            if (input.Contains("%__APPDIR__%"))
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!appDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    appDir += Path.DirectorySeparatorChar;
                input = input.Replace("%__APPDIR__%", appDir);
            }

            return input;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            try
            {
                string expanded = ExpandVariables(path);
                string fullPath = Path.GetFullPath(expanded);
                return fullPath;
            }
            catch
            {
                return path;
            }
        }

        // Converts escaped characters in the input string.
        private static string UnEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            int pos = -1;
            int inputLength = value.Length;

            if (inputLength == 0) return value;

            // Find the first backslash or return the original text without allocating.
            for (int i = 0; i < inputLength; ++i)
            {
                if (value[i] == '\\')
                {
                    pos = i;
                    break;
                }
            }

            if (pos < 0) return value; // No backslash found.

            // Copy the unchanged prefix preceding the first escape sequence.
            StringBuilder sb = new StringBuilder(inputLength);
            sb.Append(value, 0, pos);

            do
            {
                char c = value[pos++];
                if (c == '\\')
                {
                    // Read the escape sequence following the backslash.
                    // If the backslash is the last character, keep it unchanged.
                    c = pos < inputLength ? value[pos] : '\\';
                    switch (c)
                    {
                        case '\\': c = '\\'; break;
                        case '0': c = '\0'; break;
                        case 'a': c = '\a'; break;
                        case 'b': c = '\b'; break;
                        case 'n': c = '\n'; break;
                        case 'r': c = '\r'; break;
                        case 'f': c = '\f'; break;
                        case 't': c = '\t'; break;
                        case 'v': c = '\v'; break;
                        case '"': c = '"'; break;
                        case '\'': c = '\''; break;
                        // Unicode escape: \uXXXX
                        case 'u' when pos < inputLength - 3:
                            c = UnHex(value, ++pos, 4);
                            pos += 3;
                            break;
                        // Hex escape: \xXX
                        case 'x' when pos < inputLength - 1:
                            c = UnHex(value, ++pos, 2);
                            pos++;
                            break;
                        // Control character escape: \cA .. \cZ
                        case 'c' when pos < inputLength:
                            c = value[++pos];
                            if (c >= 'a' && c <= 'z')
                                c -= ' ';
                            if ((c = (char)(c - 0x40U)) >= ' ')
                                c = '?';
                            break;
                        // Unknown escape sequence.
                        // Preserve it exactly as it appears in the input string.
                        default:
                            sb.Append('\\');
                            sb.Append(c);
                            pos++;
                            continue;
                    }
                    // Skip the escape code character.
                    pos++;
                }
                sb.Append(c);

            } while (pos < inputLength);

            return sb.ToString();
        }

        // Converts special characters in the input string to escaped sequences.
        private static string ToEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;

            int pos = 0;
            int inputLength = value.Length;

            if (inputLength == 0) return value;

            // Allocate enough capacity for the maximum possible escaped length.
            StringBuilder sb = new StringBuilder(inputLength * 2);
            do
            {
                char c = value[pos++];

                switch (c)
                {
                    case '\\': sb.Append(@"\\"); break;
                    case '\0': sb.Append(@"\0"); break;
                    case '\a': sb.Append(@"\a"); break;
                    case '\b': sb.Append(@"\b"); break;
                    case '\n': sb.Append(@"\n"); break;
                    case '\r': sb.Append(@"\r"); break;
                    case '\f': sb.Append(@"\f"); break;
                    case '\t': sb.Append(@"\t"); break;
                    case '\v': sb.Append(@"\v"); break;
                    case '"': sb.Append(@"\"""); break;
                    case '\'': sb.Append(@"\'"); break;
                    default:
                        sb.Append(c);
                        break;
                }
            } while (pos < inputLength);

            return sb.ToString();
        }

        // Converts hex number to unicode character.
        /*private static char UnHex(string value)
        {
            if (value == null) return '\0';

            int c = 0;
            for (int i = 0; i < value.Length; i++)
            {
                int r = value[i]; // Obtain next digit.
                if (r > 0x2F && r < 0x3A) r -= 0x30;
                else if (r > 0x40 && r < 0x47) r -= 0x37;
                else if (r > 0x60 && r < 0x67) r -= 0x57;
                else return '?';
                c = (c << 4) + r; // Insert next digit.
            }

            return (char)c;
        }*/

        private static char UnHex(string value, int index, int length)
        {
            int c = 0;

            for (int i = 0; i < length; i++)
            {
                int digit = ParseHexDigit(value[index++]);
                if (digit < 0)
                    return '?';

                c = (c << 4) + digit;
            }

            return (char)c;
        }

        // Removes the outer quotes from a wrapped value and trims spaces and tabs inside the braces.
        private static string UnQuote(string value)
        {
            if (value == null) return null;

            int length = value.Length;
            if (length < 2 || value[0] != '"' || value[length - 1] != '"')
                return value;

            // trim whitespace characters.
            int start = 1;
            while (start < length - 1 && (value[start] == ' ' || value[start] == '\t'))
                start++;

            int end = length - 2;
            while (end >= start && (value[end] == ' ' || value[end] == '\t'))
                end--;

            // If there is no content, return empty string.
            if (start > end)
                return string.Empty;

            // Extract the trimmed inner content (single allocation).
            return value.Substring(start, end - start + 1);
        }

        // Wraps a multiline value in quotes'.
        private string ToQuote(string value)
        {
            if (value == null) return null;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\r' || c == '\n')
                {
                    return string.Concat('"', _lineBreaker, value, _lineBreaker, '"');
                }
            }

            return value;
        }

        // Converts a byte array to a hexadecimal string without separators.
        private static string ToHexString(byte[] bytes)
        {
            if (bytes == null) return null;
            if (bytes.Length == 0) return string.Empty;

            char[] chars = new char[bytes.Length * 3 - 1];
            for (int i = 0, j = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                chars[j++] = GetHexChar(b >> 4);
                chars[j++] = GetHexChar(b & 0x0F);
                if (i < bytes.Length - 1)
                    chars[j++] = ' ';
            }
            return new string(chars);
        }

        // Returns the uppercase hexadecimal character for a nibble value (0-15).
        private static char GetHexChar(int value)
        {
            if (value < 10)
                return (char)('0' + value);
            else
                return (char)('A' + (value - 10));
        }

        // Converts a hexadecimal string to a byte array.
        private static byte[] FromHexString(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                return null;

            // Remove all whitespace characters
            int length = hex.Length;
            char[] filtered = new char[length];
            int count = 0;
            for (int i = 0; i < length; i++)
            {
                char c = hex[i];
                if (!char.IsWhiteSpace(c))
                {
                    filtered[count++] = c;
                }
            }

            // Must have at least one digit and even number of digits
            if (count == 0 || (count % 2) != 0)
                return null;

            byte[] result = new byte[count / 2];
            int pos = 0;
            for (int i = 0; i < count; i += 2)
            {
                int high = ParseHexDigit(filtered[i]);
                int low = ParseHexDigit(filtered[i + 1]);
                if (high < 0 || low < 0)
                    return null;
                result[pos++] = (byte)((high << 4) | low);
            }
            return result;
        }

        // Converts a single hexadecimal character (0-9, A-F, a-f) to its integer value.
        private static int ParseHexDigit(char c)
        {
            if (c >= '0' && c <= '9')
                return c - '0';
            if (c >= 'A' && c <= 'F')
                return c - 'A' + 10;
            if (c >= 'a' && c <= 'f')
                return c - 'a' + 10;
            return -1;
        }

        private static bool IsNewLine(char c)
        {
            return c == '\n' || c == '\r';
        }

        // Moves index to the end of current line in the StringBuilder.
        private static StringBuilder MoveIndexToEndOfLinePosition(StringBuilder sb, ref int index)
        {
            int length = sb.Length;

            // Adjust index if it's beyond the current length.
            if (index < 0) index = 0;
            else if (index >= length) index = length;

            // Search for the nearest line breaker and move index to position after line breaker.
            else if (index > 0)
            {
                while (index < length && !IsNewLine(sb[index]))
                    index++;

                while (index < length && IsNewLine(sb[index]))
                    index++;
            }

            return sb;
        }

        // Inserts a specified line at the specified index in the StringBuilder, followed by a specified new line and update the index.
        private static StringBuilder InsertLine(StringBuilder sb, ref int index, string newLine, string text)
        {
            if (sb == null) throw new ArgumentNullException(nameof(sb));
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));

            sb = MoveIndexToEndOfLinePosition(sb, ref index);

            // Insert the line content.
            sb = sb.Insert(index, text);
            index += text.Length;

            // Insert the new line.
            sb = sb.Insert(index, newLine);
            index += newLine.Length - 1;

            return sb;
        }

        // Detects the most likely line breaker by counting CR and LF characters with 10% threshold.
        private static string AutoDetectLineBreaker(string text)
        {
            if (string.IsNullOrEmpty(text)) return Environment.NewLine;

            int crCount = 0;
            int lfCount = 0;

            // Count CR and LF characters.
            for (int index = 0; index < text.Length; index++)
            {
                if (text[index] == '\r')
                    crCount++;
                else if (text[index] == '\n')
                    lfCount++;
            }

            int crlfCount = Math.Min(crCount, lfCount);
            int crOnlyCount = crCount - crlfCount;
            int lfOnlyCount = lfCount - crlfCount;
            int total = crlfCount + crOnlyCount + lfOnlyCount;

            if (total == 0)
                return Environment.NewLine;

            int threshold = total / 10; // 10% occurrence threshold.

            // Prefer CRLF when it is used frequently enough.
            if (crlfCount > threshold)
                return "\r\n";

            // Otherwise check single-character line breakers.
            if (lfOnlyCount > threshold)
                return "\n";

            if (crOnlyCount > threshold)
                return "\r";

            return Environment.NewLine;
        }

        // Tries to detect the text encoding using BOM and simple heuristics.
        private static Encoding AutoDetectEncoding(string fileName, Encoding defaultEncoding = null)
        {
            const int SampleSize = 4096;
            byte[] buffer = new byte[SampleSize];
            int totalRead = 0;

            using (FileStream fs = File.OpenRead(fileName))
            {
                // Read until the buffer is full or EOF is reached.
                while (totalRead < SampleSize)
                {
                    int bytesRead = fs.Read(buffer, totalRead, SampleSize - totalRead);
                    if (bytesRead == 0)
                        break;
                    totalRead += bytesRead;
                }
            }

            int count = totalRead;

            // ----- BOM detection (most reliable) -----
            if (count >= 4)
            {
                // UTF‑32 Big Endian
                if (buffer[0] == 0x00 && buffer[1] == 0x00 &&
                    buffer[2] == 0xFE && buffer[3] == 0xFF)
                    return Encoding.GetEncoding("utf-32BE");

                // UTF‑32 Little Endian
                if (buffer[0] == 0xFF && buffer[1] == 0xFE &&
                    buffer[2] == 0x00 && buffer[3] == 0x00)
                    return Encoding.UTF32;
            }

            if (count >= 3)
            {
                // UTF‑8 BOM
                if (buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                    return Encoding.UTF8;

#pragma warning disable SYSLIB0001 // UTF‑7 is obsolete
                // UTF‑7 BOM (rare, but kept for legacy)
                if (buffer[0] == 0x2B && buffer[1] == 0x2F && buffer[2] == 0x76)
                    return Encoding.UTF7;
#pragma warning restore SYSLIB0001
            }

            if (count >= 2)
            {
                // UTF‑16 Little Endian
                if (buffer[0] == 0xFF && buffer[1] == 0xFE)
                    return Encoding.Unicode;

                // UTF‑16 Big Endian
                if (buffer[0] == 0xFE && buffer[1] == 0xFF)
                    return Encoding.BigEndianUnicode;
            }

            // ----- UTF‑16 heuristic: count zero bytes in even/odd positions -----
            int evenZero = 0, oddZero = 0;
            for (int i = 0; i + 1 < count; i += 2)
            {
                if (buffer[i] == 0) evenZero++;
                if (buffer[i + 1] == 0) oddZero++;
            }

            int pairs = count / 2;
            if (pairs > 8)
            {
                if (oddZero > pairs * 8 / 10)
                    return Encoding.Unicode;          // LE
                if (evenZero > pairs * 8 / 10)
                    return Encoding.BigEndianUnicode; // BE
            }

            // ----- UTF‑8 heuristic (no BOM) -----
            if (IsUtf8(buffer, count))
                return Encoding.UTF8;

            // ----- Fallback -----
            return defaultEncoding ?? Encoding.Default;
        }
        // Determines whether the given byte buffer contains valid UTF‑8 and includes at least one multibyte character.
        private static bool IsUtf8(byte[] buffer, int count)
        {
            bool hasMultibyte = false;

            for (int i = 0; i < count;)
            {
                byte b = buffer[i];

                // ASCII: single byte, valid UTF‑8.
                if (b <= 0x7F)
                {
                    i++;
                    continue;
                }

                int remaining;
                byte minContinuation = 0x80;
                byte maxContinuation = 0xBF;

                // 2‑byte sequence: C2..DF 80..BF
                if ((b & 0xE0) == 0xC0)
                {
                    remaining = 1;
                    // C0 and C1 are overlong for ASCII.
                    if (b < 0xC2)
                        return false;
                }
                // 3‑byte sequence: E0..EF 80..BF 80..BF
                else if ((b & 0xF0) == 0xE0)
                {
                    remaining = 2;
                    // E0 A0..BF to avoid overlong; ED 80..9F to avoid surrogates.
                    if (b == 0xE0)
                        minContinuation = 0xA0;
                    else if (b == 0xED)
                        maxContinuation = 0x9F;
                }
                // 4‑byte sequence: F0..F4 80..BF 80..BF 80..BF
                else if ((b & 0xF8) == 0xF0)
                {
                    remaining = 3;
                    // F0 90..BF to avoid overlong; F4 80..8F to stay within U+10FFFF.
                    if (b == 0xF0)
                        minContinuation = 0x90;
                    else if (b == 0xF4)
                        maxContinuation = 0x8F;
                    else if (b > 0xF4)
                        return false;
                }
                else
                {
                    // Invalid leading byte.
                    return false;
                }

                // If the sequence is cut off at the end of the sample, treat it as
                // a boundary issue and ignore it (provided we already saw a complete
                // multibyte sequence elsewhere).
                if (i + remaining >= count)
                    return hasMultibyte;

                // Validate the first continuation byte with the special range.
                byte cont = buffer[i + 1];
                if (cont < minContinuation || cont > maxContinuation)
                    return false;

                // Remaining continuation bytes (if any) must be 10xxxxxx.
                for (int j = 2; j <= remaining; j++)
                {
                    if ((buffer[i + j] & 0xC0) != 0x80)
                        return false;
                }

                hasMultibyte = true;
                i += remaining + 1;
            }

            return hasMultibyte;
        }

        // Normalizes the string case according to the specified comparison mode.
        private static string NormalizeString(string text, StringComparison comparison)
        {
            if ((((int)comparison) & 1) != 0)
                switch (comparison)
                {
                    case StringComparison.CurrentCultureIgnoreCase:
                        return text.ToLower(CultureInfo.CurrentCulture);
                    case StringComparison.InvariantCultureIgnoreCase:
                    case StringComparison.OrdinalIgnoreCase:
                        return text.ToLowerInvariant();
                }

            return text;
        }

        // Normalizes the substring case according to the specified comparison mode.
        private static string NormalizeSubstring(string source, int index, int length, StringComparison comparison)
        {
            if ((((int)comparison) & 1) != 0)
            {
                switch (comparison)
                {
                    case StringComparison.CurrentCultureIgnoreCase:
                        return source.Substring(index, length).ToLower(CultureInfo.CurrentCulture);

                    case StringComparison.InvariantCultureIgnoreCase:
                    case StringComparison.OrdinalIgnoreCase:
                        return source.Substring(index, length).ToLowerInvariant();
                }
            }

            return source.Substring(index, length);
        }

        // Replaces all line break sequences with the specified line breaker.
        private static string NormalizeLineBreaker(string value, string lineBreaker)
        {
            if (value == null) return null;
            if (lineBreaker == null) lineBreaker = Environment.NewLine;

            int length = value.Length;
            bool normalize = false;

            // Check whether normalization is required.
            for (int i = 0; i < length && !normalize; i++)
            {
                char c = value[i];

                if (c == '\r')
                {
                    normalize =
                        lineBreaker != "\r" ||
                        (i + 1 < length && value[i + 1] == '\n');
                }
                else if (c == '\n')
                {
                    normalize =
                        lineBreaker != "\n" ||
                        (i == 0 || value[i - 1] != '\r');
                }
            }

            if (!normalize)
                return value;

            StringBuilder sb = new StringBuilder(length);

            for (int i = 0; i < length; i++)
            {
                char c = value[i];

                if (c == '\r')
                {
                    // Skip '\n' in CRLF.
                    if (i + 1 < length && value[i + 1] == '\n')
                        i++;

                    sb.Append(lineBreaker);
                }
                else if (c == '\n')
                {
                    // Standalone LF.
                    sb.Append(lineBreaker);
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        // Checks whether the fileName string contains invalid characters for the path.
        private static bool IsInvalidPath(string fileName)
        {
            return fileName.Any(InvalidPathChar);
        }

        private static bool InvalidPathChar(char c)
        {
            return _invalidPathChars.Contains(c);
        }

        // Checks whether the file name is correct and, if necessary, whether the file exists.
        // Returns null if the file name is valid, otherwise returns an Exception object to throw at the calling code.
        private static Exception ValidateFileName(string fileName, bool checkExists = false)
        {
            if (fileName == null)
                return new ArgumentNullException(nameof(fileName));
            if (string.IsNullOrEmpty(fileName) || fileName.All(char.IsWhiteSpace) || IsInvalidPath(fileName))
                return new ArgumentException(null, nameof(fileName));
            if (checkExists && !File.Exists(fileName))
                return new FileNotFoundException(null, fileName);

            return null;
        }

        // Validates
        private static string GetFullPath(string fileName, bool checkExists = false)
        {
            if (ValidateFileName(fileName, checkExists) is Exception exception)
                throw exception;

            return Path.GetFullPath(fileName);
        }

        // Gets the declaring path of the specified type, using the specified delimiter.
        private static string GetDeclaringPath(Type type, char delimiter = '.')
        {
            // Initialize a StringBuilder with the initial name of the type.
            StringBuilder sb = new StringBuilder(type.Name);

            // Traverse through the declaring types, if any, in a loop.
            while ((type = type.DeclaringType) != null)
            {
                sb.Insert(0, delimiter);
                sb.Insert(0, type.Name);
            }

            return sb.ToString();
        }

        // Splits path into its segments.
        // Supports '/' and '\' as separators.
        private static string[] GetPathSegments(string path)
        {
            if (string.IsNullOrEmpty(path))
                return Array.Empty<string>();

            return path.Split(_pathSeparatorChars, StringSplitOptions.RemoveEmptyEntries);
        }

        // Compares a substring of the source string with the specified value
        // without allocating an intermediate string.
        private static bool SubstringEquals(string source, int index, int length, string value, StringComparison comparison)
        {
            if (ReferenceEquals(source, value))
                return true;

            if (source == null || value == null)
                return false;

            if (length != value.Length)
                return false;

            return string.Compare(source, index, value, 0, length, comparison) == 0;
        }

        #endregion

        /************************************************** Public API **************************************************/

        #region Object overrides

        /// <inheritdoc/>
        public override string ToString()
        {
            return Content;
        }

        /// <summary>
        /// Reads or writes the value associated with the specified section and key to the ini file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <returns>
        /// The value associated with the specified section and key.
        /// If the specified entry is not found, attempting to get it returns the empty string,
        /// and attempting to set it creates a new entry using the specified name.
        /// </returns>
        public string this[string section, string key]
        {
            get => ReadString(section, key, string.Empty);
            set => WriteString(section, key, value);
        }

        /// <summary>
        /// Reads or writes the value associated with the specified name.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be returned if the specified entry is not found.
        /// </param>
        /// <returns>
        /// The value associated with the specified name.
        /// If the specified entry is not found, attempting to get it returns the <paramref name="defaultValue"/>,
        /// and attempting to set it creates a new entry using the specified name.
        /// </returns>
        public string this[string section, string key, string defaultValue]
        {
            get => ReadString(section, key, defaultValue);
        }

        #endregion

        #region Public read methods

        /// <summary>
        /// Exports the INI file content to a dictionary mapping section names to a dictionary
        /// of keys with lists of their associated values (preserving order and duplicates).
        /// </summary>
        /// <returns>
        /// A dictionary where the key is the section name (empty string for global entries)
        /// and the value is a dictionary of key → list of values for that section.
        /// </returns>
        public Dictionary<string, Dictionary<string, List<string>>> ExportToDictionary()
        {
            StringComparer comparer = GetComparer(_comparison);
            var result = new Dictionary<string, Dictionary<string, List<string>>>(DefaultCapacity, comparer);

            string currentSection = string.Empty; // global
            Dictionary<string, List<string>> currentDict = null;

            for (int i = 0; i < _matches.Count; i++) // Ignore comments, whitespace, etc.
            {
                Match match = _matches[i];

                if (match.Groups[_iniSection].Success)
                {
                    string sectionName = match.Groups[_iniValue].Value;
                    // Normalize case according to comparison settings
                    sectionName = NormalizeString(sectionName, _comparison);

                    // Add new section if not exists
                    if (!result.TryGetValue(sectionName, out currentDict))
                    {
                        currentDict = new Dictionary<string, List<string>>(DefaultCapacity, comparer);
                        result[sectionName] = currentDict;
                    }
                    currentSection = sectionName;
                    continue;
                }

                if (match.Groups[_iniEntry].Success)
                {
                    // If no section yet, use global (empty key)
                    if (currentDict == null)
                    {
                        // Global section
                        if (!result.TryGetValue(string.Empty, out currentDict))
                        {
                            currentDict = new Dictionary<string, List<string>>(DefaultCapacity, comparer);
                            result[string.Empty] = currentDict;
                        }
                    }

                    string key = match.Groups[_iniKey].Value;
                    string value = match.Groups[_iniValue].Value;

                    // Unwrap/unescape if needed
                    //if (_allowMultiLine) value = UnWrap(value);
                    if (_allowEscapeChars) value = UnEscape(value);

                    // Normalize key case
                    key = NormalizeString(key, _comparison);

                    if (!currentDict.TryGetValue(key, out List<string> values))
                    {
                        values = new List<string>(DefaultCapacity);
                        currentDict[key] = values;
                    }
                    values.Add(value);
                }
            }

            return result;
        }

        /// <summary>
        /// Returns a simplified version of the INI file content containing only sections
        /// and key-value pairs, without comments, empty lines, and extra whitespace.
        /// The resulting string uses the delimiter and line breaker configured for the
        /// current instance (or auto-detected from the original content).
        /// Multiple values for the same key are preserved as separate lines.
        /// The order of sections and keys is preserved.
        /// </summary>
        /// <returns>A compacted INI string.</returns>
        public string Justify()
        {
            // Nothing to normalize.
            if (_matches.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();

            // We collect sections and entries in file order to preserve the original layout.
            var globalEntries = new List<KeyValuePair<string, string>>(DefaultCapacity);
            var sectionEntries = new Dictionary<string, List<KeyValuePair<string, string>>>(GetComparer(_comparison));
            var sectionOrder = new List<string>(DefaultCapacity);
            string currentSection = null;

            for (int i = 0; i < _matches.Count; i++)
            {
                Match match = _matches[i];

                if (match.Groups[_iniSection].Success)
                {
                    string section = match.Groups[_iniValue].Value;

                    if (!sectionEntries.ContainsKey(section))
                    {
                        sectionEntries[section] = new List<KeyValuePair<string, string>>(DefaultCapacity);
                        sectionOrder.Add(section);
                    }

                    currentSection = section;
                    continue;
                }

                if (match.Groups[_iniEntry].Success)
                {
                    string key = match.Groups[_iniKey].Value;
                    string value = match.Groups[_iniValue].Value;

                    if (currentSection == null)
                    {
                        // Global section — entries above the first named section.
                        globalEntries.Add(new KeyValuePair<string, string>(key, value));
                    }
                    else if (sectionEntries.TryGetValue(currentSection, out var list))
                    {
                        list.Add(new KeyValuePair<string, string>(key, value));
                    }
                }
            }

            // Global entries first, if any.
            if (globalEntries.Count > 0)
            {
                for (int i = 0; i < globalEntries.Count; i++)
                {
                    KeyValuePair<string, string> kv = globalEntries[i];
                    sb.Append(kv.Key).Append(_defaultDelimiter).Append(kv.Value).Append(_lineBreaker);
                }
                sb.Append(_lineBreaker);
            }

            // Then each section in file order.
            for (int s = 0; s < sectionOrder.Count; s++)
            {
                string section = sectionOrder[s];
                sb.Append('[').Append(section).Append(']').Append(_lineBreaker);

                List<KeyValuePair<string, string>> entries = sectionEntries[section];
                for (int i = 0; i < entries.Count; i++)
                {
                    KeyValuePair<string, string> kv = entries[i];
                    sb.Append(kv.Key).Append(_defaultDelimiter).Append(kv.Value).Append(_lineBreaker);
                }

                sb.Append(_lineBreaker); // blank line after each section
            }

            // Remove trailing line breakers (each loop iteration appends one, plus the
            // separator between blocks). Trim down to a single clean end.
            while (sb.Length > 0 && IsNewLine(sb[sb.Length - 1]))
                sb.Length--;

            return sb.ToString();
        }

        /// <summary>
        /// Reads all sections from the INI file.
        /// </summary>
        /// <returns>
        ///  A string array contains all names of sections.
        /// </returns>
        public string[] ReadSections()
        {
            return GetSections().ToArray();
        }

        /// <summary>
        /// Reads all keys associated with the specified section from the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <returns>
        /// A string array contains all names of keys associated with the specified section.
        /// </returns>
        public string[] ReadKeys(string section = null)
        {
            return GetKeys(section).ToArray();
        }

        /// <summary>
        /// Determines whether the INI file contains a section with the specified name.
        /// </summary>
        /// <param name="section">
        /// The section name to look for. A <c>null</c> or empty value is not a valid
        /// section name and always returns <c>false</c>.
        /// </param>
        /// <returns>
        /// <c>true</c> if a section with the given name exists; otherwise <c>false</c>.
        /// </returns>
        /// <remarks>
        /// Global entries (those located before the first named section) are not
        /// considered a section, so <c>ContainsSection(null)</c> and
        /// <c>ContainsSection(string.Empty)</c> always return <c>false</c>.
        /// </remarks>
        public bool ContainsSection(string section)
        {
            return TryGetSection(section, out _);
        }

        /// <summary>
        /// Determines whether the INI file contains an entry with the specified key
        /// in the given section.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass <c>null</c> or an empty string to search global entries
        /// located above all named sections.
        /// </param>
        /// <param name="key">
        /// The key name to look for. Cannot be <c>null</c>.
        /// </param>
        /// <returns>
        /// <c>true</c> if an entry with the given key exists in the specified section;
        /// otherwise <c>false</c>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="key"/> is <c>null</c>.
        /// </exception>
        public bool ContainsKey(string section, string key)
        {
            return TryGetKey(section, key, out _);
        }

        /// <summary>
        /// Reads a string associated with the specified section and key from the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be returned if the specified entry is not found.
        /// </param>
        /// <param name="expandVariables">
        /// When <c>true</c>, environment variables and pseudo‑variables
        /// (<c>%TEMP%</c>, <c>%USERPROFILE%</c>, <c>%RANDOM%</c>, <c>%DATE%</c>,
        /// <c>%TIME%</c>, <c>%CD%</c>, <c>%__CD__%</c>, <c>%CMDCMDLINE%</c>,
        /// <c>%__APPDIR__%</c>, <c>%0</c>, <c>%1</c>..<c>%9</c>, <c>%*</c>) in the value
        /// are replaced with their runtime values. Escape sequences are <b>not</b>
        /// processed in this mode: the expanded text comes from the environment, not
        /// from the INI file, so backslashes that arrive from <c>%TEMP%</c>,
        /// <c>%USERPROFILE%</c>, etc. are not reinterpreted as INI escapes
        /// (e.g. <c>"\app"</c> stays <c>"\app"</c> and does not become <c>BEL + "pp"</c>).
        /// </param>
        /// <returns>
        /// Read value. If the key is not found, <paramref name="defaultValue"/> is returned.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public string ReadString(string section, string key, string defaultValue = "", bool expandVariables = false)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            string value = GetValue(section, key, defaultValue);

            return expandVariables 
                ? ExpandVariables(value) 
                : _allowEscapeChars 
                    ? UnEscape(value) 
                    : value;
        }

        /// <summary>
        /// Reads a string associated with the specified section and key from the INI file
        /// and expands environment variables (e.g., %RANDOM%, %DATE%, %CD%, %0%, etc.).
        /// </summary>
        /// <remarks>
        /// <para>Supported pseudo‑variables (emulating CMD dynamic variables):</para>
        /// <list type="table">
        ///   <listheader>
        ///     <term>Variable</term>
        ///     <description>Replacement</description>
        ///   </listheader>
        ///   <item>
        ///     <term><c>%RANDOM%</c></term>
        ///     <description>Random 32‑bit unsigned integer (e.g., 1234567890)</description>
        ///   </item>
        ///   <item>
        ///     <term><c>%DATE%</c></term>
        ///     <description>Current date in <c>yyyyMMdd</c> format (e.g., 20260909)</description>
        ///   </item>
        ///   <item>
        ///     <term><c>%TIME%</c></term>
        ///     <description>Current time in <c>HHmmss</c> format (e.g., 143022)</description>
        ///   </item>
        ///   <item>
        ///     <term><c>%CD%</c></term>
        ///     <description>Current working directory (no trailing backslash)</description>
        ///   </item>
        ///   <item>
        ///     <term><c>%__CD__%</c></term>
        ///     <description>Current working directory with trailing backslash</description>
        ///   </item>
        ///   <item>
        ///     <term><c>%CMDCMDLINE%</c></term>
        ///     <description>Full command line of the current process</description>
        ///   </item>
        ///   <item>
        ///     <term><c>%__APPDIR__%</c></term>
        ///     <description>Directory of the executable file with trailing backslash</description>
        ///   </item>
        ///   <item>
        ///     <term><c>%0</c></term>
        ///     <description>Full path to the executable file (like <c>%0</c> in batch)</description>
        ///   </item>
        ///   <item>
        ///     <term><c>%1</c> … <c>%9</c></term>
        ///     <description>Command‑line arguments (missing arguments become empty string)</description>
        ///   </item>
        ///   <item>
        ///     <term><c>%*</c></term>
        ///     <description>All command‑line arguments (from <c>%1%</c> onward), joined with spaces</description>
        ///   </item>
        /// </list>
        /// <para>Standard environment variables (e.g., <c>%TEMP%</c>, <c>%USERPROFILE%</c>) are expanded as well.</para>
        /// <para>If expansion fails (e.g., invalid path), the original value is preserved.</para>
        /// </remarks>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be returned if the specified entry is not found.
        /// </param>
        /// <returns>
        /// The expanded string value. If the key is not found, <paramref name="defaultValue"/> is returned.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        [Obsolete("Use ReadString(section, key, defaultValue, expandVariables: true) instead.")]
        public string ReadExpandedString(string section, string key, string defaultValue = "")
        {
            return ReadString(section, key, defaultValue, expandVariables: true);
        }

        /// <summary>
        /// Reads a JSON string associated with the specified section and key from the INI file
        /// without removing outer curly braces or wrapping/unwrapping multiline values.
        /// </summary>
        /// <param name="section">Section name. Pass null to get global entries above all sections.</param>
        /// <param name="key">Key name.</param>
        /// <param name="defaultValue">The value to be returned if the specified entry is not found.</param>
        /// <returns>The raw JSON string as stored in the INI file, or <paramref name="defaultValue"/> if not found.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null.</exception>
        public string ReadJsonString(string section, string key, string defaultValue = null)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            return GetValue(section, key, defaultValue);
        }

        /// <summary>
        /// Reads the raw JSON fragment located at the specified path inside the
        /// JSON entry, preserving its original formatting (whitespace, comments,
        /// line breaks).
        /// </summary>
        /// <param name="section">Section name. Pass <c>null</c> for global entries.</param>
        /// <param name="key">Key name.</param>
        /// <param name="path">
        /// A slash- or backslash-separated path to the desired value inside the
        /// JSON structure, e.g. <c>"root/nested/number"</c>. Array elements are
        /// addressed by their numeric index, which may be written in decimal,
        /// hexadecimal, octal, or binary notation (e.g. <c>"items/0x2/name"</c>).
        /// </param>
        /// <param name="defaultValue">
        /// The value returned if the entry is missing, the JSON is invalid, or
        /// the path cannot be resolved.
        /// </param>
        /// <returns>
        /// The raw JSON fragment found at the specified path (with its original
        /// formatting), or <paramref name="defaultValue"/> if not found.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="key"/> or <paramref name="path"/> is <c>null</c>.
        /// </exception>
        public string ReadJsonString(
            string section,
            string key,
            string path,
            string defaultValue = null)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            string json = GetValue(section, key, defaultValue);
            if (json == null) return null;

            try
            {
                string[] segments = GetPathSegments(path);
                if (segments.Length == 0)
                    return defaultValue;

                MatchCollection matches = _jsonRegex.Matches(json);

                if (!TryNavigateJsonPathRaw(matches, segments, out int start, out int length))
                    return defaultValue;

                return json.Substring(start, length);
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Reads a string associated with the specified section and key from the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be returned if the specified entry is not found.
        /// </param>
        /// <param name="args">
        /// An object array that contains zero or more objects to format.
        /// </param>
        /// <returns>
        /// Read value.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public string FormatString(string section, string key, string defaultValue = "", params object[] args)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            string format = ReadString(section, key, defaultValue);
            return format == null ? null : string.Format(_culture, format, args);
        }

        /// <summary>
        /// Reads an array of strings associated with the specified section and key from the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="defaultValues">
        /// The values to be returned if the specified entry is not found.
        /// </param>
        /// <returns>
        /// Read value.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when the parameter <paramref name="key"/> is null.
        /// </exception>
        public string[] ReadStrings(string section, string key, params string[] defaultValues)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            // Retrieve the array of strings associated with the given section and key.
            string[] values = GetValues(section, key).ToArray();

            if (_allowEscapeChars)
                for (int i = 0; i < values.Length; i++)
                    values[i] = UnEscape(values[i]);

            // If no strings are found and default values are provided, use the default values.
            if (values.Length == 0 && defaultValues?.Length > 0)
                values = defaultValues;

            // Return the array of strings.
            return values;
        }

        /// <summary>
        /// Reads a byte array associated with the specified section and key.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass <c>null</c> for global entries.
        /// </param>
        /// <param name="key">
        /// Key name. Cannot be <c>null</c>.
        /// </param>
        /// <param name="encoding">
        /// How the byte array is encoded in the file: <see cref="IniByteEncoding.Hexadecimal"/>
        /// (space-separated hex pairs, the default) or <see cref="IniByteEncoding.Base64"/>.
        /// </param>
        /// <param name="defaultValue">
        /// The value returned if the key is missing or the stored string cannot be
        /// decoded with the chosen <paramref name="encoding"/>.
        /// </param>
        /// <returns>
        /// The decoded byte array, an empty array if the key is present but has an empty
        /// value, or <paramref name="defaultValue"/> if the key is missing or decoding fails.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="key"/> is <c>null</c>.
        /// </exception>
        public byte[] ReadBytes(string section, string key, IniByteEncoding encoding = IniByteEncoding.Hexadecimal, 
                                params byte[] defaultValue)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            string value = GetValue(section, key);
            if (value == null)
                return defaultValue;

            // A present-but-empty value denotes an empty array.
            if (value.Length == 0)
                return Array.Empty<byte>();

            try
            {
                switch (encoding)
                {
                    case IniByteEncoding.Hexadecimal:
                        return FromHexString(value) ?? defaultValue;

                    case IniByteEncoding.Base64:
                        return Convert.FromBase64String(value);

                    default:
                        return defaultValue;
                }
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Reads a character array associated with the specified section and key.
        /// The value is stored as a plain string; no escaping or encoding is applied
        /// beyond the usual INI value processing.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass <c>null</c> for global entries.
        /// </param>
        /// <param name="key">
        /// Key name. Cannot be <c>null</c>.
        /// </param>
        /// <param name="defaultValue">
        /// The value returned if the key is missing.
        /// </param>
        /// <returns>
        /// The characters of the stored value, or <paramref name="defaultValue"/> if
        /// the key is not found.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="key"/> is <c>null</c>.
        /// </exception>
        public char[] ReadChars(string section, string key, params char[] defaultValue)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            string value = ReadString(section, key, null);
            return value?.ToCharArray() ?? defaultValue;
        }

        /// <summary>
        /// Reads a value associated with the specified section and key from the ini file and converts it to the specified type.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="type">
        /// The desired value type.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be returned if the specified entry is not found.
        /// </param>
        /// <param name="converter">
        /// A type converter used to convert a value. If it is null, the default converter will be used.
        /// </param>
        /// <returns>
        /// Read value.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when one of the parameters <paramref name="key"/> or <paramref name="type"/> is null.
        /// </exception>
        public object ReadObject(string section, string key, Type type,
            object defaultValue = default, TypeConverter converter = null)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (type == null)
                throw new ArgumentNullException(nameof(type));

            // Attempt to read the string value from the ini file for the given section and key.
            string value = ReadString(section, key, null);


            // If a value is found and can be converted from string, convert it and return.
            if (value != null)
            {
                // Try JSON deserialize.
                if (type == typeof(ExpandoObject) || type == typeof(DynamicObject))
                    return ReadJsonDynamicObject(section, key, value ?? defaultValue);

                bool empty = value.Length == 0;

                // If the desired type is string, return the value directly.
                if (type == typeof(string))
                    return value;

                // If the desired type is boolean, use the common numeric conversion.
                if (type == typeof(bool))
                {
                    // Flag mode.
                    if (empty)
                        return true;

                    // Try numeric conversion.
                    object number = ParseNumber(value, typeof(int), _culture);

                    if (number != null)
                        return (int)number != 0;

                    // Try named boolean values.
                    if (_trueValues.Contains(value))
                        return true;

                    if (_falseValues.Contains(value))
                        return false;
                }

                // First char.
                else if (type == typeof(char) && !empty)
                    return value[0];

                // Primitive numeric types use the common extended numeric conversion.
                else if (type.IsPrimitive)
                {
                    object result = ParseNumber(value, type, _culture);

                    if (result != null)
                        return result;
                }

                // Enumerations use names first, then the common numeric conversion.
                else if (type.IsEnum)
                {
                    try
                    {
                        bool ignoreCase = IsIgnoreCase(_comparison);
                        object result = ParseEnum(value, type, ignoreCase, _culture);

                        if (result != null)
                            return result;
                    }
                    catch
                    {
                        // If parsing fails, continue with the regular converter.
                    }
                }

                // If no converter is provided, use the default converter for the specified type.
                if (converter == null)
                    converter = TypeDescriptor.GetConverter(type);

                if (converter.CanConvertFrom(typeof(string)))
                {
                    try
                    {
                        return converter.ConvertFromString(null, _culture, value);
                    }
                    catch
                    {
                        // If conversion fails, process the default value.
                    }
                }
            }

            // If a default value is provided and needs conversion, convert it to the desired type.
            if (defaultValue != null && defaultValue.GetType() != type)
            {
                if (converter == null)
                    converter = TypeDescriptor.GetConverter(type);

                if (converter.CanConvertFrom(defaultValue.GetType()))
                {
                    try
                    {
                        defaultValue = converter.ConvertFrom(null, _culture, defaultValue);
                    }
                    catch
                    {
                        defaultValue = null;
                    }
                }
            }

            // Return the default value if the conversion is not possible.
            return defaultValue;
        }


        /// <summary>
        /// Reads a JSON value from the specified section and key, and returns it as an object.
        /// The returned object can be a primitive (string, bool, double), an array (object[]),
        /// or a dictionary (IDictionary&lt;string, object&gt;) for JSON objects.
        /// </summary>
        /// <param name="section">Section name. Pass <c>null</c> for global entries.</param>
        /// <param name="key">Key name.</param>
        /// <param name="defaultValue">Default object returned if entry not found or JSON invalid.</param>
        /// <returns>An object representing the JSON, or <paramref name="defaultValue"/> if not found.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <c>null</c>.</exception>
        public object ReadJsonObject(string section, string key, object defaultValue = null)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            string json = GetValue(section, key);
            if (json == null)
                return defaultValue;

            try
            {
                object result = ParseJson(json);
                return result ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Reads a value at the specified path inside the JSON entry and returns it
        /// as a plain object. The returned object can be a primitive (string, bool,
        /// double, null), an array (<c>object[]</c>), or a dictionary
        /// (<see cref="IDictionary{TKey,TValue}"/> of <c>string</c> to <c>object</c>)
        /// for JSON objects.
        /// </summary>
        /// <param name="section">Section name. Pass <c>null</c> for global entries.</param>
        /// <param name="key">Key name.</param>
        /// <param name="path">
        /// A slash- or backslash-separated path to the desired value inside the JSON
        /// structure, e.g. <c>"root/nested/number"</c>. Array elements are addressed
        /// by their numeric index, which may be written in decimal, hexadecimal,
        /// octal, or binary notation (e.g. <c>"items/0x2/name"</c>).
        /// </param>
        /// <param name="defaultValue">
        /// The value returned if the entry is missing, the JSON is invalid, or the
        /// path cannot be resolved.
        /// </param>
        /// <returns>
        /// The value found at the specified path, or <paramref name="defaultValue"/>
        /// if not found.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="key"/> or <paramref name="path"/> is <c>null</c>.
        /// </exception>
        public object ReadJsonObject(string section, string key, string path, object defaultValue = null)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            string[] segments = GetPathSegments(path);
            if (segments.Length == 0)
                return defaultValue;

            string json = GetValue(section, key);
            if (json == null)
                return defaultValue;

            try
            {
                object root = ParseJson(json);
                if (root == null)
                    return defaultValue;

                if (TryNavigateJsonPath(root, segments, out object value))
                    return value ?? defaultValue;

                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Reads a JSON value from the specified section and key, and returns it as an dynamic object.
        /// </summary>
        /// <param name="section">Section name. Pass <c>null</c> for global entries.</param>
        /// <param name="key">Key name.</param>
        /// <param name="defaultValue">Default object returned if entry not found or JSON invalid.</param>
        /// <returns>An object representing the JSON, or <paramref name="defaultValue"/> if not found.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <c>null</c>.</exception>
        public dynamic ReadJsonDynamicObject(string section, string key, dynamic defaultValue = null)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            string json = GetValue(section, key);
            if (json == null)
                return defaultValue;

            try
            {
                object result = ParseJson(json);
                if (result == null) return defaultValue;
                if (result is IDictionary<string, object> dict)
                    return ConvertToExpando(dict);
                if (result is object[] arr)
                    return ConvertArray(arr);
                return result;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Reads a value from a JSON entry at the specified path inside the JSON structure.
        /// </summary>
        /// <param name="section">Section name. Pass <c>null</c> for global entries.</param>
        /// <param name="key">Key name.</param>
        /// <param name="path">
        /// A slash- or backslash-separated path to the desired value inside the JSON
        /// structure, e.g. <c>"root/nested/number"</c>.
        /// Array elements are addressed by their numeric index.
        /// </param>
        /// <param name="defaultValue">
        /// The value returned if the entry is missing, the JSON is invalid,
        /// or the path cannot be resolved.
        /// </param>
        /// <returns>
        /// The value found at the specified path, or <paramref name="defaultValue"/>
        /// if not found.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="key"/> or <paramref name="path"/> is <c>null</c>.
        /// </exception>
        public dynamic ReadJsonDynamicObject(string section, string key, string path, dynamic defaultValue = null)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            string[] segments = GetPathSegments(path);
            if (segments.Length == 0)
                return defaultValue;

            string json = GetValue(section, key);
            if (json == null)
                return defaultValue;

            try
            {
                object root = ParseJson(json);
                if (root == null)
                    return defaultValue;

                if (!TryNavigateJsonPath(root, segments, out object value))
                    return defaultValue;

                if (value == null)
                    return defaultValue;

                if (value is IDictionary<string, object> dict)
                    return ConvertToExpando(dict);

                if (value is object[] array)
                    return ConvertArray(array);

                return value;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Reads a value associated with the specified section and key from the INI file
        /// and converts it to the specified type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">
        /// The desired value type.
        /// </typeparam>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be returned if the specified entry is not found.
        /// </param>
        /// <param name="converter">
        /// A type converter used to convert a value. If it is null, the default converter will be used.
        /// </param>
        /// <returns>
        /// Read value.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when the parameter <paramref name="key"/> is null.
        /// </exception>
        public T Read<T>(string section, string key, T defaultValue = default, TypeConverter converter = null)
        {
            Type type = typeof(T);

            return (T) ReadObject(section, key, type, defaultValue, converter);
        }

        /// <summary>
        /// Reads values associated with the specified section and key from the INI file
        /// and converts them to the specified type of array elements.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="elementType">
        /// The desired value type of the array elements.
        /// </param>
        /// <param name="converter">
        /// A type converter used to convert values. If it is null, the default converter will be used.
        /// </param>
        /// <returns>
        /// An array of the read values converted to the specified type.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when one of the parameters <paramref name="key"/> or <paramref name="elementType"/> is null.
        /// </exception>
        public Array ReadArray(string section, string key, Type elementType, TypeConverter converter = null)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (elementType == null)
                throw new ArgumentNullException(nameof(elementType));

            // If the element type is char, return the value as char array.
            if (elementType == typeof(char))
            {
                string value = ReadString(section, key, string.Empty);
                return value.ToCharArray();
            }

            // If the element type is byte, return the value decoded with base64.
            if (elementType == typeof(byte))
            {
                string value = ReadString(section, key, string.Empty);
                return FromHexString(value) ?? Array.Empty<byte>();
            }

            // Retrieve the array of string values associated with the given section and key.
            string[] values = ReadStrings(section, key);

            // If the element type is string, return the values directly.
            if (elementType == typeof(string))
                return values;

            // Create an array of the specified element type with the same length as the values array.
            Array array = Array.CreateInstance(elementType, values.Length);

            // Iterate through each value, convert it, and set it in the array.
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i];
                // Use the provided converter or get the default converter for the element type.
                TypeConverter tmpConv = converter ?? TypeDescriptor.GetConverter(elementType);

                // Check if the conversion from string is possible and set the value in the array.
                if (tmpConv.CanConvertFrom(typeof(string)))
                    try
                    {
                        var item = tmpConv.ConvertFromString(null, _culture, value);
                        array.SetValue(item, i);
                    }
                    catch
                    {
                        continue; // If conversion fails just skip iteration. 
                    }
            }

            return array;
        }

        /// <summary>
        /// Reads the property value associated with the specified section and key from the INI file
        /// and sets it on the given object.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to set global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="property">
        /// Property to initialize.
        /// </param>
        /// <param name="obj">
        /// The object whose property value will be set.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be used if the specified entry is not found.
        /// </param>
        /// <param name="converter">
        /// A type converter used to convert values. If it is null, the default converter will be used.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when one of the parameters <paramref name="key"/> or <paramref name="property"/> is null.
        /// </exception>
        public void ReadProperty(string section, string key, PropertyInfo property,
            object obj, object defaultValue = null, TypeConverter converter = null)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (property == null)
                throw new ArgumentNullException(nameof(property));

            // Determine the type of the property.
            Type propertyType = property.PropertyType;

            if (propertyType == typeof(string))
            {
                bool expanded = property.GetCustomAttributes(typeof(IniExpandedAttribute)).Any();
                string value = ReadString(section, key, defaultValue as string, expanded);
                property.SetValue(obj, value, null);

                return;
            }

            // Check if the property type is an array.
            if (propertyType.IsArray)
            {
                // Get the element type of the array and type converter.
                Type elementType = propertyType.GetElementType();

                if (converter == null)
                    converter = TypeDescriptor.GetConverter(elementType);

                // Read the array from the INI file
                Array array = ReadArray(section, key, elementType, converter);

                // If no values are found and a default array is provided, use it.
                if (array.Length == 0 && defaultValue is Array a && a.GetType().GetElementType() == elementType)
                    array = a;

                // Set the array value to the property
                try
                {
                    property.SetValue(obj, array, null);
                }
                catch
                {
                    return; // If fails do not set the value.
                }
            }
            else
            {
                if (converter == null)
                    converter = TypeDescriptor.GetConverter(propertyType);

                // Read a single object value from the INI file.
                object value = property.IsDefined(typeof(DynamicAttribute), false) 
                    ? ReadJsonDynamicObject(section, key, defaultValue) 
                    : ReadObject(section, key, propertyType, defaultValue, converter);

                // If the value is not null, set it to the property.
                if (value != null)
                    try
                    {
                        property.SetValue(obj, value, null);
                    }
                    catch
                    {
                        return; // If fails do not set the value.
                    }
            }
        }

        /// <summary>
        /// Reads the value of a property from the INI file and sets it on the given object.
        /// </summary>
        /// <param name="property">
        /// Property to initialize.
        /// </param>
        /// <param name="obj">
        /// The object whose property value will be set.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be used if the specified entry is not found.
        /// </param>
        /// <param name="converter">
        /// A type converter used to convert values. If it is null, the default converter will be used.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when the parameter <paramref name="property"/> is null.
        /// </exception>
        public void ReadProperty(PropertyInfo property, object obj, TypeConverter converter = null)
        {
            if (property == null)
                throw new ArgumentNullException(nameof(property));

            // Skip properties marked with [IniIgnore]
            if (property.GetCustomAttributes(typeof(IniIgnoreAttribute), false).Length > 0)
                return;

            // Determine the section name for the INI file entry.
            // If no custom section is specified on the property, use the declaring type name as the default section name.
            Type declaringType = property.DeclaringType;


            object defaultValue = property.GetCustomAttributes(typeof(DefaultValueAttribute), false).FirstOrDefault() is
                DefaultValueAttribute defaultValueAttribute
                ? defaultValueAttribute.Value
                : null;

            if (defaultValue != null && defaultValue.GetType() != declaringType && declaringType.IsPrimitive)
                defaultValue = ConvertPrimitive(defaultValue, declaringType, _culture);

            string section = property.GetCustomAttributes(typeof(IniSectionAttribute), false)
                                     .FirstOrDefault() is IniSectionAttribute propertySectionAttribute
                             && !propertySectionAttribute.IsDefaultAttribute()
                                    ? propertySectionAttribute.Name
                                    : declaringType?.GetCustomAttributes(typeof(IniSectionAttribute), false)
                                    .FirstOrDefault() is IniSectionAttribute declaringTypeSectionAttribute
                                      && !declaringTypeSectionAttribute.IsDefaultAttribute()
                                        ? declaringTypeSectionAttribute.Name
                                        : GetDeclaringPath(declaringType);

            // Determine the key name for the INI file entry.
            // If no custom key name is specified, use the property name as the default key.
            string key = property.GetCustomAttributes(typeof(IniEntryAttribute), false)
                .FirstOrDefault() is IniEntryAttribute propertyEntryAttribute && !propertyEntryAttribute.IsDefaultAttribute()
                ? propertyEntryAttribute.Name
                : property.Name;

            // Read the property value from the INI file using the provided section and key names.
            ReadProperty(section, key, property, obj, defaultValue, converter);
        }

        /// <summary>
        /// Reads a values associated with the specified section and key from the ini file
        /// and converts it to the specified type.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="converter">
        /// A type converter used to convert a values. If it is null, the default converter will be used.
        /// </param>
        /// <returns>
        /// Read value.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public T[] ReadArray<T>(string section, string key, TypeConverter converter = null)
        {
            return (T[])ReadArray(section, key, typeof(T), converter);
        }

        /// <summary>
        /// Reads a boolean value associated with the specified section and key from the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be returned if the specified entry is not found.
        /// </param>
        /// <returns>
        /// Read value.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public bool ReadBoolean(string section, string key, bool defaultValue = default)
        {
            string value = ReadString(section, key, null);
            if (value == null)
                return defaultValue;

            // Flag mode.
            if (value == string.Empty)
                return true;

            // Try numeric conversion.
            object number = ParseNumber(value, typeof(int), _culture);

            if (number != null)
                return (int)number != 0;

            // Try named boolean values.
            if (_trueValues.Contains(value))
                return true;

            if (_falseValues.Contains(value))
                return false;

            return defaultValue;
        }

        /// <summary>
        /// Reads a character associated with the specified section and key from the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be returned if the specified entry is not found.
        /// </param>
        /// <returns>
        /// Read value.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public char ReadChar(string section, string key, char defaultValue = default)
        {
            string value = ReadString(section, key, null);
            if (string.IsNullOrEmpty(value))
                return defaultValue;
            return value[0];
        }

        /// <summary>
        /// Reads a signed byte associated with the specified section and key from the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be returned if the specified entry is not found.
        /// </param>
        /// <returns>
        /// Read value.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public sbyte ReadSByte(string section, string key, sbyte defaultValue = default)
        {
            return Read(section, key, defaultValue);
        }

        /// <summary>
        /// Reads an unsigned byte associated with the specified section and key from the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be returned if the specified entry is not found.
        /// </param>
        /// <returns>
        /// Read value.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public byte ReadByte(string section, string key, byte defaultValue = default)
        {
            return Read(section, key, defaultValue);
        }

        /// <summary>
        /// Reads a 16-bit integer associated with the specified section and key from the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be returned if the specified entry is not found.
        /// </param>
        /// <returns>
        /// Read value.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public short ReadInt16(string section, string key, short defaultValue = default)
        {
            return Read(section, key, defaultValue);
        }

        /// <summary>
        /// Reads an unsigned 16-bit integer associated with the specified section and key from the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be returned if the specified entry is not found.
        /// </param>
        /// <returns>
        /// Read value.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public ushort ReadUInt16(string section, string key, ushort defaultValue = default)
        {
            return Read(section, key, defaultValue);
        }

        /// <summary>
        /// Reads a 32-bit integer associated with the specified section and key from the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be returned if the specified entry is not found.
        /// </param>
        /// <returns>
        /// Read value.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public int ReadInt32(string section, string key, int defaultValue = default)
        {
            return Read(section, key, defaultValue);
        }

        /// <summary>
        /// Reads an unsigned 32-bit integer associated with the specified section and key from the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be returned if the specified entry is not found.
        /// </param>
        /// <returns>
        /// Read value.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public uint ReadUInt32(string section, string key, uint defaultValue = default)
        {
            return Read(section, key, defaultValue);
        }

        /// <summary>
        /// Reads a 64-bit integer associated with the specified section and key from the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be returned if the specified entry is not found.
        /// </param>
        /// <returns>
        /// Read value.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public long ReadInt64(string section, string key, long defaultValue = default)
        {
            return Read(section, key, defaultValue);
        }

        /// <summary>
        /// Reads an unsigned 64-bit integer associated with the specified section and key from the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be returned if the specified entry is not found.
        /// </param>
        /// <returns>
        /// Read value.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public ulong ReadUInt64(string section, string key, ulong defaultValue = default)
        {
            return Read(section, key, defaultValue);
        }

        /// <summary>
        /// Reads a 32-bit floating point value associated with the specified section and key from the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be returned if the specified entry is not found.
        /// </param>
        /// <returns>
        /// Read value.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public float ReadSingle(string section, string key, float defaultValue = default)
        {
            return Read(section, key, defaultValue);
        }

        /// <summary>
        /// Reads a 64-bit floating point value associated with the specified section and key from the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be returned if the specified entry is not found.
        /// </param>
        /// <returns>
        /// Read value.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public double ReadDouble(string section, string key, double defaultValue = default)
        {
            return Read(section, key, defaultValue);
        }

        /// <summary>
        /// Reads a decimal value associated with the specified section and key from the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be returned if the specified entry is not found.
        /// </param>
        /// <returns>
        /// Read value.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public decimal ReadDecimal(string section, string key, decimal defaultValue = default)
        {
            return Read(section, key, defaultValue);
        }

        /// <summary>
        /// Reads a <see cref="DateTime"/> value associated with the specified section and key from the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="defaultValue">
        /// The value to be returned if the specified entry is not found.
        /// </param>
        /// <returns>
        /// Read value.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public DateTime ReadDateTime(string section, string key, DateTime defaultValue = default)
        {
            return Read(section, key, defaultValue);
        }

        /// <summary>
        /// Reads a <see cref="DateTime"/> value associated with the specified section and key,
        /// using the given format string and culture provider.
        /// The value is parsed exactly according to the provided format.
        /// </summary>
        /// <param name="section">
        /// The section name. Pass null to read global entries that appear above all sections.
        /// </param>
        /// <param name="key">
        /// The key name.
        /// </param>
        /// <param name="format">
        /// A standard or custom date/time format string (e.g., <c>"yyyy-MM-dd HH:mm:ss"</c>).
        /// This format must exactly match the string stored in the INI file.
        /// </param>
        /// <param name="provider">
        /// An <see cref="IFormatProvider"/> that supplies culture-specific formatting information.
        /// If <c>null</c>, <see cref="CultureInfo.InvariantCulture"/> is used.
        /// </param>
        /// <param name="defaultValue">
        /// The value to return if the specified entry is not found in the INI file.
        /// </param>
        /// <returns>
        /// The parsed <see cref="DateTime"/> value. If the key does not exist, <paramref name="defaultValue"/> is returned.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="key"/> or <paramref name="format"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="FormatException">
        /// Thrown when the stored string does not match the specified <paramref name="format"/>.
        /// </exception>
        public DateTime ReadDateTime(
            string section,
            string key,
            string format,
            IFormatProvider provider = null,
            DateTime defaultValue = default)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (format == null)
                throw new ArgumentNullException(nameof(format));

            string str = ReadString(section, key, null);
            if (str == null)
                return defaultValue;

            return DateTime.ParseExact(str, format, provider ?? _culture);
        }

        /// <summary>
        /// Reads a <see cref="DateTime"/> value associated with the specified section and key,
        /// using the given culture provider and the standard date/time format of that culture.
        /// </summary>
        /// <param name="section">
        /// The section name. Pass <c>null</c> to read global entries that appear above all sections.
        /// </param>
        /// <param name="key">
        /// The key name. Cannot be <c>null</c>.
        /// </param>
        /// <param name="provider">
        /// An <see cref="IFormatProvider"/> that supplies culture-specific formatting information.
        /// This provider determines the expected format of the stored string.
        /// </param>
        /// <param name="defaultValue">
        /// The value to return if the specified entry is not found in the INI file.
        /// </param>
        /// <returns>
        /// The parsed <see cref="DateTime"/> value. If the key does not exist, <paramref name="defaultValue"/> is returned.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="key"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="FormatException">
        /// Thrown when the stored string cannot be parsed using the standard format of the given culture.
        /// </exception>
        public DateTime ReadDateTime(
            string section,
            string key,
            IFormatProvider provider,
            DateTime defaultValue = default)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            string str = ReadString(section, key, null);
            if (str == null)
                return defaultValue;

            return DateTime.Parse(str, provider ?? _culture);
        }

        /// <summary>
        /// Reads a <see cref="DateTime"/> value associated with the specified section and key,
        /// using the given format, culture provider, and <see cref="DateTimeStyles"/>.
        /// </summary>
        /// <param name="section">
        /// The section name. Pass <c>null</c> to read global entries that appear above all sections.
        /// </param>
        /// <param name="key">
        /// The key name. Cannot be <c>null</c>.
        /// </param>
        /// <param name="format">
        /// A standard or custom date/time format string (e.g., <c>"dd/MM/yyyy"</c>).
        /// This format must exactly match the string stored in the INI file.
        /// </param>
        /// <param name="provider">
        /// An <see cref="IFormatProvider"/> that supplies culture-specific formatting information.
        /// If <c>null</c>, <see cref="CultureInfo.InvariantCulture"/> is used.
        /// </param>
        /// <param name="styles">
        /// A combination of <see cref="DateTimeStyles"/> values that define the parsing behaviour
        /// (e.g., <see cref="DateTimeStyles.AllowWhiteSpaces"/>).
        /// </param>
        /// <param name="defaultValue">
        /// The value to return if the specified entry is not found in the INI file.
        /// </param>
        /// <returns>
        /// The parsed <see cref="DateTime"/> value. If the key does not exist, <paramref name="defaultValue"/> is returned.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="key"/> or <paramref name="format"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="FormatException">
        /// Thrown when the stored string does not match the specified <paramref name="format"/>
        /// or cannot be parsed according to the given <paramref name="styles"/>.
        /// </exception>
        public DateTime ReadDateTime(
            string section,
            string key,
            string format,
            IFormatProvider provider,
            DateTimeStyles styles,
            DateTime defaultValue = default)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (format == null)
                throw new ArgumentNullException(nameof(format));

            string str = ReadString(section, key, null);
            if (str == null)
                return defaultValue;

            return DateTime.ParseExact(str, format, provider ?? _culture, styles);
        }

        #endregion

        #region Public write methods

        /// <summary>
        /// Imports the contents of the specified dictionary into this INI file.
        /// The dictionary format matches the one produced by
        /// <see cref="ExportToDictionary"/>: section name → key → list of values.
        /// </summary>
        /// <param name="data">
        /// The dictionary to import. An empty string as the outer key represents
        /// global entries located above all named sections. A null value of the
        /// inner dictionary is skipped. An empty list of values removes all
        /// occurrences of the corresponding key.
        /// </param>
        /// <param name="replace">
        /// When <c>false</c> (default), the data is merged into the existing content:
        /// existing keys are overwritten in place, new keys and sections are appended.
        /// When <c>true</c>, the current content is cleared first, and the imported
        /// data becomes the entire file.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="data"/> is <c>null</c>.
        /// </exception>
        public void ImportFromDictionary(
            IDictionary<string, Dictionary<string, List<string>>> data,
            bool replace = false)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (replace)
                Content = string.Empty;

            foreach (KeyValuePair<string, Dictionary<string, List<string>>> sectionPair in data)
            {
                // Empty string in ExportToDictionary means "global entries".
                string section = string.IsNullOrEmpty(sectionPair.Key) ? null : sectionPair.Key;
                Dictionary<string, List<string>> entries = sectionPair.Value;

                if (entries == null)
                    continue;

                foreach (KeyValuePair<string, List<string>> entryPair in entries)
                {
                    string key = entryPair.Key;
                    if (key == null)
                        continue;

                    List<string> values = entryPair.Value;

                    if (values == null || values.Count == 0)
                    {
                        // Empty list — remove every occurrence of this key.
                        RemoveKeys(section, key);
                    }
                    else
                    {
                        // Overwrite or append each value, preserving multi-value keys.
                        WriteStrings(section, key, values.ToArray());
                    }
                }
            }
        }

        /// <summary>
        /// Removes the first occurrence of the specified key in the given section from the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass <c>null</c> to remove global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="key"/> is <c>null</c>.
        /// </exception>
        public void RemoveKey(string section, string key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            SetValue(section, key);
        }

        /// <summary>
        /// Removes all occurrences of the specified key in the given section from the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass <c>null</c> to remove global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="key"/> is <c>null</c>.
        /// </exception>
        public void RemoveKeys(string section, string key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            SetValues(section, key); // empty params array removes all matching  entries.
        }

        /// <summary>
        /// Removes all sections with the specified name from the INI file.
        /// Preserves formatting and does not alter whitespace outside removed ranges.
        /// </summary>
        /// <param name="section">Section name to remove.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="section"/> is <c>null</c>.</exception>
        public void RemoveSection(string section)
        {
            // Handle global entries removal.
            bool emptySection = string.IsNullOrEmpty(section);
            StringBuilder sb = new StringBuilder(_content);
            if (emptySection)
            {
                int firstSectionIndex = -1;
                for (int i = 0; i < _matches.Count; i++)
                {
                    if (_matches[i].Groups["section"].Success)
                    {
                        firstSectionIndex = _matches[i].Index;
                        break;
                    }
                }

                if (firstSectionIndex < 0)
                {
                    // No sections at all - delete everything.
                    Content = string.Empty;
                }
                else
                {
                    // Remove all characters from the beginning up to the first section

                    sb.Remove(0, firstSectionIndex);
                    Content = sb.ToString();
                }
                return;
            }

            // For named sections.
            List<long> ranges = new List<long>(); // Packed ranges.
            int currentStart = -1; // Tracks the currently matched section.

            for (int i = 0; i < _matches.Count; i++)
            {
                Match match = _matches[i];

                if (match.Groups[_iniSection].Success)
                {
                    // Close previous range if any.
                    if (currentStart >= 0)
                    {
                        // High 32 bits = start, low 32 bits = end (exclusive).
                        uint currentEnd = (uint)match.Index;
                        ranges.Add(((long)currentStart << 32) | currentEnd);
                        currentStart = -1;
                    }

                    // Start new range if section matches.
                    if (match.Groups[_iniValue].Value.Equals(section, _comparison))
                        currentStart = match.Index;
                }
                // Entries are ignored - they're inside section ranges.
            }

            // Close last range if it extends to the end.
            if (currentStart >= 0)
                ranges.Add(((long)currentStart << 32) | (uint)_content.Length);

            if (ranges.Count == 0)
                return;

            // Remove from the end to preserve indices of remaining ranges.
            for (int i = ranges.Count - 1; i >= 0; i--)
            {
                // Unpack start and end.
                long packed = ranges[i];
                int start = unchecked((int)(packed >> 32));
                int end = unchecked((int)packed);
                sb.Remove(start, end - start);
            }

            Content = sb.ToString();
        }

        /// <summary>
        /// Writes a string associated with the specified section and key to the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to set global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="value">
        /// The value to be written.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public void WriteString(string section, string key, string value)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            // Prepare the value for writing.
            if (_allowEscapeChars)
                value = ToEscape(value);

            SetValue(section, key, value);
        }

        /// <summary>
        /// Writes a JSON string associated with the specified section and key to the INI file
        /// without adding outer curly braces or wrapping/unwrapping multiline values.
        /// </summary>
        /// <param name="section">Section name. Pass null to set global entries above all sections.</param>
        /// <param name="key">Key name.</param>
        /// <param name="value">The JSON string to be written.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null.</exception>
        public void WriteJsonString(string section, string key, string value)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            SetValue(section, key, value);
        }

        /// <summary>
        /// Writes a strings associated with the specified section and key to the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to set global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="values">
        /// The values to be written.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public void WriteStrings(string section, string key, params string[] values)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (values == null)
                throw new ArgumentNullException(nameof(values));

            if (_allowEscapeChars) values = TransformArray(values, ToEscape);

            SetValues(section, key, wrap: true, values);
        }

        /// <summary>
        /// Writes a byte array associated with the specified section and key.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass <c>null</c> for global entries.
        /// </param>
        /// <param name="key">
        /// Key name. Cannot be <c>null</c>.
        /// </param>
        /// <param name="value">
        /// The byte array to write. If <c>null</c>, the entry is removed.
        /// </param>
        /// <param name="encoding">
        /// How the byte array is encoded in the file: <see cref="IniByteEncoding.Hexadecimal"/>
        /// (space-separated hex pairs, the default) or <see cref="IniByteEncoding.Base64"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="key"/> is <c>null</c>.
        /// </exception>
        public void WriteBytes(string section, string key, byte[] value,
                               IniByteEncoding encoding = IniByteEncoding.Hexadecimal)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (value == null)
            {
                RemoveKey(section, key);
                return;
            }

            string str;
            switch (encoding)
            {
                case IniByteEncoding.Base64:
                    str = Convert.ToBase64String(value);
                    break;

                case IniByteEncoding.Hexadecimal:
                default:
                    str = ToHexString(value);
                    break;
            }

            WriteString(section, key, str);
        }

        /// <summary>
        /// Writes a character array associated with the specified section and key.
        /// The array is stored as a plain string; no escaping or encoding is applied
        /// beyond the usual INI value processing.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass <c>null</c> for global entries.
        /// </param>
        /// <param name="key">
        /// Key name. Cannot be <c>null</c>.
        /// </param>
        /// <param name="value">
        /// The characters to write. If <c>null</c>, the entry is removed.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="key"/> is <c>null</c>.
        /// </exception>
        public void WriteChars(string section, string key, char[] value)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (value == null)
            {
                RemoveKey(section, key);
                return;
            }

            WriteString(section, key, new string(value));
        }

        /// <summary>
        /// Writes a value associated with the specified section and key to the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to set global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="value">
        /// The value to be written.
        /// </param>
        /// <param name="converter">
        /// A type converter used to convert the value. If it is null, the default converter will be used.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when the parameter <paramref name="key"/> is null.
        /// </exception>
        public void WriteObject(string section, string key, object value, TypeConverter converter = null)
        {
            // Check if the key is null and throw an exception if it is.
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            // Initialize a string for the converted value
            string str = null;

            // If the value is not null, attempt to convert it to a string.
            if (value != null)
            {
                // Get the type of the value.
                Type type = value.GetType();

                if (value is string s)
                    str = s;

                // Try JSON deserialize.
                if (type == typeof(ExpandoObject) || type == typeof(DynamicObject))
                {
                    WriteJsonDynamicObject(section, key, value, true);
                }

                // Parse enum.
                else if (value != null && value.GetType().IsEnum)
                {
                    str = EnumToString(value);
                }

                // Convert primitive.
                else if (value is IConvertible conv)
                {
                    str = conv.ToString(_culture);
                }

                // Use the provided converter or get the default converter for the value type.
                else if ((converter ?? (converter = TypeDescriptor.GetConverter(type))).CanConvertTo(typeof(string)))
                {
                    try
                    {
                        // Convert the value to a string.
                        str = converter.ConvertToString(null, _culture, value);
                    }
                    catch
                    {
                        // If conversion fails, exit the method without writing.
                        return;
                    }
                }
            }

            // Write the converted string value to the INI file.
            WriteString(section, key, str);
        }

        /// <summary>
        /// Writes an object as JSON to the specified section and key.
        /// Supports IDictionary&lt;string, object&gt;, IEnumerable (non-string), and primitives.
        /// If <paramref name="value"/> is <c>null</c>, the entry is removed.
        /// </summary>
        /// <param name="section">Section name. Pass <c>null</c> for global entries.</param>
        /// <param name="key">Key name.</param>
        /// <param name="value">The object to serialize to JSON.</param>
        /// <param name="beautify">If <c>true</c>, formats JSON with indentation.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <c>null</c>.</exception>
        public void WriteJsonObject(string section, string key, object value, bool beautify = false)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (value == null)
            {
                SetValue(section, key, null);
                return;
            }

            string json = SerializeJson(value, beautify);
            SetValue(section, key, json);
        }

        /// <summary>
        /// Writes a value at the specified path inside the JSON entry without
        /// replacing the rest of the JSON structure. Missing intermediate objects
        /// are created automatically. If the JSON entry does not exist or cannot
        /// be parsed, a new JSON object is created starting at the given path.
        /// </summary>
        /// <param name="section">Section name. Pass <c>null</c> for global entries.</param>
        /// <param name="key">Key name.</param>
        /// <param name="path">
        /// A slash- or backslash-separated path to the location where the value
        /// should be stored, e.g. <c>"root/nested/number"</c>. Array elements are
        /// addressed by their numeric index, which may be written in decimal,
        /// hexadecimal, octal, or binary notation.
        /// </param>
        /// <param name="value">
        /// The value to store. Can be a primitive, an array, a dictionary, or <c>null</c>.
        /// </param>
        /// <param name="beautify">
        /// If <c>true</c>, formats the JSON with indentation.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="key"/> or <paramref name="path"/> is <c>null</c>.
        /// </exception>
        /// <remarks>
        /// If the path cannot be resolved (for example, a primitive value is
        /// encountered at an intermediate position, or an array index is out of
        /// range), the method performs no change.
        /// </remarks>
        public void WriteJsonObject(
            string section,
            string key,
            string path,
            object value,
            bool beautify = false)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            string[] segments = GetPathSegments(path);
            if (segments.Length == 0)
                return;

            // Read the existing JSON (if any) and parse it; on failure, start from
            // an empty dictionary root.
            string existing = GetValue(section, key);
            object root = null;

            if (existing != null)
            {
                try
                {
                    root = ParseJson(existing);
                }
                catch
                {
                    root = null;
                }
            }

            if (root == null)
                root = new Dictionary<string, object>(DefaultCapacity, GetComparer(_comparison));

            if (!TrySetJsonPathValue(root, segments, value))
                return;

            string json = SerializeJson(root, beautify);
            SetValue(section, key, json);
        }

        /// <summary>
        /// Writes a dynamic object as JSON to the specified section and key.
        /// The object can be any .NET object, ExpandoObject, or Dictionary.
        /// If <paramref name="value"/> is <c>null</c>, the entry is removed.
        /// </summary>
        /// <param name="section">Section name. Pass <c>null</c> for global entries.</param>
        /// <param name="key">Key name.</param>
        /// <param name="value">The dynamic object to serialize to JSON.</param>
        /// <param name="beautify">If <c>true</c>, formats JSON with indentation.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <c>null</c>.</exception>
        public void WriteJsonDynamicObject(string section, string key, dynamic value, bool beautify = false)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (value == null)
            {
                SetValue(section, key, null);
                return;
            }

            // Convert dynamic to a regular object for serialization.
            // If it's ExpandoObject, we need to convert to Dictionary.
            object obj = ConvertFromDynamic(value);
            string json = SerializeJson(obj, beautify);
            SetValue(section, key, json);
        }

        /// <summary>
        /// Writes a dynamic object at the specified path inside the JSON entry
        /// without replacing the rest of the JSON structure. Missing intermediate
        /// objects are created automatically.
        /// </summary>
        /// <param name="section">Section name. Pass <c>null</c> for global entries.</param>
        /// <param name="key">Key name.</param>
        /// <param name="path">
        /// A slash- or backslash-separated path to the location where the value
        /// should be stored.
        /// </param>
        /// <param name="value">
        /// The dynamic object to serialize. Can be an <c>ExpandoObject</c>,
        /// a custom <c>DynamicObject</c>, a dictionary, an array, or a primitive.
        /// </param>
        /// <param name="beautify">
        /// If <c>true</c>, formats the JSON with indentation.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="key"/> or <paramref name="path"/> is <c>null</c>.
        /// </exception>
        public void WriteJsonDynamicObject(
            string section,
            string key,
            string path,
            dynamic value,
            bool beautify = false)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            object boxed = ConvertFromDynamic(value);
            WriteJsonObject(section, key, path, boxed, beautify);
        }

        /// <summary>
        /// Writes a value associated with the specified section and key to the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to set global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="value">
        /// The value to be written.
        /// </param>
        /// <param name="converter">
        /// A type converter used to convert a value. If it is null, the default converter will be used.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public void Write<T>(string section, string key, T value, TypeConverter converter = null)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            WriteObject(section, key, value, converter);
        }

        /// <summary>
        /// Writes an array of values associated with the specified section and key to the INI file,
        /// converting each element to a string using the specified type converter.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="array">
        /// The array to be written.
        /// </param>
        /// <param name="converter">
        /// A type converter used to convert values. If it is null, the default converter will be used.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when one of the parameters <paramref name="key"/> or <paramref name="array"/> is null.
        /// </exception>
        public void WriteArray(string section, string key, Array array, TypeConverter converter = null)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            // Determine the type of the elements in the array.
            Type elementType = array.GetType().GetElementType();

            // If the element type is char, write the value as string.
            if (elementType == typeof(char))
            {
                char[] chars = (char[])array;
                WriteString(section, key, new string(chars));
                return;
            }

            // If the element type is byte, write the value encoded with base64.
            if (elementType == typeof(byte))
            {
                byte[] bytes = (byte[])array;
                string value = ToHexString(bytes) ?? string.Empty;
                WriteString(section, key, value);
                return;
            }

            // Use the provided converter or get the default converter for the element type.
            if (converter == null)
                converter = TypeDescriptor.GetConverter(elementType);

            // Get the length of the array
            int arrayLength = array.Length;

            // Create a string array to hold the converted values.
            string[] values = new string[arrayLength];

            // Iterate through each element in the array.
            for (int i = 0; i < arrayLength; i++)
            {
                object value = array.GetValue(i);
                try
                {
                    // Convert the value to a string using the converter.
                    values[i] = converter.ConvertToString(null, _culture, value);
                }
                catch
                {
                    // If conversion fails, set the value to null.
                    values[i] = null;
                }
            }

            // Write the converted string values to the INI file
            WriteStrings(section, key, values);
        }

        /// <summary>
        /// Writes a values associated with the specified section and key to the ini file.
        /// and converts it to the specified type.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to get global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="array">
        /// The array to be written.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when one of parameters <paramref name="key"/> or <paramref name="array"/> is null.
        /// </exception>
        public void WriteArray<T>(string section, string key, params T[] array)
        {
            WriteArray(section, key, (Array)array);
        }

        /// <summary>
        /// Writes the property value associated with the specified section and key to the ini file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to set global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="property">
        /// A property to write.
        /// </param>
        /// <param name="obj">
        /// The object whose property value will be get. Pass null for static property.
        /// </param>
        /// <param name="converter">
        /// A type converter used to convert values. If it is null, the default converter will be used.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when one of parameters <paramref name="key"/> or <paramref name="property"/> is null.
        /// </exception>
        public void WriteProperty(string section, string key, PropertyInfo property, object obj = null, TypeConverter converter = null)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (property == null)
                throw new ArgumentNullException(nameof(property));

            // Skip properties marked with [IniIgnore]
            if (property.GetCustomAttributes(typeof(IniIgnoreAttribute), false).Length > 0)
                return;

            object value = property.GetValue(obj, null);

            if (value is Array array)
                WriteArray(section, key, array, converter);
            else if (property.IsDefined(typeof(DynamicAttribute), false))
                WriteJsonDynamicObject(section, key, value);
            else
                WriteObject(section, key, value, converter);
        }

        /// <summary>
        /// Writes the value of a property to the ini file.
        /// </summary>
        /// <param name="property">
        /// The <see cref="PropertyInfo"/> object representing the property to write.
        /// </param>
        /// <param name="obj">
        /// An optional object instance from which to retrieve the property value. 
        /// If null, static properties are assumed.
        /// </param>
        /// <param name="converter">
        /// An optional <see cref="TypeConverter"/> used to convert the property value to a string.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when of parameter <paramref name="property"/> is null.
        /// </exception>
        public void WriteProperty(PropertyInfo property, object obj = null, TypeConverter converter = null)
        {
            if (property == null)
                throw new ArgumentNullException(nameof(property));

            // Determine the section name for the INI file entry.
            // If no custom section is specified on the property, use the declaring type name as the default section name.
            Type declaringType = property.DeclaringType;
            string section = property.GetCustomAttributes(typeof(IniSectionAttribute), false)
                                 .FirstOrDefault() is IniSectionAttribute propertySectionAttribute
                                 && !propertySectionAttribute.IsDefaultAttribute()
                                    ? propertySectionAttribute.Name
                                    : declaringType?.GetCustomAttributes(typeof(IniSectionAttribute), false)
                                    .FirstOrDefault() is IniSectionAttribute declaringTypeSectionAttribute
                                      && !declaringTypeSectionAttribute.IsDefaultAttribute()
                                        ? declaringTypeSectionAttribute.Name
                                        : GetDeclaringPath(declaringType);

            // Determine the key name for the INI file entry.
            // If no custom key name is specified, use the property name as the default key.
            string key = property.GetCustomAttributes(typeof(IniEntryAttribute), false)
                .FirstOrDefault() is IniEntryAttribute propertyEntryAttribute && !propertyEntryAttribute.IsDefaultAttribute()
                ? propertyEntryAttribute.Name
                : property.Name;

            // Write the property to the configuration using the determined section and key.
            WriteProperty(section, key, property, obj, converter);
        }


        /// <summary>
        /// Reads settings from the INI file and sets it to the specified type, including its nested types.
        /// </summary>
        /// <param name="type">The <see cref="Type"/> from which to read settings.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="type"/> is null.</exception>
        public void ReadSettings(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            // Retrieve all static properties of the given type
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            // Read settings for each property
            foreach (PropertyInfo property in properties)
            {
                ReadProperty(property, null);
            }

            // Get all nested types and recursively read settings for each
            Type[] nestedTypes = type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
            foreach (Type nestedType in nestedTypes)
            {
                ReadSettings(nestedType);
            }
        }

        /// <summary>
        /// Reads settings from the INI file and sets it to all types in the specified assembly.
        /// </summary>
        /// <param name="assembly">The <see cref="Assembly"/> containing the types to read settings from.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="assembly"/> is null.</exception>
        public void ReadSettings(Assembly assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));

            // Retrieve all types from the assembly and read settings for each
            Type[] types = assembly.GetTypes();
            foreach (Type type in types)
            {
                ReadSettings(type);
            }
        }

        /// <summary>
        /// Reads settings from the INI file and sets it to the specified object instance.
        /// </summary>
        /// <param name="obj">The object from which to read settings.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="obj"/> is null.</exception>
        public void ReadSettings(object obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            Type type = obj.GetType();

            // Retrieve all instance properties of the given object
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            // Read settings for each property
            foreach (PropertyInfo property in properties)
            {
                ReadProperty(property, obj);
            }
        }

        /// <summary>
        /// Writes settings from the specified type to the INI file, including its nested types.
        /// </summary>
        /// <param name="type">The <see cref="Type"/> for which to write settings.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="type"/> is null.</exception>
        public void WriteSettings(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            // Retrieve all static properties of the given type
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            // Write settings for each property
            foreach (PropertyInfo property in properties)
            {
                WriteProperty(property);
            }

            // Get all nested types and recursively write settings for each
            Type[] nestedTypes = type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
            foreach (Type nestedType in nestedTypes)
            {
                WriteSettings(nestedType);
            }
        }

        /// <summary>
        /// Writes settings from all types in the specified assembly to the INI file.
        /// </summary>
        /// <param name="assembly">The <see cref="Assembly"/> containing the types to write settings for.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="assembly"/> is null.</exception>
        public void WriteSettings(Assembly assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));

            // Retrieve all types from the assembly and write settings for each
            Type[] types = assembly.GetTypes();
            foreach (Type type in types)
            {
                WriteSettings(type);
            }
        }

        /// <summary>
        /// Writes settings from the specified object instance to the INI file.
        /// </summary>
        /// <param name="obj">The object for which to write settings.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="obj"/> is null.</exception>
        public void WriteSettings(object obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            Type type = obj.GetType();

            // Retrieve all instance properties of the given object
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            // Write settings for each property
            foreach (PropertyInfo property in properties)
            {
                WriteProperty(property, obj);
            }
        }

        /// <summary>
        /// Subscribes to property change notifications of the specified object and writes
        /// the changed property values to the INI file as they change.
        /// </summary>
        /// <param name="obj">
        /// The object implementing <see cref="INotifyPropertyChanged"/> whose property
        /// changes should be tracked.
        /// </param>
        /// <returns>
        /// An <see cref="IDisposable"/> that unsubscribes from the notifications when disposed.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="obj"/> is <c>null</c>.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Unlike <see cref="WriteSettings(object)"/>, this method does not write the
        /// current property values immediately. Only subsequent changes are persisted.
        /// To capture the initial state, call <see cref="WriteSettings(object)"/> before
        /// calling <see cref="WatchSettings(INotifyPropertyChanged)"/>.
        /// </para>
        /// <para>
        /// Properties marked with <see cref="IniIgnoreAttribute"/> are not tracked.
        /// When <see cref="INotifyPropertyChanged.PropertyChanged"/> is raised with an
        /// empty or <c>null</c> property name, all tracked properties are written.
        /// </para>
        /// <para>
        /// The watcher does not synchronise access to the INI file. If the source object
        /// raises <see cref="INotifyPropertyChanged.PropertyChanged"/> from multiple
        /// threads, the caller is responsible for serialising the writes.
        /// </para>
        /// </remarks>
        public IDisposable WatchSettings(INotifyPropertyChanged obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            return new SettingsWatcher(this, obj);
        }

        /// <summary>
        /// Writes a boolean value associated with the specified section and key to the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to set global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="value">
        /// The value to be written.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public void WriteBoolean(string section, string key, bool value)
        {
            Write(section, key, value);
        }

        /// <summary>
        /// Writes a character value associated with the specified section and key to the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to set global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="value">
        /// The value to be written.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public void WriteChar(string section, string key, char value)
        {
            Write(section, key, value);
        }

        /// <summary>
        /// Writes a signed byte associated with the specified section and key to the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to set global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="value">
        /// The value to be written.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public void WriteSByte(string section, string key, sbyte value)
        {
            Write(section, key, value);
        }

        /// <summary>
        /// Writes an unsigned byte associated with the specified section and key to the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to set global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="value">
        /// The value to be written.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public void WriteByte(string section, string key, byte value)
        {
            Write(section, key, value);
        }

        /// <summary>
        /// Writes a signed 16-bit integer associated with the specified section and key to the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to set global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="value">
        /// The value to be written.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public void WriteInt16(string section, string key, short value)
        {
            Write(section, key, value);
        }

        /// <summary>
        /// Writes an unsigned 16-bit integer associated with the specified section and key to the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to set global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="value">
        /// The value to be written.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public void WriteUInt16(string section, string key, ushort value)
        {
            Write(section, key, value);
        }

        /// <summary>
        /// Writes a signed 32-bit integer associated with the specified section and key to the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to set global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="value">
        /// The value to be written.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public void WriteInt32(string section, string key, int value)
        {
            Write(section, key, value);
        }

        /// <summary>
        /// Writes an unsigned 32-bit integer associated with the specified section and key to the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to set global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="value">
        /// The value to be written.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public void WriteUInt32(string section, string key, uint value)
        {
            Write(section, key, value);
        }

        /// <summary>
        /// Writes a signed 64-bit integer associated with the specified section and key to the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to set global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="value">
        /// The value to be written.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public void WriteInt64(string section, string key, long value)
        {
            Write(section, key, value);
        }

        /// <summary>
        /// Writes an unsigned 64-bit integer associated with the specified section and key to the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to set global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="value">
        /// The value to be written.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public void WriteUInt64(string section, string key, ulong value)
        {
            Write(section, key, value);
        }

        /// <summary>
        /// Writes a 32-bit floating point value associated with the specified section and key to the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to set global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="value">
        /// The value to be written.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public void WriteSingle(string section, string key, float value)
        {
            Write(section, key, value);
        }

        /// <summary>
        /// Writes a 64-bit floating point value associated with the specified section and key to the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to set global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="value">
        /// The value to be written.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public void WriteDouble(string section, string key, double value)
        {
            Write(section, key, value);
        }

        /// <summary>
        /// Writes a decimal value associated with the specified section and key to the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to set global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="value">
        /// The value to be written.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public void WriteDecimal(string section, string key, decimal value)
        {
            Write(section, key, value);
        }

        /// <summary>
        /// Writes a <see cref="DateTime"/> value associated with the specified section and key to the INI file.
        /// </summary>
        /// <param name="section">
        /// Section name. Pass null to set global entries above all sections.
        /// </param>
        /// <param name="key">
        /// Key name.
        /// </param>
        /// <param name="value">
        /// The value to be written.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public void WriteDateTime(string section, string key, DateTime value)
        {
            Write(section, key, value);
        }

        /// <summary>
        /// Writes a <see cref="DateTime"/> value to the specified section and key,
        /// formatting it according to the given format string and culture provider.
        /// </summary>
        /// <param name="section">
        /// The section name. Pass <c>null</c> to write a global entry that appears above all sections.
        /// </param>
        /// <param name="key">
        /// The key name. Cannot be <c>null</c>.
        /// </param>
        /// <param name="value">
        /// The <see cref="DateTime"/> value to write.
        /// </param>
        /// <param name="format">
        /// A standard or custom date/time format string (e.g., <c>"yyyy-MM-dd HH:mm:ss"</c>).
        /// The value will be converted to a string using this format.
        /// </param>
        /// <param name="provider">
        /// An <see cref="IFormatProvider"/> that supplies culture-specific formatting information.
        /// If <c>null</c>, <see cref="CultureInfo.InvariantCulture"/> is used.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="key"/> or <paramref name="format"/> is <c>null</c>.
        /// </exception>
        public void WriteDateTime(
            string section,
            string key,
            DateTime value,
            string format,
            IFormatProvider provider = null)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (format == null)
                throw new ArgumentNullException(nameof(format));

            string str = value.ToString(format, provider ?? _culture);
            WriteString(section, key, str);
        }

        /// <summary>
        /// Writes a <see cref="DateTime"/> value to the specified section and key,
        /// using the standard date/time format of the given culture provider.
        /// </summary>
        /// <param name="section">
        /// The section name. Pass <c>null</c> to write a global entry that appears above all sections.
        /// </param>
        /// <param name="key">
        /// The key name. Cannot be <c>null</c>.
        /// </param>
        /// <param name="value">
        /// The <see cref="DateTime"/> value to write.
        /// </param>
        /// <param name="provider">
        /// An <see cref="IFormatProvider"/> that supplies culture-specific formatting information.
        /// The value will be converted using the culture's standard date/time patterns.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="key"/> is <c>null</c>.
        /// </exception>
        public void WriteDateTime(
            string section,
            string key,
            DateTime value,
            IFormatProvider provider)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            string str = value.ToString(provider ?? _culture);
            WriteString(section, key, str);
        }

        #endregion
    }
}
