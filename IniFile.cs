/***************************************************************

•   File: IniFile.cs

•   Description

    THe IniFile is a class that represents a parser of ini files
    using regular expressions.

    The  class implements  methods  for working with  ini files:
       - parsing INI files;
       - getting sections, keys and values by sections and keys;
       - setting values;
       - automatically initializes properties.

    To  use the class, you  must  pass  it  a  string  or stream
    containing the ini file data and some parsing settings.

•   License

    This software is distributed under the MIT License (MIT)

    © 2024 Pavel Bashkardin.

    Permission is  hereby granted, free of charge, to any person
    obtaining   a copy    of    this  software    and associated
    documentation  files  (the “Software”),    to  deal   in the
    Software without  restriction, including without  limitation
    the rights to use, copy, modify, merge, publish, distribute,
    sublicense,  and/or  sell  copies   of  the Software, and to
    permit persons to whom the Software  is furnished to  do so,
    subject to the following conditions:

    The above copyright  notice and this permission notice shall
    be  included  in all copies   or substantial portions of the
    Software.

    THE  SOFTWARE IS  PROVIDED  “AS IS”, WITHOUT WARRANTY OF ANY
    KIND, EXPRESS  OR IMPLIED, INCLUDING  BUT NOT LIMITED TO THE
    WARRANTIES  OF MERCHANTABILITY, FITNESS    FOR A  PARTICULAR
    PURPOSE AND NONINFRINGEMENT. IN  NO EVENT SHALL  THE AUTHORS
    OR  COPYRIGHT HOLDERS  BE  LIABLE FOR ANY CLAIM,  DAMAGES OR
    OTHER LIABILITY,  WHETHER IN AN  ACTION OF CONTRACT, TORT OR
    OTHERWISE, ARISING FROM, OUT OF   OR IN CONNECTION  WITH THE
    SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

***************************************************************/
#nullable disable
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;

namespace System.Ini
{
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
        public string Name
        {
            get => _sectionName;
        }

        /// <inheritdoc />
        public override bool IsDefaultAttribute()
        {
            return string.IsNullOrEmpty(_sectionName);
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
            return string.IsNullOrEmpty(_entryName);
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

    /// <summary>
    /// Represents a regular expression-based, collection-free INI file parser that preserves the original file formatting when editing entries.
    /// </summary>
    [Serializable]
    public sealed class IniFile
    {
        // Private field for storing the content of the INI file.
        private string _content;

        // Cache of found matches, which improves performance.
        [NonSerialized]
        private List<Match> _matches;

        // Regular expression used for parsing the INI file.
        [NonSerialized]
        private readonly Regex _regex;

        // Indicates whether escape characters are allowed in the INI file.
        [NonSerialized]
        private readonly bool _allowEscapeChars = false;

        // Indicates whether multi line values are allowed in the INI file.
        [NonSerialized]
        private readonly bool _allowMultiLine = false;

        // String used to represent line breaks in the INI file.
        [NonSerialized]
        private readonly string _lineBreaker = Environment.NewLine;

        // Contains culture-specific information for parsing.
        [NonSerialized]
        private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

        // Determines how string comparisons are performed in the INI file.
        // Configured based on settings passed to the constructor.
        [NonSerialized]
        private readonly StringComparison _comparison = StringComparison.InvariantCultureIgnoreCase;

        [NonSerialized]
        private readonly HashSet<string> _trueValues;

        [NonSerialized]
        private readonly HashSet<string> _falseValues;

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
                if(string.IsNullOrEmpty(value)) return;

                // Iterate over matches using the regex pattern and collect sections and entries names.
                for (Match match = _regex.Match(_content); match.Success; match = match.NextMatch())
                {
                    GroupCollection groups = match.Groups;
                    if (groups["section"].Success || groups["entry"].Success)
                        _matches.Add(match);
                }
            }
        }

        // Private constructor to prevent direct instantiation.
        private IniFile()
        { }

        // Constructor accepting ini content as a string and settings.
        // Initializes the parser settings, setting the comparison rules,
        // regular expression pattern, escape character allowance, and delimiter
        // based on the provided settings.
        private IniFile(string content,
            StringComparison comparison = StringComparison.InvariantCultureIgnoreCase,
            bool allowEscChars = false)
        {
            if ((uint)comparison > (uint)StringComparison.OrdinalIgnoreCase)
                throw new ArgumentOutOfRangeException(nameof(comparison));
            
            if (content == null) content = string.Empty;
            _comparison = comparison;
            _regex = new Regex(@"(?=\S)(?<text>(?<comment>(?<open>[#;]+)(?:[^\S\r\n]*)(?<value>.+))|" +
                               @"(?<section>(?<open>\[)(?:\s*)(?<value>[^\]]*\S+)(?:[^\S\r\n]*)(?<close>\]))|" +
                               @"(?<entry>(?<key>[^=\r\n\[\]]*\S)(?:[^\S\r\n]*)(?<delimiter>:|=)(?:[^\S\r\n]*)(?<value>[^#;\r\n]*))|" +
                               @"(?<undefined>.+))(?<=\S)|" +
                               @"(?<linebreaker>\r\n|\n)|" +
                               @"(?<whitespace>[^\S\r\n]+)",
                GetRegexOptions(comparison, RegexOptions.Compiled));
            _culture = GetCultureInfo(_comparison);
            _allowEscapeChars = allowEscChars;
            _lineBreaker = AutoDetectLineBreaker(content);
            _matches = new List<Match>(16);
            Content = content;
            StringComparer comparer = GetComparer(comparison);
            _trueValues = new HashSet<string>(comparer) { "true", "yes", "on", "enable", "1" };
            _falseValues = new HashSet<string>(comparer) { "false", "no", "off", "disable", "0" };
        }

        /// <summary>
        /// Create a new instance of <see cref="IniFile"/> with empty content.
        /// </summary>
        /// <param name="comparison">
        /// Specifies the rules for string comparison.
        /// </param>
        /// <param name="allowEscChars">
        /// Indicates whether escape characters are allowed in the INI file.
        /// </param>
        /// <returns>
        /// An instance of <see cref="IniFile"/> initialized with the specified settings.
        /// </returns>
        public static IniFile Create(StringComparison comparison = StringComparison.InvariantCultureIgnoreCase,
            bool allowEscChars = false)
        {
            return new IniFile(string.Empty, comparison, allowEscChars);
        }

        /// <summary>
        /// Loads an INI file using a <see cref="TextReader"/> and initializes an instance of <see cref="IniFile"/>.
        /// </summary>
        /// <param name="reader">
        /// The <see cref="TextReader"/> containing the INI file data.
        /// </param>
        /// <param name="comparison">
        /// Specifies the rules for string comparison.
        /// </param>
        /// <param name="allowEscChars">
        /// Indicates whether escape characters are allowed in the INI file.
        /// </param>
        /// <returns>
        /// An instance of <see cref="IniFile"/> initialized with the specified data and settings.
        /// </returns>
        public static IniFile Load(TextReader reader,
            StringComparison comparison = StringComparison.InvariantCultureIgnoreCase,
            bool allowEscChars = false)
        {
            return new IniFile(reader.ReadToEnd(), comparison, allowEscChars);
        }

        /// <summary>
        /// Loads an INI file from a <see cref="Stream"/> using the specified encoding and initializes an instance of <see cref="IniFile"/>.
        /// </summary>
        /// <param name="stream">
        /// The <see cref="Stream"/> containing the INI file data.
        /// </param>
        /// <param name="encoding">
        /// The <see cref="Encoding"/> used to read the stream.
        /// </param>
        /// <param name="comparison">
        /// Specifies the rules for string comparison.
        /// </param>
        /// <param name="allowEscChars">
        /// Indicates whether escape characters are allowed in the INI file.
        /// </param>
        /// <returns>
        /// An instance of <see cref="IniFile"/> initialized with the specified data and settings.
        /// </returns>
        public static IniFile Load(Stream stream, Encoding encoding = null,
            StringComparison comparison = StringComparison.InvariantCultureIgnoreCase,
            bool allowEscChars = false)
        {
            using (StreamReader reader = new StreamReader(stream ?? throw new ArgumentNullException(nameof(stream)), encoding ?? Encoding.UTF8))
                return new IniFile(reader.ReadToEnd(), comparison, allowEscChars);
        }

        /// <summary>
        /// Loads an INI file from a file specified by its path using the specified encoding and initializes an instance of <see cref="IniFile"/>.
        /// </summary>
        /// <param name="fileName">
        /// The path to the file containing the INI data.
        /// </param>
        /// <param name="encoding">
        /// The <see cref="Encoding"/> used to read the file.
        /// </param>
        /// <param name="comparison">
        /// Specifies the rules for string comparison.
        /// </param>
        /// <param name="allowEscChars">
        /// Indicates whether escape characters are allowed in the INI file.
        /// </param>
        /// <returns>
        /// An instance of <see cref="IniFile"/> initialized with the specified data and settings.
        /// </returns>
        public static IniFile Load(string fileName,
            Encoding encoding,
            StringComparison comparison = StringComparison.InvariantCultureIgnoreCase,
            bool allowEscChars = false)
        {
            string filePath = GetFullPath(fileName, true);
            return new IniFile(File.ReadAllText(filePath, encoding ?? AutoDetectEncoding(filePath, Encoding.UTF8)),
                comparison, allowEscChars);
        }

        /// <summary>
        /// Loads an INI file from a file specified by its path using the specified encoding and initializes an instance of <see cref="IniFile"/>.
        /// </summary>
        /// <param name="fileName">
        /// The path to the file containing the INI data.
        /// </param>
        /// <param name="comparison">
        /// Specifies the rules for string comparison.
        /// </param>
        /// <param name="allowEscChars">
        /// Indicates whether escape characters are allowed in the INI file.
        /// </param>
        /// <returns>
        /// An instance of <see cref="IniFile"/> initialized with the specified data and settings.
        /// </returns>
        public static IniFile Load(string fileName,
            StringComparison comparison = StringComparison.InvariantCultureIgnoreCase,
            bool allowEscChars = false)
        {
            string filePath = GetFullPath(fileName, true);

            return new IniFile(File.ReadAllText(filePath, AutoDetectEncoding(filePath, Encoding.UTF8)),
                comparison, allowEscChars);
        }

        /// <summary>
        /// Loads an INI file using a <see cref="TextReader"/> or create it with empty content
        /// and initializes an instance of <see cref="IniFile"/>.
        /// </summary>
        /// <param name="fileName">
        /// The path to the file containing the INI data.
        /// </param>
        /// <param name="encoding">
        /// The <see cref="Encoding"/> used to read the file.
        /// </param>
        /// <param name="comparison">
        /// Specifies the rules for string comparison.</param>
        /// <param name="allowEscChars">
        /// Indicates whether escape characters are allowed in the INI file.
        /// </param>
        /// <returns>
        /// An instance of <see cref="IniFile"/> initialized with the specified settings.
        /// </returns>
        public static IniFile LoadOrCreate(string fileName, Encoding encoding,
            StringComparison comparison = StringComparison.InvariantCultureIgnoreCase,
            bool allowEscChars = false)
        {
            string filePath = GetFullPath(fileName);

            return new IniFile(
                File.Exists(filePath)
                    ? File.ReadAllText(filePath, encoding ?? AutoDetectEncoding(filePath, Encoding.UTF8))
                    : string.Empty,
                comparison, allowEscChars);
        }

        /// <summary>
        /// Loads an INI file using a <see cref="TextReader"/> or create it with empty content
        /// and initializes an instance of <see cref="IniFile"/>.
        /// </summary>
        /// <param name="fileName">
        /// The path to the file containing the INI data.
        /// </param>
        /// <param name="comparison">
        /// Specifies the rules for string comparison.</param>
        /// <param name="allowEscChars">
        /// Indicates whether escape characters are allowed in the INI file.
        /// </param>
        /// <returns>
        /// An instance of <see cref="IniFile"/> initialized with the specified settings.
        /// </returns>
        public static IniFile LoadOrCreate(string fileName,
            StringComparison comparison = StringComparison.InvariantCultureIgnoreCase,
            bool allowEscChars = false)
        {
            string filePath = GetFullPath(fileName);

            return new IniFile(
                File.Exists(filePath)
                    ? File.ReadAllText(filePath, AutoDetectEncoding(filePath, Encoding.UTF8))
                    : string.Empty,
                comparison, allowEscChars);
        }

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

        // Method to retrieve all sections in the INI file.
        private IEnumerable<string> GetSections()
        {
            HashSet<string> sections = new HashSet<string>(GetComparer(_comparison));

            // Iterate over matches using the regex pattern and collect section names.
            //foreach (Match match in _matches)
            for (int i = 0; i < _matches.Count; i++)
            {
                Match match = _matches[i];

                if (match.Groups["section"].Success)
                {
                    // Convert to lowercase if ignore case mode is enabled.
                    string section = MayBeToLower(match.Groups["value"].Value, _comparison);
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
                if (match.Groups["section"].Success)
                {
                    inSection = match.Groups["value"].Value.Equals(section, _comparison);
                    if (emptySection) break;
                    continue;
                }

                if (inSection && match.Groups["entry"].Success)
                {
                    string key = MayBeToLower(match.Groups["key"].Value, _comparison);
                    keys.Add(key);
                }
            }

            return keys;
        }

        // Method to get a value from a specific section and key, with an optional default value.
        private string GetValue(string section, string key, string defaultValue = null)
        {
            string value = defaultValue;
            bool emptySection = string.IsNullOrEmpty(section);
            bool inSection = emptySection;

            // Search for the section and key, and return the corresponding value.
            for (int i = 0; i < _matches.Count; i++)
            {
                Match match = _matches[i];

                if (match.Groups["section"].Success)
                {
                    inSection = match.Groups["value"].Value.Equals(section, _comparison);
                    if (emptySection) break;
                    continue;
                }

                if (inSection && match.Groups["entry"].Success)
                {
                    if (!match.Groups["key"].Value.Equals(key, _comparison))
                        continue;

                    value = match.Groups["value"].Value;
                    if (_allowEscapeChars) value = UnEscape(value);

                    return value;
                }
            }

            return value;
        }

        // Method to get all values in a specific section.
        private IEnumerable<string> GetValues(string section)
        {
            List<string> values = new List<string>();
            bool emptySection = string.IsNullOrEmpty(section);
            bool inSection = emptySection;

            // Collect all values within the specified section.
            for (int i = 0; i < _matches.Count; i++)
            {
                Match match = _matches[i];

                if (match.Groups["section"].Success)
                {
                    inSection = match.Groups["value"].Value.Equals(section, _comparison);
                    if (emptySection) break;
                    continue;
                }

                if (inSection && match.Groups["entry"].Success)
                {
                    string value = match.Groups["value"].Value;
                    if (_allowEscapeChars) value = UnEscape(value);
                    values.Add(value);
                }
            }

            return values;
        }

        // Method to get all values associated with a specific key in a section.
        private IEnumerable<string> GetValues(string section, string key)
        {
            // If the key is empty, return all the values in the section.
            if (string.IsNullOrEmpty(key)) return GetValues(section);

            List<string> values = new List<string>();
            bool emptySection = string.IsNullOrEmpty(section);
            bool inSection = emptySection;

            // Collect all values corresponding to the key in the section.
            for (int i = 0; i < _matches.Count; i++)
            {
                Match match = _matches[i];

                if (match.Groups["section"].Success)
                {
                    inSection = match.Groups["value"].Value.Equals(section, _comparison);
                    if (emptySection) break;
                    continue;
                }

                if (inSection && match.Groups["entry"].Success)
                {
                    if (!match.Groups["key"].Value.Equals(key, _comparison))
                        continue;

                    string value = match.Groups["value"].Value;
                    if (_allowEscapeChars) value = UnEscape(value);
                    values.Add(value);
                }
            }

            return values;
        }

        // Sets a single value for a specified key in a given section.
        private void SetValue(string section, string key, string value)
        {
            bool emptySection = string.IsNullOrEmpty(section);
            bool expectedValue = !string.IsNullOrEmpty(value); // Indicates that value is not set.
            bool inSection = emptySection;
            Match lastMatch = null; // Keep track of the last match for future reference.
            StringBuilder sb = new StringBuilder(_content);


            // Escape the value if necessary.
            if (_allowEscapeChars && expectedValue) value = ToEscape(value);

            // Iterate over the content to find the section and key, and set the value.
            for (int i = 0; i < _matches.Count; i++)
            {
                Match match = _matches[i];

                if (match.Groups["section"].Success)
                {
                    inSection = match.Groups["value"].Value.Equals(section, _comparison);
                    if (emptySection) break; // If no section is specified, break out of the loop.
                    continue;
                }

                // If inside the correct section and the match is an entry.
                if (inSection && match.Groups["entry"].Success)
                {
                    lastMatch = match;

                    // Continue if the key doesn't match.
                    if (!match.Groups["key"].Value.Equals(key, _comparison))
                        continue;

                    // Remove the existing value associated with the key
                    // and insert the new value in its place if it is not empty.
                    Group group = match.Groups["value"];

                    int index = group.Index;
                    int length = group.Length;

                    if (expectedValue)
                    {
                        // Remove the old value.
                        sb.Remove(index, length);

                        // Insert the new value in its place.
                        sb.Insert(index, value);
                    }
                    else
                    {
                        // Remove all entry.
                        sb.Remove(match.Index, match.Length);
                    }

                    // Indicate the value has been set.
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

                // If no match was found, append a new section and then insert the key-value pair
                else if (!emptySection)
                {


                    // Add the section header.
                    sb.Append(_lineBreaker);
                    sb.Append($"[{section}]{_lineBreaker}");
                    index = sb.Length;  // Set the index to the end of the StringBuilder.
                }

                // Insert the new key-value pair into the content.
                string line = $"{key}={value}";
                InsertLine(sb, ref index, _lineBreaker, line);
            }

            Content = sb.ToString();
        }

        // Sets multiple values for a specific key in a section.
        private void SetValues(string section, string key, params string[] values)
        {
            int valueIndex = 0;  // Track the index of the current value being processed.
            bool emptySection = string.IsNullOrEmpty(section);
            bool inSection = emptySection;
            Match lastMatch = null;  // Keep track of the last regex match found.
            StringBuilder sb = new StringBuilder(_content);  // Create a StringBuilder to modify the ini content.
            int offset = 0; // Offset to account for changes in length during replacements.


            // Iterate over the ini content and process each match for section and entry
            for (int i = 0; valueIndex < values.Length && i < _matches.Count; i++)
            {
                Match match = _matches[i];

                if (match.Groups["section"].Success)  // Check if the current match is a section.
                {
                    // Set the inSection flag based on whether the section matches the target section.
                    inSection = match.Groups["value"].Value.Equals(section, _comparison);
                    if (emptySection) break;  // If there is no section, break out of the loop.
                    continue;
                }

                // Check if inside the correct section and the current match is an entry.
                if (inSection && match.Groups["entry"].Success)
                {
                    lastMatch = match;

                    // Check if the key matches.
                    if (!match.Groups["key"].Value.Equals(key, _comparison))
                        continue;

                    // Get the group representing the value.
                    Group group = match.Groups["value"];

                    // Get the new value to insert.
                    string newValue = values[valueIndex++] ?? string.Empty;
                    string oldValue = group.Value;

                    // Calculate the index considering previous modifications.
                    int index = group.Index + offset;
                    int length = group.Length;

                    // Remove the old value and insert the new one.
                    sb = sb.Remove(index, length);
                    if (_allowEscapeChars) newValue = ToEscape(newValue);
                    sb = sb.Insert(index, newValue);

                    // Update the offset for future replacements.
                    offset += newValue.Length - oldValue.Length;
                }
            }

            // If there are still values left to be processed, append them at the end of the section.
            if (valueIndex < values.Length)
            {
                int index = 0;

                // If a previous match was found, insert after the last entry.
                if (lastMatch != null)
                {
                    index = lastMatch.Index + lastMatch.Length;
                }

                // If no match was found, append a new section header if necessary.
                else if (!emptySection)
                {
                    sb.Append(_lineBreaker);
                    sb.Append($"[{section}]{_lineBreaker}");
                    index = sb.Length;
                }

                // Insert the remaining values as new entries in the section.
                while (valueIndex < values.Length)
                {
                    // Obtaining the next value.
                    string value = values[valueIndex++];
                    if (_allowEscapeChars) value = ToEscape(value);  // Escape characters if allowed.

                    // Insert the new key-value pair into the content.
                    string line = $"{key}={value}";
                    InsertLine(sb, ref index, _lineBreaker, line);
                }
            }

            // Update the content with the modified StringBuilder content
            Content = sb.ToString();
        }

        // Returns a CultureInfo object that defines the string comparison rules for the specified StringComparison.
        private static CultureInfo GetCultureInfo(StringComparison comparison)
        {
            return comparison < StringComparison.InvariantCulture
                ? CultureInfo.CurrentCulture
                : CultureInfo.InvariantCulture;
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

        // Escape characters in the input string with backslashes.
        private static string ToEscape(string text)
        {
            int pos = 0;
            int inputLength = text.Length;

            if (inputLength == 0) return text;

            StringBuilder sb = new StringBuilder(inputLength * 2);
            do
            {
                char c = text[pos++];

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
        private static char UnHex(string hex)
        {
            int c = 0;
            for (int i = 0; i < hex.Length; i++)
            {
                int r = hex[i]; // Obtain next digit.
                if (r > 0x2F && r < 0x3A) r -= 0x30;
                else if (r > 0x40 && r < 0x47) r -= 0x37;
                else if (r > 0x60 && r < 0x67) r -= 0x57;
                else return '?';
                c = (c << 4) + r; // Insert next digit.
            }

            return (char)c;
        }

        // Converts any escaped characters in the input string.
        private static string UnEscape(string text)
        {
            int pos = -1;
            int inputLength = text.Length;

            if (inputLength == 0) return text;

            // Find the first occurrence of backslash or return the original text.
            for (int i = 0; i < inputLength; ++i)
            {
                if (text[i] == '\\')
                {
                    pos = i;
                    break;
                }
            }

            if (pos < 0) return text; // Backslash not found.

            // If backslash is found.
            StringBuilder sb = new StringBuilder(text.Substring(0, pos));

            do
            {
                char c = text[pos++];
                if (c == '\\')
                {
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
                        case 'u' when pos < inputLength - 3:
                            c = UnHex(text.Substring(++pos, 4));
                            pos += 3;
                            break;
                        case 'x' when pos < inputLength - 1:
                            c = UnHex(text.Substring(++pos, 2));
                            pos++;
                            break;
                        case 'c' when pos < inputLength:
                            c = text[++pos];
                            if (c >= 'a' && c <= 'z')
                                c -= ' ';
                            if ((c = (char)(c - 0x40U)) >= ' ')
                                c = '?';
                            break;
                        default:
                            sb.Append("\\" + c);
                            pos++;
                            continue;
                    }
                    pos++;
                }
                sb.Append(c);

            } while (pos < inputLength);

            return sb.ToString();
        }

        internal static bool IsNewLine(char c)
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

        // Tries to determine if either \r (carriage return) or \n (line feed) characters are present.
        // It stops iterating as soon as it finds both characters.
        private static string AutoDetectLineBreaker(string text)
        {
            if (string.IsNullOrEmpty(text)) return Environment.NewLine;

            bool r = false, n = false;

            // Searching for cr and lf characters.
            for (int index = 0; index < text.Length; index++)
            {
                char c = text[index];
                if (c == '\r') r = true;
                if (c == '\n') n = true;

                // If both carriage return and line feed were found, exit the loop.
                if (r && n) break;
            }

            // Determine the line break type based on the flags set.
            return n ? r ? "\r\n" : "\n" : r ? "\r" : Environment.NewLine;
        }

        // Tries to determine the encoding, checking the presence of signature (BOM) for some popular encodings.
        private static Encoding AutoDetectEncoding(string fileName, Encoding defaultEncoding = null)
        {
            byte[] bom = new byte[4];

            using (FileStream fs = File.OpenRead(fileName))
            {
                int count = fs.Read(bom, 0, 4);

                // Check for BOM (Byte Order Mark)
                if (count > 2)
                {
                    if (bom[0] == 0x2b && bom[1] == 0x2f && bom[2] == 0x76) return Encoding.UTF7;
                    if (bom[0] == 0xef && bom[1] == 0xbb && bom[2] == 0xbf) return Encoding.UTF8;
                    if (bom[0] == 0 && bom[1] == 0 && bom[2] == 0xfe && bom[3] == 0xff) return Encoding.UTF32;
                }
                else if (count > 1)
                {
                    if (bom[0] == 0xff && bom[1] == 0xfe) return Encoding.Unicode; // UTF-16LE
                    if (bom[0] == 0xfe && bom[1] == 0xff) return Encoding.BigEndianUnicode; // UTF-16BE
                }
            }

            // Default fallback.
            return defaultEncoding ?? Encoding.Default;
        }


        // Converts a string to lowercase based on the specified.
        private static string MayBeToLower(string text, StringComparison comparison)
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

        // Array containing the characters that are not allowed in path names.
        private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();

        // Checks whether the fileName string contains invalid characters for the path.
        private static bool IsInvalidPath(string fileName)
        {
            return fileName.Any(InvalidPathChar);
        }

        private static bool InvalidPathChar(char c)
        {
            return InvalidPathChars.Contains(c);
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


        /// <inheritdoc/>
        public override string ToString()
        {
            return Content;
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
                    if (int.TryParse(value, NumberStyles.Integer | NumberStyles.AllowHexSpecifier, _culture,
                            out int number))
                    {
                        return number != 0;
                    }

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
                        return Enum.Parse(type, value, ignoreCase: true);
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
                return Convert.FromHexString(value);
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
                        array.SetValue(tmpConv.ConvertFromString(null, _culture, value), i);
                    }
                    catch (Exception e)
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

            SetValues(section, key, values);
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
            }

            // If the element type is byte, write the value encoded with base64.
            if (elementType == typeof(byte))
            {
                byte[] bytes = (byte[])array;
                string value = Convert.ToHexString(bytes, 0, bytes.Length);
                WriteString(section, key, value);
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

            string section = GetDeclaringPath(property.DeclaringType);
            string key = property.Name;

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
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic);

            // Write settings for each property
            foreach (PropertyInfo property in properties)
            {
                WriteProperty(property, obj);
            }
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
            return Read(section, key, 0) > 0;
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
            return Read(section, key, defaultValue.ToString())[0];
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
    }
}
