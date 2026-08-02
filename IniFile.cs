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
          - automatically mapping objects to and from INI files;
          - reading and writing multiline values enclosed in '{' and '}';
          - reading and writing embedded JSON blocks as raw strings or 
            dynamic objects;
          - flexible interpretation of otherwise unrecognised text:
            it can be treated as undefined,   as a key with an empty value
            (flags), or as a value with an empty key (continuation lines);
          - controlling whether the first or last duplicate key value is
            returned.
   
       All  modifications  preserve  the  original  formatting  of  the file,
       including  whitespace,  comments,  and  line  endings,  by  operating
       directly on the original text.
   
       INI parsing behaviour can be configured through the IniSettings class,
       including string comparison rules, multiline values, escape sequences,
       allowed delimiters,  comment characters,   handling of spaces in keys,
       undefined text mode, duplicate key override, and other parser options.
   
       The class can load INI data from strings,  text readers,  streams,  or
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

#nullable disable

namespace System.Ini
{
    #region INI settings

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

        // Value pattern (plain or multiline object).
        private string BuildValuePattern()
        {
            // Determine comment characters for the exclusion class.
            string commentChars = BuildCommentCharacters();
            string exclude = AllowInlineComments ? @"[^\r\n]*" : $@"[^{commentChars}\r\n]*";


            if (AllowMultiLine)
            {
                // Multiline: two alternatives.
                // 1. Optional horizontal whitespace, then a balanced JSON‑like object (captured as 'value').
                // 2. Optional horizontal whitespace, then any text except comment chars or line breaks.
                string obj = BuildObjectPattern();
                return $@"(?:\s*(?<value>{obj})|((?:[^\S\r\n]*)(?<value>{exclude})))";
            }
            else
            {
                // Single‑line: optional horizontal whitespace, then any text except comment chars or line breaks.
                return $@"(?:[^\S\r\n]*)(?<value>{exclude})";
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

        // Matched groups indexes.
        [NonSerialized]
        private readonly int _groupValue;
        
        [NonSerialized]
        private readonly int _groupSection;
        
        [NonSerialized]
        private readonly int _groupKey;
        
        [NonSerialized]
        private readonly int _groupEntry;

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

        // Array containing the characters that are not allowed in path names.
        [NonSerialized]
        private static readonly char[] _invalidPathChars = Path.GetInvalidPathChars();

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
                if (string.IsNullOrEmpty(value)) return;

                // Iterate over matches using the regex pattern and collect sections and entries names.
                for (Match match = _iniRegex.Match(value); match.Success; match = match.NextMatch())
                {
                    GroupCollection groups = match.Groups;
                    if (groups["section"].Success || groups["entry"].Success)
                        _matches.Add(match);
                }
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
            _trueValues = new HashSet<string>(comparer) { "true", "yes", "on", "enable", "1" };
            _falseValues = new HashSet<string>(comparer) { "false", "no", "off", "disable", "0" };

            // Initialize parsing engine.
            _iniRegex = new Regex(iniPattern, regexOptions);
            _jsonRegex = new Regex(jsonPattern, regexOptions);

            // Cache group numbers.
            _groupSection = _iniRegex.GroupNumberFromName("section");
            _groupEntry = _iniRegex.GroupNumberFromName("entry");
            _groupKey = _iniRegex.GroupNumberFromName("key");
            _groupValue = _iniRegex.GroupNumberFromName("value");

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
        public void Save(TextWriter writer)
        {
            writer.Write(Content);
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
        public void Save(Stream stream, Encoding encoding = null)
        {
            using (StreamWriter writer = new StreamWriter(stream, encoding ?? Encoding.UTF8))
            {
                writer.Write(Content);
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
        public void Save(string fileName, Encoding encoding = null)
        {
            string fullPath = GetFullPath(fileName);
            File.WriteAllText(fullPath, Content, encoding ?? Encoding.UTF8);
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

        #endregion

        /****************************************** Core of content processing *****************************************/

        #region Internal data access methods

        // Method to retrieve all sections in the INI file.
        private IEnumerable<string> GetSections()
        {
            HashSet<string> sections = new HashSet<string>(GetComparer(_comparison));

            // Iterate over matches using the regex pattern and collect section names.
            //foreach (Match match in _matches)
            for (int i = 0; i < _matches.Count; i++)
            {
                Match match = _matches[i];

                if (match.Groups[_groupSection].Success)
                {
                    // Convert to lowercase if ignore case mode is enabled.
                    Group group = match.Groups[_groupValue];
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
            bool emptySection = string.IsNullOrEmpty(section);
            bool inSection = emptySection;

            // Iterate through the content to find keys within the specified section.
            for (int i = 0; i < _matches.Count; i++)
            {
                Match match = _matches[i];

                // If the section name is not specified, then the parameters without a section,
                // which are located above the first section, are used.
                if (match.Groups[_groupSection].Success)
                {
                    Group group = match.Groups[_groupValue];
                    inSection = SubstringEquals(_content, group.Index, group.Length, section, _comparison);

                    if (emptySection) break;
                    continue;
                }

                if (inSection && match.Groups[_groupEntry].Success)
                {
                    Group group = match.Groups[_groupKey];
                    string key = NormalizeSubstring(_content, group.Index, group.Length, _comparison);
                    keys.Add(key);
                }
            }

            return keys;
        }

        // Method to get a value from a specific section and key, with an optional default value.
        private string GetValue(string section, string key, string defaultValue = null, bool unwrap = true)
        {
            string value = defaultValue;
            bool emptySection = string.IsNullOrEmpty(section);
            bool inSection = emptySection;

            // Search for the section and key, and return the corresponding value.
            for (int i = 0; i < _matches.Count; i++)
            {
                Match match = _matches[i];

                if (match.Groups[_groupSection].Success)
                {
                    Group group = match.Groups[_groupValue];
                    inSection = SubstringEquals(_content, group.Index, group.Length, section, _comparison);

                    if (emptySection) break;
                    continue;
                }

                if (inSection && match.Groups[_groupEntry].Success)
                {
                    Group group = match.Groups[_groupKey];
                    if (!SubstringEquals(_content, group.Index, group.Length, key, _comparison))
                        continue;

                    value = match.Groups[_groupValue].Value;

                    // Apply unwrapping and unescaping only for regular INI values (not JSON).
                    if (unwrap)
                    {
                        if (_allowMultiLine) value = UnWrap(value);
                        if (_allowEscapeChars) value = UnEscape(value);
                    }

                    // If override mode is off, return the first match immediately.
                    // Otherwise keep scanning...
                    if (!_allowOverrides) return value;
                }
            }

            return value;
        }

        // Method to get all values in a specific section.
        private IEnumerable<string> GetValues(string section, bool unwrap = true)
        {
            List<string> values = new List<string>(DefaultCapacity);
            bool emptySection = string.IsNullOrEmpty(section);
            bool inSection = emptySection;

            // Collect all values within the specified section.
            for (int i = 0; i < _matches.Count; i++)
            {
                Match match = _matches[i];

                if (match.Groups[_groupSection].Success)
                {
                    Group group = match.Groups[_groupValue];
                    inSection = SubstringEquals(_content, group.Index, group.Length, section, _comparison);

                    if (emptySection) break;
                    continue;
                }

                if (inSection && match.Groups[_groupEntry].Success)
                {
                    string value = match.Groups[_groupValue].Value;

                    if (unwrap)
                    {
                        if (_allowMultiLine) value = UnWrap(value);
                        if (_allowEscapeChars) value = UnEscape(value);
                    }

                    values.Add(value);
                }
            }

            return values;
        }

        // Method to get all values associated with a specific key in a section.
        private IEnumerable<string> GetValues(string section, string key, bool unwrap = true)
        {
            // If the key is empty, return all the values in the section.
            if (string.IsNullOrEmpty(key)) return GetValues(section);

            List<string> values = new List<string>(DefaultCapacity);
            bool emptySection = string.IsNullOrEmpty(section);
            bool inSection = emptySection;

            // Collect all values corresponding to the key in the section.
            for (int i = 0; i < _matches.Count; i++)
            {
                Match match = _matches[i];

                if (match.Groups[_groupSection].Success)
                {
                    Group group = match.Groups[_groupValue];
                    inSection = SubstringEquals(_content, group.Index, group.Length, section, _comparison);

                    if (emptySection) break;
                    continue;
                }

                if (inSection && match.Groups[_groupEntry].Success)
                {
                    Group group = match.Groups[_groupKey];
                    if (!SubstringEquals(_content, group.Index, group.Length, key, _comparison))
                        continue;

                    string value = match.Groups[_groupValue].Value;

                    if (unwrap)
                    {
                        if (_allowMultiLine) value = UnWrap(value);
                        if (_allowEscapeChars) value = UnEscape(value);
                    }

                    values.Add(value);
                }
            }

            return values;
        }

        #endregion

        #region Internal data modification methods

        // Sets a single value for a specified key in a given section.
        private void SetValue(string section, string key, string value = null, bool wrap = true, bool escape = true)
        {
            bool emptySection = string.IsNullOrEmpty(section);
            bool expectedValue = !string.IsNullOrEmpty(value); // Indicates that a value should be inserted or updated.
            bool inSection = emptySection;
            Match lastMatch = null; // Keep track of the last match for future reference.
            StringBuilder sb = new StringBuilder(_content);

            // Prepare the value for writing.
            if (_allowEscapeChars && escape && expectedValue)
                value = ToEscape(value);
            else
            {
                string lineBreaker = _allowMultiLine ? _lineBreaker : " ";
                value = NormalizeLineBreaker(value, lineBreaker);
                if (_allowMultiLine && wrap) value = ToWrap(value);
            }

            // Iterate over the content to find the section and key, and set the value.
            for (int i = 0; i < _matches.Count; i++)
            {
                Match match = _matches[i];

                if (match.Groups[_groupSection].Success)
                {
                    Group group = match.Groups[_groupValue];
                    inSection = SubstringEquals(_content, group.Index, group.Length, section, _comparison);

                    if (emptySection) break;
                    continue;
                }

                // If inside the correct section and the match is an entry.
                if (inSection && match.Groups[_groupEntry].Success)
                {
                    lastMatch = match;

                    // Continue if the key doesn't match.
                    Group keyGroup = match.Groups[_groupKey];
                    if (!SubstringEquals(_content, keyGroup.Index, keyGroup.Length, key, _comparison))
                        continue;

                    Group valueGroup = match.Groups[_groupValue];

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

                if (match.Groups[_groupSection].Success)  // Check if the current match is a section.
                {
                    // Set the inSection flag based on whether the section matches the target section.
                    Group sectionGroup = match.Groups[_groupValue];
                    bool sectionMatch = SubstringEquals(_content, sectionGroup.Index, sectionGroup.Length, section, _comparison);

                    if (sectionMatch)
                        lastSection = match;  // Remember only the matching section header.

                    inSection = sectionMatch;
                    if (emptySection) break;  // If there is no section, break out of the loop.
                    continue;
                }

                // Check if inside the correct section and the current match is an entry.
                if (inSection && match.Groups[_groupEntry].Success)
                {
                    lastMatch = match;  // Remember the last entry in the section.

                    // Check if the key matches.
                    Group keyGroup = match.Groups[_groupKey];
                    if (SubstringEquals(_content, keyGroup.Index, keyGroup.Length, key, _comparison))
                    {
                        keyMatches.Add(match);

                        // If there are still values left, replace the current entry.
                        if (valueIndex < values.Length)
                        {
                            // Get the group representing the value.
                            Group valueGroup = match.Groups[_groupValue];

                            // Get the new value to insert.
                            string newValue = values[valueIndex++] ?? string.Empty;
                            string oldValue = valueGroup.Value;

                            // Calculate the index considering previous modifications.
                            int index = valueGroup.Index + offset;
                            int length = valueGroup.Length;

                            // Remove the old value and insert the new one.
                            sb.Remove(index, length);

                            if (_allowEscapeChars)
                                newValue = ToEscape(newValue);
                            else
                            {
                                string lineBreaker = _allowMultiLine ? _lineBreaker : " ";
                                newValue = NormalizeLineBreaker(newValue, lineBreaker);

                                if (_allowMultiLine && wrap)
                                    newValue = ToWrap(newValue);
                            }

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

                    if (_allowEscapeChars)
                        value = ToEscape(value);
                    else
                    {
                        string lineBreaker = _allowMultiLine ? _lineBreaker : " ";
                        value = NormalizeLineBreaker(value, lineBreaker);

                        if (_allowMultiLine && wrap)
                            value = ToWrap(value);
                    }

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
                if (!m.Groups["Comment"].Success && !m.Groups["whitespace"].Success && !m.Groups["newline"].Success)
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

            int nesting = 0;
            bool started = false;

            while (index < matches.Count)
            {
                Match m = matches[index];

                // Skip comments, whitespace, newlines
                if (m.Groups["Comment"].Success || m.Groups["whitespace"].Success || m.Groups["newline"].Success)
                {
                    index++;
                    continue;
                }

                if (m.Groups["object_open"].Success || m.Groups["array_open"].Success)
                {
                    nesting++;
                    started = true;
                }
                else if (m.Groups["object_close"].Success || m.Groups["array_close"].Success)
                {
                    nesting--;
                    if (started && nesting == 0)
                    {
                        index++; // consume closing token
                        break;
                    }
                }
                // For any other token (strings, numbers, etc.) just skip
                index++;
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
            if (m.Groups["object_open"].Success)
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
            else if (m.Groups["array_open"].Success)
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
            else if (m.Groups["value"].Success)
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
                if (m.Groups["object_close"].Success)
                {
                    index++;
                    result = dict;
                    return true;
                }

                if (first)
                {
                    // Expect key.
                    if (!m.Groups["key"].Success)
                        return false;
                    first = false;
                }
                else
                {
                    // Expect comma or close.
                    if (m.Groups["array_sep"].Success)
                    {
                        index++;
                        // Skip whitespace.
                        SkipWhitespaceAndComments(matches, ref index);
                        if (index >= matches.Count) return false;
                        m = matches[index];

                        // Trailing comma, skip to close...
                        if (m.Groups["object_close"].Success)
                        {
                            index++;
                            result = dict;
                            return true;
                        }

                        // ...else expect key.
                        if (!m.Groups["key"].Success)
                            return false;
                    }

                    // End of the object.
                    else if (m.Groups["object_close"].Success)
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
                string key = UnEscape(m.Groups["key"].Value.Substring(1, m.Groups["key"].Value.Length - 2));
                index++;

                // Skip whitespace.
                SkipWhitespaceAndComments(matches, ref index);
                if (index >= matches.Count) return false;
                m = matches[index];

                // Expect delimiter.
                if (!m.Groups["value_sep"].Success)
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
                if (m.Groups["array_close"].Success)
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
                    if (m.Groups["array_sep"].Success)
                    {
                        // Skip whitespace.
                        index++;
                        SkipWhitespaceAndComments(matches, ref index);
                        if (index >= matches.Count) return false;
                        m = matches[index];

                        // Trailing comma, skip to close.
                        if (m.Groups["array_close"].Success)
                        {
                            index++;
                            result = list.ToArray();
                            return true;
                        }

                        // ...else parse value
                    }
                    else if (m.Groups["array_close"].Success)
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
            if (match.Groups["null"].Success)
            {
                result = null;
                return true;
            }

            // Boolean.
            if (match.Groups["bool"].Success)
            {
                if (bool.TryParse(match.Groups["bool"].Value, out bool value))
                {
                    result = value;
                    return true;
                }
                return false;
            }

            // String.
            if (match.Groups["string"].Success)
            {
                string value = match.Groups["string"].Value;
                if (_allowEscapeChars) value = UnEscape(value);
                result = value;
                return true;
            }

            // Number.
            if (match.Groups["number"].Success)
            {
                if (double.TryParse(
                    match.Groups["number"].Value,
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
                if (_allowEscapeChars) str = ToEscape(str);
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
            if (_allowEscapeChars) text = ToEscape(text);
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
                if (_allowEscapeChars) key = ToEscape(key);
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

        #endregion

        #region Internal utility and helper methods

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
                try
                {
                    return _values.TryGetValue(binder.Name, out result);
                }
                catch (Exception)
                {
                    result = null;
                    return false;
                }
                
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
        private static RegexOptions GetRegexOptions(StringComparison comparison, RegexOptions options = RegexOptions.None)
        {
            // Bit 0 indicates IgnoreCase.
            if ((((int)comparison) & 1) != 0)
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
        private static object ParseEnum(string value, Type enumType, bool ignoreCase = true)
        {
            if (string.IsNullOrEmpty(value) || enumType == null) return null;

            // Split by commas, trim whitespace.
            string[] parts = value.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
            long result = 0;

            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                // Try to parse by name first.
                try
                {
                    object parsed = Enum.Parse(enumType, trimmed, ignoreCase);
                    result |= Convert.ToInt64(parsed);
                }
                catch (ArgumentException)
                {
                    // If name parsing fails, try numeric parsing.
                    if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long numeric))
                        result |= numeric;
                    else
                        throw; // Re-throw if both fail.
                }
            }

            return Enum.ToObject(enumType, result);
        }

        // Converts escaped characters in the input string.
        private static string UnEscape(string text)
        {
            int pos = -1;
            int inputLength = text.Length;

            if (inputLength == 0) return text;

            // Find the first backslash or return the original text without allocating.
            for (int i = 0; i < inputLength; ++i)
            {
                if (text[i] == '\\')
                {
                    pos = i;
                    break;
                }
            }

            if (pos < 0) return text; // No backslash found.

            // Copy the unchanged prefix preceding the first escape sequence.
            StringBuilder sb = new StringBuilder(inputLength);
            sb.Append(text, 0, pos);

            do
            {
                char c = text[pos++];
                if (c == '\\')
                {
                    // Read the escape sequence following the backslash.
                    // If the backslash is the last character, keep it unchanged.
                    c = pos < inputLength ? text[pos] : '\\';
                    switch (c)
                    {
                        case '\\':
                            c = '\\';
                            break;
                        case '0':
                            c = '\0';
                            break;
                        case 'a':
                            c = '\a';
                            break;
                        case 'b':
                            c = '\b';
                            break;
                        case 'n':
                            c = '\n';
                            break;
                        case 'r':
                            c = '\r';
                            break;
                        case 'f':
                            c = '\f';
                            break;
                        case 't':
                            c = '\t';
                            break;
                        case 'v':
                            c = '\v';
                            break;
                        // Unicode escape: \uXXXX
                        case 'u' when pos < inputLength - 3:
                            c = UnHex(text, ++pos, 4);
                            pos += 3;
                            break;
                        // Hex escape: \xXX
                        case 'x' when pos < inputLength - 1:
                            c = UnHex(text, ++pos, 2);
                            pos++;
                            break;
                        // Control character escape: \cA .. \cZ
                        case 'c' when pos < inputLength:
                            c = text[++pos];
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
            if (value == null) return null;

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
                    case '\\':
                        sb.Append(@"\\");
                        break;
                    case '\0':
                        sb.Append(@"\0");
                        break;
                    case '\a':
                        sb.Append(@"\a");
                        break;
                    case '\b':
                        sb.Append(@"\b");
                        break;
                    case '\n':
                        sb.Append(@"\n");
                        break;
                    case '\r':
                        sb.Append(@"\r");
                        break;
                    case '\f':
                        sb.Append(@"\f");
                        break;
                    case '\t':
                        sb.Append(@"\t");
                        break;
                    case '\v':
                        sb.Append(@"\v");
                        break;
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

        // // Removes the outer '{' and '}' from a wrapped value and trims spaces and tabs inside the braces.
        private static string UnWrap(string value)
        {
            if (value == null) return null;

            int length = value.Length;
            if (length < 2 || value[0] != '{' || value[length - 1] != '}')
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

        // Wraps a multiline value in '{' and '}'.
        private string ToWrap(string value)
        {
            if (value == null) return null;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\r' || c == '\n')
                {
                    return string.Concat("{", _lineBreaker, value, _lineBreaker, "}");
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

            int count;
            using (FileStream fs = File.OpenRead(fileName))
            {
                count = fs.Read(buffer, 0, buffer.Length);
            }

            if (count >= 4)
            {
                // UTF-32 BE
                if (buffer[0] == 0x00 && buffer[1] == 0x00 &&
                    buffer[2] == 0xFE && buffer[3] == 0xFF)
                    return Encoding.GetEncoding("utf-32BE");

                // UTF-32 LE
                if (buffer[0] == 0xFF && buffer[1] == 0xFE &&
                    buffer[2] == 0x00 && buffer[3] == 0x00)
                    return Encoding.UTF32;
            }

            if (count >= 3)
            {
                // UTF-8
                if (buffer[0] == 0xEF &&
                    buffer[1] == 0xBB &&
                    buffer[2] == 0xBF)
                    return Encoding.UTF8;

#pragma warning disable SYSLIB0001
                // UTF-7
                if (buffer[0] == 0x2B &&
                    buffer[1] == 0x2F &&
                    buffer[2] == 0x76)
                    return Encoding.UTF7;
#pragma warning restore SYSLIB0001
            }

            if (count >= 2)
            {
                // UTF-16 LE
                if (buffer[0] == 0xFF &&
                    buffer[1] == 0xFE)
                    return Encoding.Unicode;

                // UTF-16 BE
                if (buffer[0] == 0xFE &&
                    buffer[1] == 0xFF)
                    return Encoding.BigEndianUnicode;
            }

            // UTF-16 heuristic.
            int evenZero = 0;
            int oddZero = 0;

            for (int i = 0; i + 1 < count; i += 2)
            {
                if (buffer[i] == 0)
                    evenZero++;

                if (buffer[i + 1] == 0)
                    oddZero++;
            }

            int pairs = count / 2;

            if (pairs > 8)
            {
                if (oddZero > pairs * 8 / 10)
                    return Encoding.Unicode;

                if (evenZero > pairs * 8 / 10)
                    return Encoding.BigEndianUnicode;
            }

            // UTF-8 heuristic.
            if (IsUtf8(buffer, count))
                return Encoding.UTF8;

            // Default fallback.
            return defaultEncoding ?? Encoding.Default;
        }

        private static bool IsUtf8(byte[] buffer, int count)
        {
            bool hasMultibyte = false;

            for (int i = 0; i < count;)
            {
                byte b = buffer[i];

                if (b <= 0x7F)
                {
                    i++;
                    continue;
                }

                int remaining;

                if ((b & 0xE0) == 0xC0)
                {
                    remaining = 1;

                    if (b < 0xC2)
                        return false;
                }
                else if ((b & 0xF0) == 0xE0)
                {
                    remaining = 2;
                }
                else if ((b & 0xF8) == 0xF0)
                {
                    remaining = 3;

                    if (b > 0xF4)
                        return false;
                }
                else
                {
                    return false;
                }

                if (i + remaining >= count)
                    return false;

                while (remaining-- > 0)
                {
                    i++;

                    if ((buffer[i] & 0xC0) != 0x80)
                        return false;
                }

                hasMultibyte = true;
                i++;
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

                if (match.Groups[_groupSection].Success)
                {
                    string sectionName = match.Groups[_groupValue].Value;
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

                if (match.Groups[_groupEntry].Success)
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

                    string key = match.Groups[_groupKey].Value;
                    string value = match.Groups[_groupValue].Value;

                    // Unwrap/unescape if needed
                    if (_allowMultiLine) value = UnWrap(value);
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
        /// The resulting string uses '=' as the delimiter and normalizes spacing.
        /// Multiple values for the same key are preserved as separate lines.
        /// The order of sections and keys is preserved.
        /// </summary>
        /// <returns>A compacted INI string.</returns>
        public string Justify()
        {
            var sb = new StringBuilder();

            // We'll process matches directly to preserve order and avoid extra allocations.
            bool firstSection = true;
            string currentSection = null;

            // We need to collect global entries first, then sections.
            // To keep order, we'll iterate twice: first collect global entries, then sections.
            // Or we can build lists.
            // Let's build a list of all entries with their section.

            var globalEntries = new List<KeyValuePair<string, string>>(DefaultCapacity);
            var sectionEntries = new Dictionary<string, List<KeyValuePair<string, string>>>(GetComparer(_comparison));
            var sectionOrder = new List<string>(DefaultCapacity);

            for (int i = 0; i < _matches.Count; i++)
            {
                Match match = _matches[i];

                if (match.Groups[_groupSection].Success)
                {
                    string section = match.Groups[_groupValue].Value;
                    if (!sectionEntries.ContainsKey(section))
                    {
                        sectionEntries[section] = new List<KeyValuePair<string, string>>(DefaultCapacity);
                        sectionOrder.Add(section);
                    }
                    currentSection = section;
                    continue;
                }

                if (match.Groups[_groupEntry].Success)
                {
                    string key = match.Groups[_groupKey].Value;
                    string value = match.Groups[_groupValue].Value;
                    //if (_allowMultiLine) value = UnWrap(value);
                    //if (_allowEscapeChars) value = UnEscape(value);

                    if (currentSection == null) // Global section.
                    {
                        globalEntries.Add(new KeyValuePair<string, string>(key, value));
                    }
                    else
                    {
                        if (sectionEntries.TryGetValue(currentSection, out var list))
                            list.Add(new KeyValuePair<string, string>(key, value));
                    }
                }
            }

            // Write global entries.
            if (globalEntries.Count > 0)
            {
                foreach (var kv in globalEntries)
                    sb.AppendLine($"{kv.Key}={kv.Value}");
                sb.AppendLine();
            }

            // Write sections.
            foreach (string section in sectionOrder)
            {
                sb.AppendLine($"[{section}]");
                var entries = sectionEntries[section];
                foreach (var kv in entries)
                    sb.AppendLine($"{kv.Key}={kv.Value}");
                sb.AppendLine(); // blank line after each section
            }

            // Remove trailing line breakers.
            if (sb.Length > 0)
            {
                if (sb[sb.Length - 1] == '\n')
                    sb.Length--;
                if (sb.Length > 0 && sb[sb.Length - 1] == '\r')
                    sb.Length--;
            }

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
        /// <returns>
        /// Read value.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when parameter <paramref name="key"/> is null.
        /// </exception>
        public string ReadString(string section, string key, string defaultValue = "")
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            return GetValue(section, key, defaultValue);
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

            return GetValue(section, key, defaultValue, false);
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

            string format = GetValue(section, key, defaultValue);
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

            // If no strings are found and default values are provided, use the default values.
            if (values.Length == 0 && defaultValues?.Length > 0)
                values = defaultValues;

            // Return the array of strings.
            return values;
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

            // If no converter is provided, use the default converter for the specified type.
            if (converter == null)
                converter = TypeDescriptor.GetConverter(type);

            // Attempt to read the string value from the ini file for the given section and key.
            string value = ReadString(section, key, null);

            // If a value is found and can be converted from string, convert it and return.
            if (value != null)
            {
                // If the desired type is string, return the value directly.
                if (type == typeof(string))
                    return value;

                // If the desired type is boolean, try custom conversion for boolean.
                if (type == typeof(bool))
                {
                    // Flag mode.
                    if (value == string.Empty)
                        return true;

                    // Try to parse as integer (decimal) with no hex specifier.
                    if (int.TryParse(value, NumberStyles.Integer, _culture, out int number))
                        return number != 0;

                    // Try to parse as hex number (allow "0x" prefix manually).
                    string hexValue = value.Trim();
                    if (hexValue.StartsWith("0x") || hexValue.StartsWith("0X"))
                        hexValue = hexValue.Substring(2);
                    if (int.TryParse(hexValue, NumberStyles.HexNumber, _culture, out int hexNumber))
                        return hexNumber != 0;

                    if (_trueValues.Contains(value))
                    {
                        return true;
                    }
                    if (_falseValues.Contains(value))
                    {
                        return false;
                    }
                }

                // If the type is an enumeration, try parsing the enum value.
                if (type.IsEnum)
                {
                    try
                    {
                        // Try to parse the value as an enum name or numeric value.
                        return ParseEnum(value, type, ignoreCase: true);
                    }
                    catch
                    {
                        // If parsing fails, the default value will be returned at the end of the method.
                    }
                }

                if (converter.CanConvertFrom(typeof(string)))
                {
                    try
                    {
                        return converter.ConvertFromString(null, _culture, value);
                    }
                    catch
                    {
                        // If fails process the default value.
                    }
                }
            }

            // If a default value is provided and needs conversion, convert it to the desired type
            if (defaultValue != null && defaultValue.GetType() != type && converter.CanConvertFrom(defaultValue.GetType()))
                try
                {
                    defaultValue = converter.ConvertFrom(null, _culture, defaultValue);
                }
                catch
                {
                    defaultValue = null; // If conversion fails return null.
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

            string json = GetValue(section, key, null, false);
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

            string json = GetValue(section, key, null, false);
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
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (converter == null)
                converter = TypeDescriptor.GetConverter(typeof(T));

            if (typeof(T) == typeof(bool))
                return (T)(object)ReadBoolean(section, key, (bool)(object)defaultValue);

            if (typeof(T) == typeof(char))
                return (T)(object)ReadChar(section, key, (char)(object)defaultValue);

            // Attempt to read the string value from the INI file for the given section and key.
            string value = ReadString(section, key, null);

            // Attempt to directly cast the value to type T if it matches.
            if (value is T t) return t;

            // If the value is null or empty, return the provided default value.
            if (string.IsNullOrEmpty(value)) return defaultValue;

            // Convert the string value to the specified type T using the converter and return it.
            try
            {
                return (T)converter.ConvertFromString(null, _culture, value);
            }
            catch
            {
                return defaultValue; // If conversion fails return the default value.
            }
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
                object value = ReadObject(section, key, propertyType, defaultValue, converter);

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
        public void ReadProperty(PropertyInfo property, object obj, object defaultValue = null, TypeConverter converter = null)
        {
            if (property == null)
                throw new ArgumentNullException(nameof(property));

            // Skip properties marked with [IniIgnore]
            if (property.GetCustomAttributes(typeof(IniIgnoreAttribute), false).Length > 0)
                return;

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

            // Try to parse as integer (decimal) with no hex specifier.
            if (int.TryParse(value, NumberStyles.Integer, _culture, out int number))
                return number != 0;

            // Try to parse as hex number (allow "0x" prefix manually).
            string hexValue = value.Trim();
            if (hexValue.StartsWith("0x") || hexValue.StartsWith("0X"))
                hexValue = hexValue.Substring(2);
            if (int.TryParse(hexValue, NumberStyles.HexNumber, _culture, out int hexNumber))
                return hexNumber != 0;

            // Try to parse by sets of true/false values.
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

                if (match.Groups[_groupSection].Success)
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
                    if (match.Groups[_groupValue].Value.Equals(section, _comparison))
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

            SetValue(section, key, value, false, false);
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

            SetValues(section, key, wrap: true, values);
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

                else if (value != null && value.GetType().IsEnum)
                {
                    str = EnumToString(value);
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
                SetValue(section, key, null, false);
                return;
            }

            string json = SerializeJson(value, beautify);
            SetValue(section, key, json, false, false);
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
                SetValue(section, key, null, false, false);
                return;
            }

            // Convert dynamic to a regular object for serialization.
            // If it's ExpandoObject, we need to convert to Dictionary.
            object obj = ConvertFromDynamic(value);
            string json = SerializeJson(obj, beautify);
            SetValue(section, key, json, false, false);
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
            if (converter == null)
                converter = TypeDescriptor.GetConverter(typeof(T));

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
                object defaultValue = property.GetCustomAttributes(typeof(DefaultValueAttribute), false).FirstOrDefault() is
                    DefaultValueAttribute defaultValueAttribute
                    ? defaultValueAttribute.Value
                    : null;
                ReadProperty(property, obj, defaultValue);
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
