/******************************************************************************

•   File: IniFileTest.cs

•   Description:

    Console test util for the IniFile class with no external dependencies.

    Tests are grouped by theme. Each group creates its own fixture, runs
    isolated assertions, and cleans up after itself. Logging verbosity is
    controlled by the first command-line argument:

        0  — only PASS / FAIL per test
        1  — details only on failure (default for CI)
        2  — expected and actual values for every test (default)
        3  — full dumps of expected and actual values as-is

•   License:

    This software is distributed under the MIT License (MIT)

    © 2024-2026 Pavel Bashkardin.

    See https://github.com/ng256/IniFile/blob/main/LICENSE for details.

******************************************************************************/

using System.ComponentModel;
using System.Dynamic;
using System.Ini;
using System.Reflection;
using System.Text;

namespace TestIni
{
    #region Test model classes

    [IniSection("Network")]
    public class NetworkSettings
    {
        [DefaultValue("localhost")]
        public string Host { get; set; } = "localhost";

        [DefaultValue(8080)]
        public int Port { get; set; } = 8080;

        [DefaultValue(30.5)]
        public double Timeout { get; set; } = 30.5;

        [DefaultValue(true)]
        public bool Enabled { get; set; } = true;

        [IniIgnore]
        public string Comment { get; set; } = "Network Settings";
    }

    [IniSection("Logging")]
    public class LoggingSettings
    {
        public string Level { get; set; } = "Info";
        public string FilePath { get; set; } = "log.txt";

        [IniIgnore]
        public string Comment { get; set; } = "Logging Settings";
    }

    public class WatchableSettings : INotifyPropertyChanged
    {
        private string _host = "initial";
        private int _port = 8080;

        [IniSection("WatchSection")]
        [IniEntry("Host")]
        public string Host
        {
            get => _host;
            set { _host = value; OnPropertyChanged(nameof(Host)); }
        }

        [IniSection("WatchSection")]
        [IniEntry("Port")]
        public int Port
        {
            get => _port;
            set { _port = value; OnPropertyChanged(nameof(Port)); }
        }

        [IniIgnore]
        public string RuntimeOnly { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public void RaiseAllChanged()
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    #endregion

    internal class Program
    {
        #region Constants and state

        private const int LogLevelMinimal = 0;
        private const int LogLevelErrors = 1;
        private const int LogLevelExpected = 2;
        private const int LogLevelVerbose = 3;

        private static int _logLevel = LogLevelVerbose;
        private static int _testNum = 0;
        private static int _testsPassed = 0;
        private static int _testsFailed = 0;

        private static readonly IniSettings _settings = new IniSettings
        {
            Comparison = StringComparison.InvariantCultureIgnoreCase,
            AllowEscapeChars = true,
            AllowMultiLine = true,
            AllowQuotedValues = true,
            Delimiters = IniDelimiterMode.Both,
            Comments = IniCommentMode.Both,
            AllowSpacesInKey = false
        };

        #endregion

        #region Entry point

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            if (args.Length > 0 && int.TryParse(args[0], out int level))
                _logLevel = Math.Max(LogLevelMinimal, Math.Min(LogLevelVerbose, level));

            PrintHeader();

            RunGroup("Basic Read", TestBasicRead);
            RunGroup("Basic Write", TestBasicWrite);
            RunGroup("Removal", TestRemoval);
            RunGroup("Indexer", TestIndexer);
            RunGroup("ContainsKey / ContainsSection", TestContainsKeyAndSection);
            RunGroup("Escaping", TestEscaping);
            RunGroup("Colon Delimiter", TestColonDelimiter);
            RunGroup("Quoted Values", TestQuotedValues);
            RunGroup("Undefined Text Modes", TestUndefinedTextModes);
            RunGroup("Duplicate Key Override", TestDuplicateKeyOverride);
            RunGroup("Radix Numbers", TestRadixNumbers);
            RunGroup("Environment Expansion", TestExpandedString);
            RunGroup("JSON Reading", TestJsonReading);
            RunGroup("JSON Path Navigation", TestJsonPath);
            RunGroup("JSON Writing", TestJsonWriting);
            RunGroup("Object Serialization", TestSerialization);
            RunGroup("WatchSettings", TestWatchSettings);
            RunGroup("Dictionary Round-trip", TestDictionaryRoundTrip);
            RunGroup("Byte Arrays", TestByteArrays);
            RunGroup("Justify", TestJustify);
            RunGroup("Invalid Input (isolated)", TestInvalidInput);
            RunGroup("File I/O Round-trip", TestFileIO);

            PrintSummary();

            RunBenchmarks();

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static void PrintHeader()
        {
            Console.WriteLine("=== IniFile Library Test Suite ===");
            Console.WriteLine($"Log level: {_logLevel} " +
                (_logLevel switch
                {
                    LogLevelMinimal => "(PASS/FAIL only)",
                    LogLevelErrors => "(errors only)",
                    LogLevelExpected => "(expected values)",
                    LogLevelVerbose => "(verbose dumps)",
                    _ => ""
                }));
            Console.WriteLine();
        }

        private static void PrintSummary()
        {
            Console.WriteLine();
            Console.WriteLine("=== Test Summary ===");
            Console.Write("Passed: ");
            WriteColored(_testsPassed.ToString(), ConsoleColor.Green);
            Console.Write(", Failed: ");
            WriteColored(_testsFailed.ToString(), ConsoleColor.Red);
            Console.WriteLine();
            Console.WriteLine(_testsFailed == 0 ? "All tests passed." : "Some tests failed.");
        }

        #endregion

        #region Test fixtures

        private static string CreateBasicContent() => @"
# Global comment
; Another global comment
global_key = global_value

[Section1]
key1 = value1
key2 = value2
key_num = 12345

[Section2]
key3 = value3
key4 = value4
";

        private static string CreateJsonContent() => @"
[App]
inline = {""name"":""test"",""value"":123}
multiline =
{
  ""array"": [1, 2, 3],
  ""nested"": {""flag"": true},
  // JSON comment
  ""text"": ""// not a comment""
}

[Paths]
data =
{
  ""root"": {
    ""nested"": { ""number"": 42, ""text"": ""hello"" },
    ""list"": [10, 20, 30],
    ""flag"": true
  }
}
";

        private static string CreateColonContent() => @"
[Colon]
key1 : value1
json : {""k"":""v""}
number : 42
";

        private static string CreateQuotedContent() =>
            "[Quotes]\n" +
            "double = \"hello world\"\n" +
            "single = 'goodbye world'\n" +
            "multiline = \"line1\nline2\nline3\"\n" +
            "trailing_comment = \"value\" ; comment after quote\n" +
            "escaped = \"quote \\\" inside\"\n";

        private static string CreateRadixContent() => @"
[Radix]
Hex = 0xFF
HexAmp = &hFF
HexDollar = $FF
HexSuffix = FFh
Binary = 0b1111
BinaryPercent = %1111
BinarySuffix = 1111b
Octal = 0o17
OctalSharp = 8#17
OctalSuffix = 17o
Decimal = 255
Negative = -42
";

        #endregion

        #region Test groups — Basic

        private static void TestBasicRead()
        {
            var ini = IniFile.Create(CreateBasicContent(), _settings);

            var sections = ini.ReadSections();
            Array.Sort(sections);
            RunTest(Next(), "ReadSections",
                new[] { "section1", "section2" }, sections,
                "list of all sections (lowercased)");

            var keys = ini.ReadKeys("Section1");
            Array.Sort(keys);
            RunTest(Next(), "ReadKeys(Section1)",
                new[] { "key_num", "key1", "key2" }, keys,
                "keys of Section1");

            RunTest(Next(), "ReadKeys(unknown)",
                new string[0], ini.ReadKeys("UnknownSection"),
                "empty list for missing section");

            RunTest(Next(), "ReadString(existing)",
                "value1", ini.ReadString("Section1", "key1", "fallback"),
                "existing key returns its value");

            RunTest(Next(), "ReadString(missing)",
                "fallback", ini.ReadString("Section1", "missing", "fallback"),
                "missing key returns default");

            RunTest(Next(), "ReadString(global)",
                "global_value", ini.ReadString(null, "global_key", ""),
                "global entry above sections");

            RunTest(Next(), "ReadStrings(missing)",
                new string[0], ini.ReadStrings("Section1", "missing"),
                "empty array for missing multi-value key");
        }

        private static void TestBasicWrite()
        {
            var ini = IniFile.Create(CreateBasicContent(), _settings);

            ini.WriteString("Section1", "key1", "updated");
            RunTest(Next(), "WriteString(update)",
                "updated", ini.ReadString("Section1", "key1", ""),
                "existing value updated in place");

            ini.WriteInt32("Section1", "new_int", 999);
            RunTest(Next(), "WriteInt32(new key)",
                999, ini.ReadInt32("Section1", "new_int", 0),
                "new integer key created");

            ini.WriteBoolean("Section1", "flag", true);
            RunTest(Next(), "WriteBoolean(new key)",
                true, ini.ReadBoolean("Section1", "flag", false),
                "new boolean key created");

            ini.WriteStrings("Section1", "arr", "a", "b", "c");
            RunTest(Next(), "WriteStrings(multi-value)",
                new[] { "a", "b", "c" }, ini.ReadStrings("Section1", "arr"),
                "multi-value key round-trip");

            ini.WriteString(null, "global_new", "global value");
            RunTest(Next(), "WriteString(global)",
                "global value", ini.ReadString(null, "global_new", ""),
                "global entry created");

            ini.WriteString("BrandNew", "k", "v");
            RunTest(Next(), "WriteString(new section)",
                "v", ini.ReadString("BrandNew", "k", ""),
                "new section created on demand");
        }

        private static void TestRemoval()
        {
            var ini = IniFile.Create(CreateBasicContent(), _settings);
            ini.WriteStrings("Section1", "multi", "1", "2", "3");

            ini.RemoveKey("Section1", "key1");
            RunTest(Next(), "RemoveKey",
                false, ini.ContainsKey("Section1", "key1"),
                "first occurrence removed");

            ini.RemoveKeys("Section1", "multi");
            RunTest(Next(), "RemoveKeys",
                new string[0], ini.ReadStrings("Section1", "multi"),
                "all occurrences removed");

            ini.RemoveSection("Section1");
            RunTest(Next(), "RemoveSection",
                false, ini.ContainsSection("Section1"),
                "entire section removed");

            bool missingSectionOk = true;
            try { ini.RemoveSection("NoSuchSection"); }
            catch { missingSectionOk = false; }
            RunTest(Next(), "RemoveSection(missing)",
                true, missingSectionOk,
                "no exception on missing section");
        }

        private static void TestIndexer()
        {
            var ini = IniFile.Create(CreateBasicContent(), _settings);

            RunTest(Next(), "Indexer get",
                "value1", ini["Section1", "key1"],
                "indexer returns value");

            ini["Section1", "key1"] = "indexer_value";
            RunTest(Next(), "Indexer set",
                "indexer_value", ini.ReadString("Section1", "key1", ""),
                "indexer writes value");

            RunTest(Next(), "Indexer get with default",
                "fallback", ini["Section1", "missing", "fallback"],
                "indexer returns default for missing key");
        }

        private static void TestContainsKeyAndSection()
        {
            var ini = IniFile.Create(CreateBasicContent(), _settings);

            RunTest(Next(), "ContainsSection(existing)",
                true, ini.ContainsSection("Section1"),
                "existing section found");

            RunTest(Next(), "ContainsSection(missing)",
                false, ini.ContainsSection("NoSection"),
                "missing section not found");

            RunTest(Next(), "ContainsSection(null)",
                false, ini.ContainsSection(null),
                "null is not a section");

            RunTest(Next(), "ContainsSection(empty)",
                false, ini.ContainsSection(""),
                "empty name is not a section");

            RunTest(Next(), "ContainsKey(existing)",
                true, ini.ContainsKey("Section1", "key1"),
                "existing key found");

            RunTest(Next(), "ContainsKey(missing)",
                false, ini.ContainsKey("Section1", "no_key"),
                "missing key not found");

            RunTest(Next(), "ContainsKey(global)",
                true, ini.ContainsKey(null, "global_key"),
                "global entry found");

            RunTest(Next(), "ContainsKey(missing section)",
                false, ini.ContainsKey("NoSection", "key1"),
                "no key in missing section");
        }

        private static void TestEscaping()
        {
            var ini = IniFile.Create(_settings);
            ini.WriteString("S", "esc", "Line1\nLine2\tTabbed");
            RunTest(Next(), "WriteString with escapes",
                "Line1\nLine2\tTabbed", ini.ReadString("S", "esc", ""),
                "escaped value round-trip");

            ini.WriteString("S", "backslash", "path\\to\\file");
            RunTest(Next(), "WriteString with backslash",
                "path\\to\\file", ini.ReadString("S", "backslash", ""),
                "backslash preserved");

            ini.WriteString("S", "quote", "he said \"hi\"");
            RunTest(Next(), "WriteString with quote",
                "he said \"hi\"", ini.ReadString("S", "quote", ""),
                "quote preserved");

            var noEsc = IniFile.Create(new IniSettings
            {
                Comparison = StringComparison.InvariantCultureIgnoreCase,
                AllowEscapeChars = false
            });
            noEsc.WriteString("S", "raw", @"literal\nvalue");
            RunTest(Next(), "No escapes - literal \\n stored",
                @"literal\nvalue", noEsc.ReadString("S", "raw", ""),
                "escape sequence not processed");
        }

        private static void TestColonDelimiter()
        {
            var ini = IniFile.Create(CreateColonContent(), _settings);

            RunTest(Next(), "ReadString with ':'",
                "value1", ini.ReadString("Colon", "key1", ""),
                "key : value is parsed");

            RunTest(Next(), "ReadJsonString with ':'",
                "{\"k\":\"v\"}", ini.ReadJsonString("Colon", "json", defaultValue: ""),
                "JSON after ':' delimiter");

            RunTest(Next(), "ReadInt32 with ':'",
                42, ini.ReadInt32("Colon", "number", 0),
                "integer after ':' delimiter");

            var keys = ini.ReadKeys("Colon");
            Array.Sort(keys);
            RunTest(Next(), "ReadKeys with ':'",
                new[] { "json", "key1", "number" }, keys,
                "all colon-delimited keys found");
        }

        private static void TestQuotedValues()
        {
            var ini = IniFile.Create(CreateQuotedContent(), _settings);

            RunTest(Next(), "Double-quoted single-line",
                "hello world", ini.ReadString("Quotes", "double", ""),
                "double quotes stripped");

            RunTest(Next(), "Single-quoted single-line",
                "goodbye world", ini.ReadString("Quotes", "single", ""),
                "single quotes stripped");

            RunTest(Next(), "Double-quoted multi-line",
                "line1\nline2\nline3",
                Normalize(ini.ReadString("Quotes", "multiline", "")),
                "multi-line content preserved");

            RunTest(Next(), "Trailing comment after quote",
                "value", ini.ReadString("Quotes", "trailing_comment", ""),
                "comment after closing quote ignored");

            RunTest(Next(), "Escaped quote inside",
                "quote \" inside", ini.ReadString("Quotes", "escaped", ""),
                "escaped quote preserved");

            var noQuotes = IniFile.Create(
                "[S]\nkey = \"literal quotes\"\n",
                new IniSettings { AllowQuotedValues = false });
            RunTest(Next(), "AllowQuotedValues=false",
                "\"literal quotes\"", noQuotes.ReadString("S", "key", ""),
                "quotes preserved literally");
        }

        #endregion

        #region Test groups — Parsing modes

        private static void TestUndefinedTextModes()
        {
            {
                var ini = IniFile.Create("flag1\nflag2",
                    new IniSettings { UndefinedText = IniUndefinedTextMode.Ignore });
                RunTest(Next(), "UndefinedTextMode.Ignore",
                    new string[0], ini.ReadKeys(),
                    "bare words are not captured as keys");
            }

            {
                var ini = IniFile.Create("flag1\nkey2=val2\nflag3",
                    new IniSettings { UndefinedText = IniUndefinedTextMode.Key });
                var keys = ini.ReadKeys();
                Array.Sort(keys);
                RunTest(Next(), "UndefinedTextMode.Key (keys)",
                    new[] { "flag1", "flag3", "key2" }, keys,
                    "bare words and normal entries captured");

                RunTest(Next(), "UndefinedTextMode.Key (values)",
                    new[] { "", "val2", "" },
                    new[]
                    {
                        ini.ReadString(null, "flag1", null),
                        ini.ReadString(null, "key2", null),
                        ini.ReadString(null, "flag3", null)
                    },
                    "flags have empty values, normal entry has value");
            }

            {
                var ini = IniFile.Create("value1\nvalue2",
                    new IniSettings { UndefinedText = IniUndefinedTextMode.Value });
                RunTest(Next(), "UndefinedTextMode.Value (first)",
                    "value1", ini.ReadString(null, "", ""),
                    "first value returned by ReadString");
                RunTest(Next(), "UndefinedTextMode.Value (all)",
                    new[] { "value1", "value2" }, ini.ReadStrings(null, ""),
                    "ReadStrings returns both values");
            }

            {
                var ini = IniFile.Create("my flag\nkey = value",
                    new IniSettings
                    {
                        UndefinedText = IniUndefinedTextMode.Key,
                        AllowSpacesInKey = true
                    });
                RunTest(Next(), "UndefinedTextMode.Key with spaces",
                    true, ini.ContainsKey(null, "my flag"),
                    "flag with space captured as key");
                RunTest(Next(), "UndefinedTextMode.Key mixed",
                    "value", ini.ReadString(null, "key", ""),
                    "normal entry still works");
            }
        }

        private static void TestDuplicateKeyOverride()
        {
            string content = "key=first\nkey=second";

            {
                var ini = IniFile.Create(content,
                    new IniSettings { DuplicateKeyOverride = false });
                RunTest(Next(), "DuplicateKeyOverride=false (ReadString)",
                    "first", ini.ReadString(null, "key", ""),
                    "first occurrence returned");
            }

            {
                var ini = IniFile.Create(content,
                    new IniSettings { DuplicateKeyOverride = true });
                RunTest(Next(), "DuplicateKeyOverride=true (ReadString)",
                    "second", ini.ReadString(null, "key", ""),
                    "last occurrence returned");
            }

            {
                var ini = IniFile.Create(content,
                    new IniSettings { DuplicateKeyOverride = true });
                RunTest(Next(), "DuplicateKeyOverride (ReadStrings unaffected)",
                    new[] { "first", "second" }, ini.ReadStrings(null, "key"),
                    "ReadStrings always returns all");
            }
        }

        private static void TestRadixNumbers()
        {
            var ini = IniFile.Create(CreateRadixContent(), _settings);

            void Check(string key, int expected, string note)
                => RunTest(Next(), $"ReadInt32 {note}", expected,
                    ini.ReadInt32("Radix", key, -1), note);

            Check("Hex", 255, "0x hex");
            Check("HexAmp", 255, "&h hex");
            Check("HexDollar", 255, "$ hex");
            Check("HexSuffix", 255, "hex suffix 'h'");
            Check("Binary", 15, "0b binary");
            Check("BinaryPercent", 15, "% binary");
            Check("BinarySuffix", 15, "binary suffix 'b'");
            Check("Octal", 15, "0o octal");
            Check("OctalSharp", 15, "8# octal");
            Check("OctalSuffix", 15, "octal suffix 'o'");
            Check("Decimal", 255, "decimal");
            Check("Negative", -42, "negative decimal");
        }

        #endregion

        #region Test groups — Environment and JSON

        private static void TestExpandedString()
        {
            {
                var ini = IniFile.Create("[E]\npath = %TEMP%\\app.log");
                string path = ini.ReadExpandedString("E", "path", "");
                RunTest(Next(), "ReadExpandedString (%TEMP%)",
                    true,
                    !path.Contains("%TEMP%") && path.EndsWith("app.log"),
                    "variable expanded, suffix preserved");
            }

            {
                var ini = IniFile.Create("[E]\nval = %NOT_A_REAL_VAR%");
                RunTest(Next(), "ReadExpandedString (unknown var)",
                    "%NOT_A_REAL_VAR%", ini.ReadExpandedString("E", "val", ""),
                    "invalid variable left as-is");
            }

            {
                var ini = IniFile.Create("[E]\nval = %DATE%");
                string val = ini.ReadExpandedString("E", "val", "");
                bool ok = val.Length == 8 && int.TryParse(val, out _);
                RunTest(Next(), "ReadExpandedString (%DATE%)",
                    true, ok, "8-digit date produced");
            }

            {
                var ini = IniFile.Create("[E]\npath = %TEMP%\\app.log");
                RunTest(Next(), "ReadString leaves %TEMP% intact",
                    true, ini.ReadString("E", "path", "").StartsWith("%TEMP%"),
                    "no expansion for plain read");
            }
        }

        private static void TestJsonReading()
        {
            var ini = IniFile.Create(CreateJsonContent(), _settings);

            RunTest(Next(), "ReadJsonString (inline)",
                "{\"name\":\"test\",\"value\":123}",
                ini.ReadJsonString("App", "inline", defaultValue: ""),
                "inline JSON string returned as-is");

            {
                string raw = ini.ReadJsonString("App", "multiline", defaultValue: "");
                string normalized = Normalize(raw);
                bool hasComment = normalized.Contains("// JSON comment");
                bool hasString = normalized.Contains("\"text\": \"// not a comment\"");
                RunTest(Next(), "ReadJsonString (multiline)",
                    true, hasComment && hasString,
                    "comments and strings preserved verbatim");
            }

            {
                var obj = ini.ReadJsonObject("App", "inline");
                bool ok = obj is IDictionary<string, object> d
                    && d.ContainsKey("name") && (string)d["name"] == "test"
                    && d.ContainsKey("value") && Convert.ToInt32(d["value"]) == 123;
                RunTest(Next(), "ReadJsonObject (inline)",
                    true, ok, "dictionary with expected keys");
            }

            {
                var obj = ini.ReadJsonObject("App", "multiline");
                bool ok = false;
                if (obj is IDictionary<string, object> d)
                {
                    var arr = d["array"] as object[];
                    var nested = d["nested"] as IDictionary<string, object>;
                    ok = arr != null && arr.Length == 3
                        && nested != null && Convert.ToBoolean(nested["flag"])
                        && (string)d["text"] == "// not a comment";
                }
                RunTest(Next(), "ReadJsonObject (multiline nested)",
                    true, ok, "arrays, nested objects, and strings parsed");
            }

            {
                dynamic dyn = ini.ReadJsonDynamicObject("Paths", "data");
                int number = Convert.ToInt32(dyn.root.nested.number);
                RunTest(Next(), "ReadJsonDynamicObject (nested access)",
                    42, number, "dynamic access at depth");
            }

            RunTest(Next(), "ReadJsonObject (missing key)",
                "default", ini.ReadJsonObject("App", "missing", defaultValue: "default"),
                "default value returned");

            {
                var bad = IniFile.Create("[S]\njson = not-a-json");
                RunTest(Next(), "ReadJsonObject (invalid JSON)",
                    "default", bad.ReadJsonObject("S", "json", defaultValue: "default"),
                    "no exception, default returned");
            }
        }

        private static void TestJsonPath()
        {
            var ini = IniFile.Create(CreateJsonContent(), _settings);

            RunTest(Next(), "ReadJsonObject(path) number",
                42, ini.ReadJsonObject("Paths", "data", "root/nested/number", -1),
                "root/nested/number");

            RunTest(Next(), "ReadJsonObject(path) string",
                "hello", ini.ReadJsonObject("Paths", "data", "root/nested/text", "?"),
                "root/nested/text");

            RunTest(Next(), "ReadJsonObject(path) bool",
                true, ini.ReadJsonObject("Paths", "data", "root/flag", false),
                "root/flag");

            RunTest(Next(), "ReadJsonObject(path) array[1]",
                20, ini.ReadJsonObject("Paths", "data", "root/list/1", -1),
                "root/list/1");

            RunTest(Next(), "ReadJsonObject(path) array[0x2]",
                30, ini.ReadJsonObject("Paths", "data", "root/list/0x2", -1),
                "hex index in array path");

            RunTest(Next(), "ReadJsonObject(path) array[0b1]",
                20, ini.ReadJsonObject("Paths", "data", "root/list/0b1", -1),
                "binary index in array path");

            RunTest(Next(), "ReadJsonObject(path) missing",
                "fallback", ini.ReadJsonObject("Paths", "data", "root/missing", "fallback"),
                "missing path returns default");

            RunTest(Next(), "ReadJsonObject(path) out of range",
                "oob", ini.ReadJsonObject("Paths", "data", "root/list/99", "oob"),
                "out-of-range index returns default");

            RunTest(Next(), "ReadJsonObject(path) into primitive",
                "no", ini.ReadJsonObject("Paths", "data", "root/flag/x", "no"),
                "cannot descend into primitive");

            RunTest(Next(), "ReadJsonString(path)",
                "42", ini.ReadJsonString("Paths", "data", "root/nested/number", "?"),
                "raw primitive fragment");

            {
                dynamic subtree = ini.ReadJsonDynamicObject("Paths", "data", "root/nested");
                int number = Convert.ToInt32(subtree.number);
                RunTest(Next(), "ReadJsonDynamicObject(path)",
                    42, number, "nested.number via dynamic");
            }
        }

        private static void TestJsonWriting()
        {
            {
                var ini = IniFile.Create("[S]\njson = {}");
                ini.WriteJsonObject("S", "json",
                    new Dictionary<string, object>
                    {
                        ["name"] = "test",
                        ["value"] = 42
                    });
                object name = ini.ReadJsonObject("S", "json", "name", "");
                object value = ini.ReadJsonObject("S", "json", "value", -1);
                RunTest(Next(), "WriteJsonObject (dictionary)",
                    true, "test".Equals(name) && 42 == Convert.ToInt32(value),
                    "dictionary serialized and re-read");
            }

            {
                var ini = IniFile.Create("[S]\njson = {}");
                dynamic d = new ExpandoObject();
                d.enabled = true;
                d.count = 3;
                ini.WriteJsonDynamicObject("S", "json", d);
                object enabled = ini.ReadJsonObject("S", "json", "enabled", false);
                object count = ini.ReadJsonObject("S", "json", "count", -1);
                RunTest(Next(), "WriteJsonDynamicObject",
                    true, Convert.ToBoolean(enabled) && Convert.ToInt32(count) == 3,
                    "dynamic object serialized");
            }

            {
                var ini = IniFile.Create("[S]\njson = { \"root\": { \"value\": 1 } }");
                ini.WriteJsonObject("S", "json", "root/value", 42);
                object value = ini.ReadJsonObject("S", "json", "root/value", -1);
                RunTest(Next(), "WriteJsonObject(path) update",
                    42, value, "existing value updated in place");
            }

            {
                var ini = IniFile.Create("[S]\njson = { \"root\": {} }");
                ini.WriteJsonObject("S", "json", "root/new/deep", "x");
                object value = ini.ReadJsonObject("S", "json", "root/new/deep", "?");
                RunTest(Next(), "WriteJsonObject(path) create",
                    "x", value, "missing subtrees created automatically");
            }

            {
                var ini = IniFile.Create("[S]\njson = { \"items\": [1, 2, 3] }");
                ini.WriteJsonObject("S", "json", "items/1", 99);
                object value = ini.ReadJsonObject("S", "json", "items/1", -1);
                RunTest(Next(), "WriteJsonObject(path) array",
                    99, value, "array element updated");
            }

            {
                var ini = IniFile.Create("[S]\njson = { \"root\": {} }");
                dynamic patch = new ExpandoObject();
                patch.enabled = true;
                patch.count = 3;
                ini.WriteJsonDynamicObject("S", "json", "root/options", patch);
                object enabled = ini.ReadJsonObject("S", "json", "root/options/enabled", false);
                object count = ini.ReadJsonObject("S", "json", "root/options/count", -1);
                RunTest(Next(), "WriteJsonDynamicObject(path)",
                    true, Convert.ToBoolean(enabled) && Convert.ToInt32(count) == 3,
                    "dynamic object written at path");
            }

            {
                var ini = IniFile.Create("[S]\njson = { \"root\": 1 }");
                ini.WriteJsonObject("S", "json", "root/child", "x");
                object value = ini.ReadJsonObject("S", "json", "root", "?");
                RunTest(Next(), "WriteJsonObject(path) into primitive",
                    1, value, "no change on invalid target");
            }

            {
                var ini = IniFile.Create("[S]\njson = { \"items\": [1, 2] }");
                ini.WriteJsonObject("S", "json", "items/99", 999);
                object arr = ini.ReadJsonObject("S", "json", "items");
                int len = (arr as object[])?.Length ?? -1;
                RunTest(Next(), "WriteJsonObject(path) out of range",
                    2, len, "array length unchanged on out-of-range index");
            }
        }

        #endregion

        #region Test groups — Serialization, tracking, dictionaries

        private static void TestSerialization()
        {
            var ini = IniFile.Create(_settings);
            var net = new NetworkSettings
            {
                Host = "testhost",
                Port = 1234,
                Timeout = 60.0,
                Enabled = false
            };
            var log = new LoggingSettings
            {
                Level = "Debug",
                FilePath = "debug.log"
            };

            ini.WriteSettings(net);
            ini.WriteSettings(log);

            var netRead = new NetworkSettings();
            var logRead = new LoggingSettings();
            ini.ReadSettings(netRead);
            ini.ReadSettings(logRead);

            RunTest(Next(), "NetworkSettings round-trip",
                true,
                netRead.Host == "testhost"
                && netRead.Port == 1234
                && Math.Abs(netRead.Timeout - 60.0) < 1e-9
                && netRead.Enabled == false,
                "Host/Port/Timeout/Enabled preserved");

            RunTest(Next(), "LoggingSettings round-trip",
                true,
                logRead.Level == "Debug" && logRead.FilePath == "debug.log",
                "Level/FilePath preserved");

            RunTest(Next(), "IniIgnore excluded",
                false, ini.ContainsKey("Network", "Comment"),
                "[IniIgnore] property not written");
        }

        private static void TestWatchSettings()
        {
            {
                var ini = IniFile.Create(_settings);
                var model = new WatchableSettings();
                ini.WriteSettings(model);

                using (ini.WatchSettings(model))
                {
                    model.Host = "changed";
                    model.Port = 9090;
                    model.RuntimeOnly = "ignored";
                }

                string host = ini.ReadString("WatchSection", "Host", "");
                int port = ini.ReadInt32("WatchSection", "Port", 0);
                RunTest(Next(), "WatchSettings (basic)",
                    true,
                    host == "changed" && port == 9090
                    && !ini.ContainsKey("WatchSection", "RuntimeOnly"),
                    "changes persisted, ignored property untouched");
            }

            {
                var ini = IniFile.Create(_settings);
                var model = new WatchableSettings();
                ini.WriteSettings(model);

                var watcher = ini.WatchSettings(model);
                model.Host = "during";
                watcher.Dispose();
                model.Host = "after";

                RunTest(Next(), "WatchSettings (after Dispose)",
                    "during", ini.ReadString("WatchSection", "Host", ""),
                    "post-dispose changes not written");
            }

            {
                var ini = IniFile.Create(_settings);
                var model = new WatchableSettings();
                ini.WriteSettings(model);

                using (ini.WatchSettings(model))
                {
                    typeof(WatchableSettings)
                        .GetProperty(nameof(WatchableSettings.Host))
                        .SetValue(model, "bulk");
                    typeof(WatchableSettings)
                        .GetProperty(nameof(WatchableSettings.Port))
                        .SetValue(model, 7777);
                    model.RaiseAllChanged();
                }

                string host = ini.ReadString("WatchSection", "Host", "");
                int port = ini.ReadInt32("WatchSection", "Port", 0);
                RunTest(Next(), "WatchSettings (blanket notification)",
                    true, host == "bulk" && port == 7777,
                    "all tracked properties written");
            }
        }

        private static void TestDictionaryRoundTrip()
        {
            var ini = IniFile.Create(CreateBasicContent(), _settings);

            var snapshot = ini.ExportToDictionary();

            RunTest(Next(), "ExportToDictionary (sections)",
                true, snapshot.ContainsKey("section1") && snapshot.ContainsKey("section2"),
                "both sections exported");

            RunTest(Next(), "ExportToDictionary (global)",
                true, snapshot.ContainsKey(""),
                "global entries under empty key");

            {
                var target = IniFile.Create(_settings);
                target.ImportFromDictionary(snapshot);
                bool ok = target.ContainsSection("Section1")
                    && target.ReadString("Section1", "key1", "") == "value1"
                    && target.ReadString(null, "global_key", "") == "global_value";
                RunTest(Next(), "ImportFromDictionary (merge)",
                    true, ok, "values imported into empty instance");
            }

            {
                var target = IniFile.Create(_settings);
                target.WriteString("Old", "k", "v");
                target.ImportFromDictionary(snapshot, replace: true);
                RunTest(Next(), "ImportFromDictionary (replace)",
                    true,
                    !target.ContainsSection("Old") && target.ContainsSection("Section1"),
                    "old content cleared, new content loaded");
            }

            {
                var target = IniFile.Create(_settings);
                target.WriteStrings("S", "k", "a", "b", "c");
                var data = new Dictionary<string, Dictionary<string, List<string>>>
                {
                    ["S"] = new Dictionary<string, List<string>> { ["k"] = new List<string>() }
                };
                target.ImportFromDictionary(data);
                RunTest(Next(), "ImportFromDictionary (empty list removes)",
                    0, target.ReadStrings("S", "k").Length,
                    "all occurrences removed");
            }
        }

        private static void TestByteArrays()
        {
            var ini = IniFile.Create(_settings);
            byte[] bytes = { 0x01, 0x02, 0x03, 0x04, 0xFF };

            ini.WriteArray("S", "bytes", bytes);
            byte[] read = ini.ReadArray<byte>("S", "bytes");

            RunTest(Next(), "byte[] round-trip",
                bytes, read, "hex-encoded byte array");

            ini.WriteArray("S", "empty", Array.Empty<byte>());
            byte[] empty = ini.ReadArray<byte>("S", "empty");
            RunTest(Next(), "byte[] empty round-trip",
                0, empty.Length, "empty array handled");
        }

        private static void TestJustify()
        {
            string content = "\n" +
                "# Comment to be removed\n" +
                "; Another comment\n" +
                "   global_key1 = value1   \n" +
                "global_key2 = value2\n" +
                "\n" +
                "[SectionA]  \n" +
                "   key1 = val1\n" +
                "key2 = val2\n" +
                "\n" +
                "key3 = val3\n" +
                "\n" +
                "[SectionB]\n" +
                "key1 = multi\n" +
                "key1 = multi2\n";

            var ini = IniFile.Create(content, _settings);
            string justified = Normalize(ini.Justify());

            string expected = "global_key1=value1\n" +
                "global_key2=value2\n" +
                "\n" +
                "[SectionA]\n" +
                "key1=val1\n" +
                "key2=val2\n" +
                "key3=val3\n" +
                "\n" +
                "[SectionB]\n" +
                "key1=multi\n" +
                "key1=multi2";

            RunTest(Next(), "Justify (compact form)",
                expected, justified.TrimEnd('\n'),
                "comments and blank lines removed");

            {
                var colonIni = IniFile.Create(
                    "[S]\nkey:value",
                    new IniSettings { Delimiters = IniDelimiterMode.Colon });
                string result = Normalize(colonIni.Justify()).TrimEnd('\n');
                RunTest(Next(), "Justify (colon delimiter)",
                    "[S]\nkey:value", result,
                    "delimiter from settings applied");
            }

            {
                var i2 = IniFile.Create("[S]\n# c\nkey = value");
                string original = i2.Content;
                i2.Justify();
                RunTest(Next(), "Justify (content unchanged)",
                    true, i2.Content == original,
                    "Content not modified by Justify");
            }
        }

        #endregion

        #region Test groups — Invalid input, file I/O

        private static void TestInvalidInput()
        {
            {
                var ini = IniFile.Create(_settings);
                RunTestThrows(Next(), "ReadString(null key)",
                    "ArgumentNullException",
                    () => ini.ReadString("S", null));
            }

            {
                var ini = IniFile.Create(_settings);
                RunTestThrows(Next(), "WriteString(null key)",
                    "ArgumentNullException",
                    () => ini.WriteString("S", null, "v"));
            }

            {
                var ini = IniFile.Create("[S]\njson = {}");
                RunTestThrows(Next(), "ReadJsonObject(null path)",
                    "ArgumentNullException",
                    () => ini.ReadJsonObject("S", "json", null, "d"));
            }

            {
                var ini = IniFile.Create("[S]\njson = {}");
                RunTestThrows(Next(), "WriteJsonObject(null path)",
                    "ArgumentNullException",
                    () => ini.WriteJsonObject("S", "json", null, "v"));
            }

            {
                var ini = IniFile.Create("[S]\njson = { \"a\": 1 }");
                RunTestNoThrow(Next(), "ReadJsonObject(empty path)",
                    "default", () => ini.ReadJsonObject("S", "json", "", "default"),
                    "empty path returns default");
            }

            {
                var ini = IniFile.Create(_settings);
                RunTestNoThrow(Next(), "ReadString(missing section)",
                    "default", () => ini.ReadString("NoSection", "k", "default"),
                    "missing section returns default");
            }

            {
                var ini = IniFile.Create("[S]\nnum = not_a_number");
                RunTestNoThrow(Next(), "ReadInt32(invalid text)",
                    0, () => ini.ReadInt32("S", "num", 0),
                    "invalid number returns default");
            }

            {
                var ini = IniFile.Create("[S]\njson = { \"a\": ");
                RunTestNoThrow(Next(), "ReadJsonObject(unbalanced JSON)",
                    "default", () => ini.ReadJsonObject("S", "json", defaultValue: "default"),
                    "malformed JSON returns default");
            }

            {
                var ini = IniFile.Create("[S]\njson = { \"a\": 1 }");
                RunTestNoThrow(Next(), "ReadJsonObject(path into primitive)",
                    "default",
                    () => ini.ReadJsonObject("S", "json", "a/b/c", defaultValue: "default"),
                    "cannot descend into primitive");
            }

            {
                RunTestThrows(Next(), "Load(null filename)",
                    "ArgumentNullException",
                    () => IniFile.Load((string)null));
            }

            {
                RunTestThrows(Next(), "Load(whitespace filename)",
                    "ArgumentException",
                    () => IniFile.Load("   "));
            }

            {
                var ini = IniFile.Create(_settings);
                RunTestThrows(Next(), "RemoveKey(null key)",
                    "ArgumentNullException",
                    () => ini.RemoveKey("S", null));
            }

            {
                var ini = IniFile.Create(_settings);
                ini.WriteString("S", "k", "v");
                RunTest(Next(), "State intact after isolated failures",
                    "v", ini.ReadString("S", "k", ""),
                    "unaffected by invalid-input tests");
            }
        }

        private static void TestFileIO()
        {
            string path = "test_io_" + Guid.NewGuid().ToString("N") + ".ini";
            try
            {
                var ini1 = IniFile.Create(_settings);
                ini1.WriteString("S", "k", "v1");
                ini1.WriteInt32("S", "n", 42);
                ini1.Save(path);

                RunTest(Next(), "Save created file",
                    true, File.Exists(path),
                    "file written to disk");

                var ini2 = IniFile.Load(path, _settings);
                RunTest(Next(), "Load restores values",
                    true,
                    ini2.ReadString("S", "k", "") == "v1"
                    && ini2.ReadInt32("S", "n", 0) == 42,
                    "string and integer values persisted");

                ini2.WriteString("S", "k", "v2");
                ini2.Save(path);

                var ini3 = IniFile.Load(path, _settings);
                RunTest(Next(), "Round-trip modification",
                    "v2", ini3.ReadString("S", "k", ""),
                    "updated value persisted");
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        #endregion

        #region Test helpers

        private static int Next() => ++_testNum;

        private static void RunGroup(string name, Action group)
        {
            WriteColored($"--- {name} ---{Environment.NewLine}", ConsoleColor.Cyan);
            try
            {
                group();
            }
            catch (Exception ex)
            {
                _testsFailed++;
                WriteColored("[GROUP FAIL] ", ConsoleColor.Red);
                Console.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine();
            }
        }

        private static void RunTest(int num, string name, object expected, object actual, string description)
        {
            bool passed;
            string exceptionMessage = null;
            try
            {
                passed = DeepEquals(expected, actual);
            }
            catch (Exception ex)
            {
                passed = false;
                exceptionMessage = $"{ex.GetType().Name}: {ex.Message}";
            }

            ReportResult(num, name, description, passed, expected, actual, exceptionMessage);
        }

        private static void RunTestThrows(int num, string name, string expectedType, Action action)
        {
            string actualType;
            try
            {
                action();
                actualType = "no exception";
            }
            catch (Exception ex)
            {
                actualType = ex.GetType().Name;
            }
            RunTest(num, name, expectedType, actualType, "throws " + expectedType);
        }

        private static void RunTestNoThrow(int num, string name, object expected, Func<object> action, string description)
        {
            object actual;
            try
            {
                actual = action();
            }
            catch (Exception ex)
            {
                actual = $"EXCEPTION: {ex.GetType().Name}: {ex.Message}";
            }
            RunTest(num, name, expected, actual, description);
        }

        private static void ReportResult(int num, string name, string description, bool passed,
            object expected, object actual, string exceptionMessage)
        {
            if (passed) _testsPassed++; else _testsFailed++;

            WriteColored(passed ? "[PASS " : "[FAIL ", passed ? ConsoleColor.Green : ConsoleColor.Red);
            Console.Write($"#{num}] ");
            Console.ResetColor();
            Console.WriteLine($"{name} - {description}");

            bool showDetails;
            switch (_logLevel)
            {
                case LogLevelMinimal:
                    showDetails = false;
                    break;
                case LogLevelErrors:
                    showDetails = !passed || exceptionMessage != null;
                    break;
                default:
                    showDetails = true;
                    break;
            }

            if (showDetails)
            {
                int maxLen = _logLevel >= LogLevelVerbose ? 0 : 80;
                Console.WriteLine($"  Expected: {FormatValue(expected, maxLen)}");
                Console.WriteLine($"  Actual:   {FormatValue(actual, maxLen)}");
                if (exceptionMessage != null)
                    Console.WriteLine($"  Exception: {exceptionMessage}");
            }

            if (passed || _logLevel >= LogLevelErrors)
                Console.WriteLine();
        }

        private static bool DeepEquals(object expected, object actual)
        {
            if (ReferenceEquals(expected, actual)) return true;
            if (expected == null || actual == null) return false;

            // Cross-type numeric comparison (e.g. int 42 vs double 42.0).
            if (IsNumeric(expected) && IsNumeric(actual))
            {
                double e = Convert.ToDouble(expected);
                double a = Convert.ToDouble(actual);
                return Math.Abs(e - a) < 1e-9;
            }

            // Arrays: element-wise comparison.
            if (expected is Array ae && actual is Array aa)
            {
                if (ae.Length != aa.Length) return false;
                for (int i = 0; i < ae.Length; i++)
                    if (!DeepEquals(ae.GetValue(i), aa.GetValue(i)))
                        return false;
                return true;
            }

            return expected.Equals(actual);
        }

        private static bool IsNumeric(object value)
        {
            return value is byte || value is sbyte
                || value is short || value is ushort
                || value is int || value is uint
                || value is long || value is ulong
                || value is float || value is double || value is decimal;
        }

        private static string FormatValue(object value, int maxLen)
        {
            if (value == null) return "null";

            string s;
            if (value is Array arr)
            {
                var parts = new List<string>();
                foreach (object item in arr)
                    parts.Add(item?.ToString() ?? "null");
                s = "[" + string.Join(", ", parts) + "]";
            }
            else if (value is DateTime dt)
            {
                s = dt.ToString("yyyy-MM-dd HH:mm:ss");
            }
            else if (value is string str)
            {
                if (_logLevel >= LogLevelVerbose)
                    s = "\"" + str + "\"";
                else
                    s = "\"" + str.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
            }
            else
            {
                s = value.ToString();
            }

            if (maxLen > 0 && s.Length > maxLen)
                s = s.Substring(0, maxLen) + $"... (+{s.Length - maxLen} chars)";

            return s;
        }

        private static string Normalize(string text)
        {
            if (text == null) return null;
            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static void WriteColored(string text, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ResetColor();
        }

        #endregion

        #region Benchmarks

        // Sink for consumed values. Prevents the JIT from eliminating benchmark
        // bodies whose results are otherwise unused. Never read.
        private static long _benchSink;

        // ---- Consume overloads ---------------------------------------------------
        // Each overload feeds a value into the sink in a way the JIT cannot remove.
        // A single Consume(long) forced callers to compute something like .Length;
        // the overloads below let benchmark bodies pass the value as-is.

        private static void Consume(string value)
            => _benchSink ^= value?.Length ?? 0;

        private static void Consume(bool value)
            => _benchSink ^= value ? 1 : 0;

        private static void Consume(int value)
            => _benchSink ^= value;

        private static void Consume(long value)
            => _benchSink ^= value;

        private static void Consume(double value)
            => _benchSink ^= BitConverter.DoubleToInt64Bits(value);

        private static void Consume(object value)
            => _benchSink ^= value?.GetHashCode() ?? 0;

        // Runs a labeled benchmark of the given action and prints a single line
        // with the timing statistics. While the benchmark is running, a progress
        // line is updated in place every 1000 iterations. The progress line is
        // excluded from the measured time (the stopwatch is paused around it).
        private static void Benchmark(string name, int iterations, Action action)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            bool showProgress = !Console.IsOutputRedirected;

            // At least 1000 iterations between progress updates, but no more than
            // 20 updates per benchmark regardless of iteration count.
            int step = Math.Max(1000, iterations / 20);

            if (showProgress)
            {
                Console.Write("\r" + new string(' ', 90) + "\r");
                Console.Write($"{name,-48} {0,10}/{iterations,-10}");
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 1; i <= iterations; i++)
            {
                action();

                if (showProgress && i % step == 0)
                {
                    sw.Stop();
                    Console.Write($"\r{name,-48} {i,10}/{iterations,-10}");
                    sw.Start();
                }
            }

            sw.Stop();

            double totalMs = sw.Elapsed.TotalMilliseconds;
            double perOpUs = totalMs * 1000.0 / iterations;

            if (showProgress)
                Console.Write("\r" + new string(' ', 90) + "\r");
            Console.WriteLine($"{name,-48} {iterations,10} {totalMs,12:F2} {perOpUs,10:F3}");
        }

        private static void RunBenchmarks()
        {
            Console.WriteLine();
            WriteColored("=== Benchmarks ===\n", ConsoleColor.Cyan);
            Console.WriteLine($"{"Benchmark",-48} {"Iters",10} {"Total ms",12} {"us/op",10}");
            Console.WriteLine(new string('-', 84));

            // Pre-built fixtures, reused across iterations so that benchmark
            // numbers reflect the operation, not fixture construction.
            string basicContent = CreateBasicContent();
            string jsonContent = CreateJsonContent();
            string radixContent = CreateRadixContent();

            var readIni = IniFile.Create(basicContent, _settings);
            var jsonIni = IniFile.Create(jsonContent, _settings);
            var radixIni = IniFile.Create(radixContent, _settings);

            // Reused mutating fixtures. The initial parse cost is not part of
            // the measurement.
            var writeIni = IniFile.Create(basicContent, _settings);
            var writeStringsIni = IniFile.Create(basicContent, _settings);
            var writeJsonIni = IniFile.Create(jsonContent, _settings);

            var settingsIni = IniFile.Create(_settings);
            var netSettings = new NetworkSettings { Host = "h", Port = 1, Timeout = 1.0, Enabled = true };

            var sampleDict = new Dictionary<string, object>
            {
                ["name"] = "test",
                ["value"] = 42,
                ["flag"] = true
            };

            // Pre-built dictionary for Import benchmarks.
            var importSnapshot = readIni.ExportToDictionary();

            // Warm-up phase: trigger JIT compilation and populate internal caches.
            // Numbers from this phase are discarded.
            for (int i = 0; i < 200; i++)
            {
                var tmp = IniFile.Create(basicContent, _settings);
                Consume(tmp.Content.Length);

                Consume(readIni.ReadString("Section1", "key1", ""));
                Consume(readIni.ReadString("Section1", "key1", "", expandVariables: true));
                Consume(readIni.ReadStrings("Section1", "key1").Length);
                Consume(readIni.ReadInt32("Radix", "Hex", -1));
                Consume(readIni.ContainsKey("Section1", "key1") ? 1 : 0);

                writeIni.WriteString("Section1", "key1", "updated");
                writeStringsIni.WriteStrings("Section1", "arr", "a", "b", "c");

                Consume(jsonIni.ReadJsonString("App", "inline", defaultValue: "")?.Length ?? 0);
                Consume((jsonIni.ReadJsonObject("App", "inline") as IDictionary<string, object>)?.Count ?? 0);
                Consume(Convert.ToInt64(jsonIni.ReadJsonObject("Paths", "data", "root/nested/number", -1)));
                dynamic d = jsonIni.ReadJsonDynamicObject("Paths", "data", "root/nested");
                Consume(d.GetHashCode());

                writeJsonIni.WriteJsonObject("App", "inline", sampleDict);

                Consume(readIni.ExportToDictionary().Count);
                Consume(readIni.Justify().Length);

                settingsIni.ReadSettings(netSettings);
                settingsIni.WriteSettings(netSettings);
            }

            // ---- Simple reads -----------------------------------------------------

            Benchmark("ReadString (existing)", 1_000_000, () =>
            {
                Consume(readIni.ReadString("Section1", "key1", ""));
            });

            Benchmark("ReadString (missing, default)", 1_000_000, () =>
            {
                Consume(readIni.ReadString("Section1", "no_key", "fallback"));
            });

            Benchmark("ReadString (expandVariables: true)", 300_000, () =>
            {
                // Note: the source value has no %VAR%, so expansion is a no-op.
                // This benchmarks the branch, not the expansion itself.
                Consume(readIni.ReadString("Section1", "key1", "", expandVariables: true));
            });

            Benchmark("ReadStrings (single value)", 500_000, () =>
            {
                Consume(readIni.ReadStrings("Section1", "key1").Length);
            });

            Benchmark("ReadInt32 (decimal)", 300_000, () =>
            {
                Consume(readIni.ReadInt32("Section1", "key_num", 0));
            });

            Benchmark("ReadInt32 (radix 0x)", 500_000, () =>
            {
                Consume(radixIni.ReadInt32("Radix", "Hex", -1));
            });

            Benchmark("ContainsKey (existing)", 1_000_000, () =>
            {
                Consume(readIni.ContainsKey("Section1", "key1"));
            });

            Benchmark("ContainsSection", 1_000_000, () =>
            {
                Consume(readIni.ContainsSection("Section1"));
            });

            // ---- Simple writes ----------------------------------------------------

            Benchmark("WriteString (update existing)", 50_000, () =>
            {
                writeIni.WriteString("Section1", "key1", "updated");
                Consume(writeIni.Content);
            });

            Benchmark("WriteString (new key in existing section)", 30_000, () =>
            {
                writeIni.WriteString("Section1", "gen_key", "value");
                Consume(writeIni.Content);
            });

            Benchmark("WriteStrings (3 values)", 20_000, () =>
            {
                writeStringsIni.WriteStrings("Section1", "arr", "a", "b", "c");
                Consume(writeStringsIni.Content);
            });

            // ---- Parsing ----------------------------------------------------------

            Benchmark("Parse (Create from string)", 30_000, () =>
            {
                var ini = IniFile.Create(basicContent, _settings);
                Consume(ini.Content);
            });

            Benchmark("Parse (JSON-heavy content)", 30_000, () =>
            {
                var ini = IniFile.Create(jsonContent, _settings);
                Consume(ini.Content);
            });

            // ---- JSON -------------------------------------------------------------

            Benchmark("ReadJsonString (raw, no path)", 1_000_000, () =>
            {
                Consume(jsonIni.ReadJsonString("App", "inline", defaultValue: ""));
            });

            Benchmark("ReadJsonObject (whole)", 70_000, () =>
            {
                var obj = jsonIni.ReadJsonObject("App", "inline");
                Consume((obj as IDictionary<string, object>)?.Count ?? 0);
            });

            Benchmark("ReadJsonObject (with path)", 15_000, () =>
            {
                Consume(Convert.ToInt64(
                    jsonIni.ReadJsonObject("Paths", "data", "root/nested/number", -1)));
            });

            Benchmark("ReadJsonObject (path, hex index)", 15_000, () =>
            {
                Consume(Convert.ToInt64(
                    jsonIni.ReadJsonObject("Paths", "data", "root/list/0x1", -1)));
            });

            Benchmark("ReadJsonString (with path)", 30_000, () =>
            {
                Consume(jsonIni.ReadJsonString(
                    "Paths", "data", "root/nested/number", defaultValue: "?"));
            });

            Benchmark("ReadJsonDynamicObject (path)", 15_000, () =>
            {
                dynamic d = jsonIni.ReadJsonDynamicObject("Paths", "data", "root/nested");
                Consume(d.GetHashCode());
            });

            Benchmark("WriteJsonObject (whole)", 60_000, () =>
            {
                writeJsonIni.WriteJsonObject("App", "inline", sampleDict);
                Consume(writeJsonIni.Content);
            });

            Benchmark("WriteJsonObject (path, update)", 15_000, () =>
            {
                writeJsonIni.WriteJsonObject("App", "inline", "value", 99);
                Consume(writeJsonIni.Content);
            });

            // ---- Dictionary round-trip -------------------------------------------

            Benchmark("ExportToDictionary", 100_000, () =>
            {
                Consume(readIni.ExportToDictionary().Count);
            });

            var importTarget = IniFile.Create(_settings);

            Benchmark("ImportFromDictionary (merge)", 10_000, () =>
            {
                importTarget.Content = string.Empty;
                importTarget.ImportFromDictionary(importSnapshot);
                Consume(importTarget.Content);
            });

            // ---- Normalization ----------------------------------------------------

            Benchmark("Justify", 150_000, () =>
            {
                Consume(readIni.Justify());
            });

            // ---- Settings serialization ------------------------------------------

            Benchmark("ReadSettings (NetworkSettings)", 30_000, () =>
            {
                settingsIni.ReadSettings(netSettings);
                Consume(netSettings.Port);
            });

            Benchmark("WriteSettings (NetworkSettings)", 15_000, () =>
            {
                settingsIni.WriteSettings(netSettings);
                Consume(settingsIni.Content);
            });

            Console.WriteLine();
            Console.WriteLine("Benchmark complete.");
            Console.WriteLine();
        }

        #endregion
    }
}
