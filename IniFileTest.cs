/******************************************************************************

•   File: IniFileTest.cs

•   Description:

    Console test util for the IniFile class with no external dependencies.

•   License:

    This software is distributed under the MIT License (MIT)

    © 2024-2026 Pavel Bashkardin.

    See https://github.com/ng256/IniFile/blob/main/LICENSE for details.

******************************************************************************/

using System.Text;
using System.Ini;

namespace IniFileTest
{
    // Classes for testing automatic serialization via attributes
    [IniSection("Network")]
    public class NetworkSettings
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 8080;
        public double Timeout { get; set; } = 30.5;
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

    internal class Program
    {
        private static int _testsPassed = 0;
        private static int _testsFailed = 0;
        private static readonly List<string> _testResults = new List<string>();

        static void Main(string[] args)
        {
            Run(args);
        }

        static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== IniFile Library Test Suite ===\n");

            // 1. Create a test INI file
            string testIniContent = CreateTestIniContent();
            File.WriteAllText("test.ini", testIniContent);
            Console.WriteLine("Generated test.ini (evil.ini)\n");

            // 2. Load with allowEscChars = true (to test escaping)
            IniFile ini = IniFile.Load("test.ini", Encoding.UTF8, StringComparison.InvariantCultureIgnoreCase, true);

            // 3. Run tests
            RunAllTests(ini);

            // 4. Save the modified file
            ini.Save("edited.ini", Encoding.UTF8);

            // 5. Manually create the expected file (estimated.ini)
            string expectedContent = GenerateExpectedContent(testIniContent);
            File.WriteAllText("estimated.ini", expectedContent);

            // 6. Compare edited.ini and estimated.ini (ignore trailing spaces/line breaks)
            string edited = File.ReadAllText("edited.ini");
            string estimated = File.ReadAllText("estimated.ini");
            bool filesMatch = string.Equals(edited.Trim(), estimated.Trim(), StringComparison.Ordinal);

            if (filesMatch)
            {
                WriteColored("[OK] ", ConsoleColor.Green);
                Console.WriteLine("edited.ini matches estimated.ini");
                _testsPassed++;
            }
            else
            {
                WriteColored("[FAIL] ", ConsoleColor.Red);
                Console.WriteLine("edited.ini does NOT match estimated.ini");
                Console.WriteLine("Expected:");
                Console.WriteLine(estimated);
                Console.WriteLine("Actual:");
                Console.WriteLine(edited);
                _testsFailed++;
            }

            // 7. Final report
            Console.WriteLine("\n=== Test Summary ===");
            Console.Write("Passed: ");
            WriteColored(_testsPassed.ToString(), ConsoleColor.Green);
            Console.Write(", Failed: ");
            WriteColored(_testsFailed.ToString(), ConsoleColor.Red);
            Console.WriteLine();

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        private static string CreateTestIniContent()
        {
            return @"
# Global comment (INI level)
; Another global comment (INI level)
global_key = global_value
global_with_braces = text with { braces } and [ brackets ]

[Section1]
; comment inside section
key1 = value1
key2 = value with // fake comment and /* fake block */ inside

[Section2]
inline_json = {""name"":""test"",""value"":123}
multiline_json =
{
  ""array"": [1, 2, 3],
  ""nested"": {""flag"": true},
  // This is a JSON comment
  ""comment"": ""// not a comment""
}

[Section3]
json_with_block_comment =
{
  ""before"": 1,
  /* This is a block comment
     with multiple lines */
  ""after"": 2
}

[Section4]
json_with_tricky_strings =
{
  ""str1"": ""/* not a comment */"",
  ""str2"": ""// not a comment"",
  ""str3"": ""{ not an object }"",
  ""str4"": ""[ not an array ]""
}

[Section5]
json_with_escaped =
{
  ""escaped"": ""line1\nline2\t\""quoted\""""
}

[Section6]
json_empty = {}

[Section7]
json_nested =
{
  ""level1"": {
    ""level2"": {
      ""level3"": {
        ""value"": 42
      }
    }
  }
}

[Section8]
json_with_trailing_comment =
{
  ""value"": 1
}
// This is an INI comment, not JSON

[Section9]
json_with_trailing_text =
{
  ""value"": 2
} trailing text after brace

[Section10]
json_with_multiline_comment =
{
  ""value"": 3,
  /*
    This is a block comment
    with fake braces { } inside
  */
  ""next"": 4
}

[Section11]
json_with_inline_comment_after_value =
{
  ""value"": 5 // comment
}
[Section12]
json_with_unbalanced_brace_comments =
{
  ""key"": ""value"",
  // This comment has an unbalanced opening brace {
  ""next"": 123,
  /* This block comment also has an unbalanced opening brace {
     and some text */
  ""final"": true
}
[Section13]
; Test keys with colon as delimiter
key_with_colon : value with colon
json_colon : {""test"":""colon""}
another_colon_key : 123
";
        }

        private static string GenerateExpectedContent(string originalContent)
        {
            // Load a copy of the original content
            var ini = IniFile.Load(new StringReader(originalContent),
                StringComparison.InvariantCultureIgnoreCase, true);

            // Repeat the operations from the tests that modify the file:
            ini["", "global_key"] = "new_global";
            byte[] testBytes = { 0x01, 0x02, 0x03, 0x04 };
            ini.WriteArray("Section2", "byteKey", testBytes);
            ini.WriteString("Section2", "escaped_new", "Line1\nLine2\tTab");
            ini.RemoveSection("Section1");

            return ini.Content;
        }

        private static void RunAllTests(IniFile ini)
        {
            int testNum = 0;

            // -------------------- Base tests (1–20) --------------------

            // Test 1: ReadSections
            testNum++;
            string[] expectedSections = { "section1", "section2", "section3", "section4", "section5", "section6", "section7", "section8", "section9", "section10", "section11", "section12", "section13" };
            string[] actualSections = ini.ReadSections();
            RunTest(testNum, "ReadSections", expectedSections, actualSections, "List of sections");

            // Test 2: ReadKeys for Section1
            testNum++;
            string[] expectedKeys1 = { "key1", "key2" };
            string[] actualKeys1 = ini.ReadKeys("Section1");
            RunTest(testNum, "ReadKeys (Section1)", expectedKeys1, actualKeys1, "Keys in Section1");

            // Test 3: ReadKeys for Section2
            testNum++;
            string[] expectedKeys2 = { "inline_json", "multiline_json" };
            string[] actualKeys2 = ini.ReadKeys("Section2");
            RunTest(testNum, "ReadKeys (Section2)", expectedKeys2, actualKeys2, "Keys in Section2");

            // Test 4: ReadString existing
            testNum++;
            RunTest(testNum, "ReadString (existing)", "value1", ini.ReadString("Section1", "key1", "default"), "value1");

            // Test 5: ReadString missing
            testNum++;
            RunTest(testNum, "ReadString (missing)", "default", ini.ReadString("Section1", "missingKey", "default"), "default");

            // Test 6: Indexer get
            testNum++;
            RunTest(testNum, "Indexer get", "value1", ini["Section1", "key1"], "value1");

            // Test 7: ReadBoolean (not present, so default)
            testNum++;
            bool expectedBool = true;
            bool actualBool = ini.ReadBoolean("Section1", "key4", true);
            RunTest(testNum, "ReadBoolean (missing, default)", expectedBool, actualBool, "default true");

            // Test 8: WriteString update
            testNum++;
            ini.WriteString("Section1", "key1", "new_value");
            string readAfterWrite = ini.ReadString("Section1", "key1", "");
            RunTest(testNum, "WriteString (update)", "new_value", readAfterWrite, "After write");

            // Test 9: WriteInt32 (new key)
            testNum++;
            ini.WriteInt32("Section1", "key_int", 999);
            RunTest(testNum, "WriteInt32", 999, ini.ReadInt32("Section1", "key_int", 0), "999");

            // Test 10: WriteArray<string>
            testNum++;
            string[] newArray = { "new1", "new2" };
            ini.WriteArray<string>("Section1", "key_array", newArray);
            string[] readArray = ini.ReadStrings("Section1", "key_array");
            RunTest(testNum, "WriteArray", newArray, readArray, "['new1','new2']");

            // Test 11: RemoveKey
            testNum++;
            ini.RemoveKey("Section1", "key_int");
            bool keyIntExists = Array.Exists(ini.ReadKeys("Section1"), k => k == "key_int");
            RunTest(testNum, "RemoveKey", false, keyIntExists, "key_int removed");

            // Test 12: RemoveKeys
            testNum++;
            ini.RemoveKeys("Section1", "key_array");
            string[] keysAfterRemove = ini.ReadKeys("Section1");
            bool keyArrayExists = Array.Exists(keysAfterRemove, k => k == "key_array");
            RunTest(testNum, "RemoveKeys (all key_array)", false, keyArrayExists, "key_array entries removed");

            // Test 13: RemoveSection
            testNum++;
            ini.RemoveSection("Section1");
            string[] sectionsAfter = ini.ReadSections();
            bool section1Exists = Array.Exists(sectionsAfter, s => s == "Section1");
            RunTest(testNum, "RemoveSection (Section1)", false, section1Exists, "Section1 removed");

            // Test 14: Indexer set (global)
            testNum++;
            ini["", "global_key"] = "new_global";
            RunTest(testNum, "Indexer set (global)", "new_global", ini["", "global_key"], "new_global");

            // Test 15: ReadSettings/WriteSettings (network)
            testNum++;
            NetworkSettings net = new NetworkSettings { Host = "testhost", Port = 1234, Timeout = 60.0, Enabled = false };
            LoggingSettings log = new LoggingSettings { Level = "Debug", FilePath = "debug.log" };

            IniFile settingsIni = IniFile.Create(StringComparison.InvariantCultureIgnoreCase, true);
            settingsIni.WriteSettings(net);
            settingsIni.WriteSettings(log);

            NetworkSettings netRead = new NetworkSettings();
            LoggingSettings logRead = new LoggingSettings();
            settingsIni.ReadSettings(netRead);
            settingsIni.ReadSettings(logRead);

            bool netOk = netRead.Host == "testhost" && netRead.Port == 1234 && Math.Abs(netRead.Timeout - 60.0) < 0.001 && netRead.Enabled == false;
            bool logOk = logRead.Level == "Debug" && logRead.FilePath == "debug.log";
            RunTest(testNum, "ReadSettings/WriteSettings (network)", true, netOk, "Network settings match");

            // Test 16: ReadSettings/WriteSettings (logging)
            testNum++;
            RunTest(testNum, "ReadSettings/WriteSettings (logging)", true, logOk, "Logging settings match");

            // Test 17: ReadKeys non-existent section
            testNum++;
            string[] keysNone = ini.ReadKeys("NonExistentSection");
            RunTest(testNum, "ReadKeys (non-existent section)", new string[0], keysNone, "empty array");

            // Test 18: byte[] roundtrip
            testNum++;
            byte[] testBytes = { 0x01, 0x02, 0x03, 0x04 };
            ini.WriteArray("Section2", "byteKey", testBytes);
            byte[] readBytes = ini.ReadArray<byte>("Section2", "byteKey");
            RunTest(testNum, "WriteArray/ReadArray byte[]", testBytes, readBytes, "byte array roundtrip");

            // Test 19: Escaping
            testNum++;
            ini.WriteString("Section2", "escaped_new", "Line1\nLine2\tTab");
            string readEscaped = ini.ReadString("Section2", "escaped_new", "");
            RunTest(testNum, "WriteString with escapes", "Line1\nLine2\tTab", readEscaped, "escaped write");

            // Test 20: Justify (separate file)
            testNum++;
            TestJustify(testNum);

            // -------------------- JSON TESTS (21–35) --------------------

            // Test 21: ReadJsonString inline
            testNum++;
            string expectedInline = "{\"name\":\"test\",\"value\":123}";
            string actualInline = ini.ReadJsonString("Section2", "inline_json", "default");
            RunTest(testNum, "ReadJsonString (inline)", expectedInline, actualInline, "inline JSON with no comments");

            // Test 22: ReadJsonString multiline with // comment
            testNum++;
            string expectedMulti = @"{
  ""array"": [1, 2, 3],
  ""nested"": {""flag"": true},
  // This is a JSON comment
  ""comment"": ""// not a comment""
}";
            string actualMulti = ini.ReadJsonString("Section2", "multiline_json", "");
            expectedMulti = expectedMulti.Replace("\r\n", "\n").Replace("\r", "\n");
            actualMulti = actualMulti?.Replace("\r\n", "\n").Replace("\r", "\n");
            RunTest(testNum, "ReadJsonString (multiline with // comment)", expectedMulti, actualMulti, "multiline JSON with // comment");

            // Test 23: ReadJsonString with block comment
            testNum++;
            string expectedBlock = @"{
  ""before"": 1,
  /* This is a block comment
     with multiple lines */
  ""after"": 2
}";
            string actualBlock = ini.ReadJsonString("Section3", "json_with_block_comment", "");
            expectedBlock = expectedBlock.Replace("\r\n", "\n").Replace("\r", "\n");
            actualBlock = actualBlock?.Replace("\r\n", "\n").Replace("\r", "\n");
            RunTest(testNum, "ReadJsonString (block comment)", expectedBlock, actualBlock, "JSON with /* ... */ comment");

            // Test 24: ReadJsonString tricky strings
            testNum++;
            string expectedTricky = @"{
  ""str1"": ""/* not a comment */"",
  ""str2"": ""// not a comment"",
  ""str3"": ""{ not an object }"",
  ""str4"": ""[ not an array ]""
}";
            string actualTricky = ini.ReadJsonString("Section4", "json_with_tricky_strings", "");
            expectedTricky = expectedTricky.Replace("\r\n", "\n").Replace("\r", "\n");
            actualTricky = actualTricky?.Replace("\r\n", "\n").Replace("\r", "\n");
            RunTest(testNum, "ReadJsonString (tricky strings)", expectedTricky, actualTricky, "strings containing /*, //, {, [");

            // Test 25: ReadJsonString escaped
            testNum++;
            string expectedEscaped = @"{
  ""escaped"": ""line1\nline2\t\""quoted\""""
}";
            string actualEscaped = ini.ReadJsonString("Section5", "json_with_escaped", "");
            expectedEscaped = expectedEscaped.Replace("\r\n", "\n").Replace("\r", "\n");
            actualEscaped = actualEscaped?.Replace("\r\n", "\n").Replace("\r", "\n");
            RunTest(testNum, "ReadJsonString (escaped)", expectedEscaped, actualEscaped, "JSON with escape sequences");

            // Test 26: ReadJsonString empty object
            testNum++;
            string expectedEmpty = "{}";
            string actualEmpty = ini.ReadJsonString("Section6", "json_empty", "");
            RunTest(testNum, "ReadJsonString (empty object)", expectedEmpty, actualEmpty, "{}");

            // Test 27: ReadJsonString nested
            testNum++;
            string expectedNested = @"{
  ""level1"": {
    ""level2"": {
      ""level3"": {
        ""value"": 42
      }
    }
  }
}";
            string actualNested = ini.ReadJsonString("Section7", "json_nested", "");
            expectedNested = expectedNested.Replace("\r\n", "\n").Replace("\r", "\n");
            actualNested = actualNested?.Replace("\r\n", "\n").Replace("\r", "\n");
            RunTest(testNum, "ReadJsonString (nested)", expectedNested, actualNested, "deeply nested JSON");

            // Test 28: ReadJsonString trailing INI comment
            testNum++;
            string expectedTrailing = @"{
  ""value"": 1
}";
            string actualTrailing = ini.ReadJsonString("Section8", "json_with_trailing_comment", "");
            expectedTrailing = expectedTrailing.Replace("\r\n", "\n").Replace("\r", "\n");
            actualTrailing = actualTrailing?.Replace("\r\n", "\n").Replace("\r", "\n");
            RunTest(testNum, "ReadJsonString (trailing INI comment)", expectedTrailing, actualTrailing, "JSON block ends before INI comment");

            // Test 29: ReadJsonString trailing text after brace
            testNum++;
            string expectedTrailingText = @"{
  ""value"": 2
}";
            string actualTrailingText = ini.ReadJsonString("Section9", "json_with_trailing_text", "");
            expectedTrailingText = expectedTrailingText.Replace("\r\n", "\n").Replace("\r", "\n");
            actualTrailingText = actualTrailingText?.Replace("\r\n", "\n").Replace("\r", "\n");
            RunTest(testNum, "ReadJsonString (trailing text after brace)", expectedTrailingText, actualTrailingText, "JSON block ends before trailing text");

            // Test 30: ReadJsonString multiline block comment
            testNum++;
            string expectedMultiBlock = @"{
  ""value"": 3,
  /*
    This is a block comment
    with fake braces { } inside
  */
  ""next"": 4
}";
            string actualMultiBlock = ini.ReadJsonString("Section10", "json_with_multiline_comment", "");
            expectedMultiBlock = expectedMultiBlock.Replace("\r\n", "\n").Replace("\r", "\n");
            actualMultiBlock = actualMultiBlock?.Replace("\r\n", "\n").Replace("\r", "\n");
            RunTest(testNum, "ReadJsonString (multiline block comment)", expectedMultiBlock, actualMultiBlock, "JSON with /* ... */ containing braces");

            // Test 31: ReadJsonString inline comment after value
            testNum++;
            string expectedInlineComment = @"{
  ""value"": 5 // comment
}";
            string actualInlineComment = ini.ReadJsonString("Section11", "json_with_inline_comment_after_value", "");
            expectedInlineComment = expectedInlineComment.Replace("\r\n", "\n").Replace("\r", "\n");
            actualInlineComment = actualInlineComment?.Replace("\r\n", "\n").Replace("\r", "\n");
            RunTest(testNum, "ReadJsonString (inline comment after value)", expectedInlineComment, actualInlineComment, "JSON with // comment after value");

            // Test 32: ReadJsonObject inline
            testNum++;
            var inlineObj = ini.ReadJsonObject("Section2", "inline_json");
            bool inlineOk = false;
            if (inlineObj is IDictionary<string, object> inlineDict)
            {
                inlineOk = inlineDict.ContainsKey("name") && inlineDict["name"] as string == "test" &&
                           inlineDict.ContainsKey("value") && Convert.ToInt32(inlineDict["value"]) == 123;
            }
            RunTest(testNum, "ReadJsonObject (inline)", true, inlineOk, "Parse inline JSON object");

            // Test 33: ReadJsonObject multiline
            testNum++;
            var multiObj = ini.ReadJsonObject("Section2", "multiline_json");
            bool multiOk = false;
            if (multiObj is IDictionary<string, object> multiDict)
            {
                var array = multiDict["array"] as object[];
                var nested = multiDict["nested"] as IDictionary<string, object>;
                multiOk = array != null && array.Length == 3 &&
                          nested != null && Convert.ToBoolean(nested["flag"]) == true &&
                          multiDict.ContainsKey("comment") && multiDict["comment"] as string == "// not a comment";
            }
            RunTest(testNum, "ReadJsonObject (multiline)", true, multiOk, "Parse multiline JSON with comments");

            // Test 34: ReadJsonString with unbalanced braces in comments
            testNum++;
            string expectedUnbalanced = @"{
  ""key"": ""value"",
  // This comment has an unbalanced opening brace {
  ""next"": 123,
  /* This block comment also has an unbalanced opening brace {
     and some text */
  ""final"": true
}";
            string actualUnbalanced = ini.ReadJsonString("Section12", "json_with_unbalanced_brace_comments", "");
            expectedUnbalanced = expectedUnbalanced.Replace("\r\n", "\n").Replace("\r", "\n");
            actualUnbalanced = actualUnbalanced?.Replace("\r\n", "\n").Replace("\r", "\n");
            RunTest(testNum, "ReadJsonString (unbalanced braces in comments)", expectedUnbalanced, actualUnbalanced, "JSON with // { and /* { comments");

            // Test 35: ReadJsonObject with unbalanced braces in comments
            testNum++;
            var obj = ini.ReadJsonObject("Section12", "json_with_unbalanced_brace_comments");
            bool parsedOk = false;
            if (obj is IDictionary<string, object> dict)
            {
                parsedOk = dict.ContainsKey("key") && dict["key"] as string == "value" &&
                           dict.ContainsKey("next") && Convert.ToInt32(dict["next"]) == 123 &&
                           dict.ContainsKey("final") && Convert.ToBoolean(dict["final"]) == true;
            }
            RunTest(testNum, "ReadJsonObject (unbalanced braces in comments)", true, parsedOk, "Parsed object from JSON with unbalanced brace comments");

            // Test 36: ReadString with colon delimiter
            testNum++;
            string colonValue = ini.ReadString("Section13", "key_with_colon", "default");
            RunTest(testNum, "ReadString (colon delimiter)", "value with colon", colonValue, "key_with_colon using ':'");

            // Test 37: ReadJsonString with colon delimiter
            testNum++;
            string jsonColon = ini.ReadJsonString("Section13", "json_colon", "default");
            RunTest(testNum, "ReadJsonString (colon delimiter)", "{\"test\":\"colon\"}", jsonColon, "json_colon using ':'");

            // Test 38: ReadJsonObject with colon delimiter
            testNum++;
            var objColon = ini.ReadJsonObject("Section13", "json_colon");
            bool colonObjOk = false;
            if (objColon is IDictionary<string, object> colonDict)
            {
                colonObjOk = colonDict.ContainsKey("test") && colonDict["test"] as string == "colon";
            }
            RunTest(testNum, "ReadJsonObject (colon delimiter)", true, colonObjOk, "Parse JSON from colon-delimited key");

            // Test 39: ReadInt32 with colon delimiter
            testNum++;
            int colonInt = ini.ReadInt32("Section13", "another_colon_key", 0);
            RunTest(testNum, "ReadInt32 (colon delimiter)", 123, colonInt, "another_colon_key using ':'");

            // Test 40: ReadKeys for Section13
            testNum++;
            string[] expectedKeys13 = { "key_with_colon", "json_colon", "another_colon_key" };
            string[] actualKeys13 = ini.ReadKeys("Section13");
            RunTest(testNum, "ReadKeys (Section13)", expectedKeys13, actualKeys13, "Keys in Section13");
        }

        private static void TestJustify(int testNum)
        {
            string content = @"
# This is a comment
; Another comment
   global_key1 = value1   
global_key2 = value2

[Section A]  
   key1 = val1
key2 = val2
   
; comment inside section
key3 = val3

[Section B]
key1 = multi
key1 = multi2

  ; some garbage
undefined_line_without_equals";
            File.WriteAllText("justify_test.ini", content);

            IniFile ini = IniFile.Load("justify_test.ini", Encoding.UTF8, StringComparison.InvariantCultureIgnoreCase, true);
            string justified = ini.Justify();

            string expected = @"global_key1=value1
global_key2=value2

[Section A]
key1=val1
key2=val2
key3=val3

[Section B]
key1=multi
key1=multi2
";
            expected = expected.Replace("\r\n", "\n").Replace("\r", "\n");
            justified = justified.Replace("\r\n", "\n").Replace("\r", "\n");

            RunTest(testNum, "Justify", expected, justified, "Compact INI without comments/blank/undefined");
        }

        // -------------------- Test helpers --------------------
        private static void RunTest(int testNumber, string testName, object expected, object actual, string description)
        {
            bool passed = Equals(expected, actual);
            if (passed) _testsPassed++;
            else _testsFailed++;

            WriteColored(passed ? "[PASS " : "[FAIL ", passed ? ConsoleColor.Green : ConsoleColor.Red);
            Console.Write($"#{testNumber}] ");
            Console.ResetColor();
            Console.WriteLine($"{testName} - {description}");
            Console.WriteLine($"  Expected: {FormatValue(expected)}");
            Console.WriteLine($"  Actual:   {FormatValue(actual)}");
            Console.WriteLine();

            string status = passed ? "PASS" : "FAIL";
            string message = $"[{status} #{testNumber}] {testName} - {description}\n" +
                             $"  Expected: {FormatValue(expected)}\n" +
                             $"  Actual:   {FormatValue(actual)}";
            _testResults.Add(message);
        }

        private static string FormatValue(object value)
        {
            if (value == null) return "null";
            if (value is string[] arr)
                return "[" + string.Join(", ", arr) + "]";
            if (value is byte[] bytes)
                return "[" + string.Join(", ", bytes) + "]";
            if (value is DateTime dt)
                return dt.ToString("yyyy-MM-dd");
            return value.ToString();
        }

        private static void RunTest(int testNumber, string testName, string expected, string actual, string description)
        {
            RunTest(testNumber, testName, (object)expected, actual, description);
        }

        private static void RunTest(int testNumber, string testName, bool expected, bool actual, string description)
        {
            RunTest(testNumber, testName, (object)expected, actual, description);
        }

        private static void RunTest(int testNumber, string testName, int expected, int actual, string description)
        {
            RunTest(testNumber, testName, (object)expected, actual, description);
        }

        private static void RunTest(int testNumber, string testName, double expected, double actual, string description)
        {
            bool passed = Math.Abs(expected - actual) < 1e-9;
            if (passed) _testsPassed++;
            else _testsFailed++;

            WriteColored(passed ? "[PASS " : "[FAIL ", passed ? ConsoleColor.Green : ConsoleColor.Red);
            Console.Write($"#{testNumber}] ");
            Console.ResetColor();
            Console.WriteLine($"{testName} - {description}");
            Console.WriteLine($"  Expected: {expected}");
            Console.WriteLine($"  Actual:   {actual}");
            Console.WriteLine();

            string status = passed ? "PASS" : "FAIL";
            string message = $"[{status} #{testNumber}] {testName} - {description}\n" +
                             $"  Expected: {expected}\n" +
                             $"  Actual:   {actual}";
            _testResults.Add(message);
        }

        private static void RunTest(int testNumber, string testName, DateTime expected, DateTime actual, string description)
        {
            RunTest(testNumber, testName, (object)expected, actual, description);
        }

        private static void RunTest(int testNumber, string testName, char expected, char actual, string description)
        {
            RunTest(testNumber, testName, (object)expected, actual, description);
        }

        private static void RunTest(int testNumber, string testName, Array expected, Array actual, string description)
        {
            bool passed = false;
            if (expected != null && actual != null && expected.Length == actual.Length)
            {
                passed = true;
                for (int i = 0; i < expected.Length; i++)
                {
                    if (!Equals(expected.GetValue(i), actual.GetValue(i)))
                    {
                        passed = false;
                        break;
                    }
                }
            }
            else if (expected == null && actual == null) passed = true;
            else passed = false;

            if (passed) _testsPassed++;
            else _testsFailed++;

            WriteColored(passed ? "[PASS " : "[FAIL ", passed ? ConsoleColor.Green : ConsoleColor.Red);
            Console.Write($"#{testNumber}] ");
            Console.ResetColor();
            Console.WriteLine($"{testName} - {description}");
            Console.WriteLine($"  Expected: {FormatValue(expected)}");
            Console.WriteLine($"  Actual:   {FormatValue(actual)}");
            Console.WriteLine();

            string status = passed ? "PASS" : "FAIL";
            string message = $"[{status} #{testNumber}] {testName} - {description}\n" +
                             $"  Expected: {FormatValue(expected)}\n" +
                             $"  Actual:   {FormatValue(actual)}";
            _testResults.Add(message);
        }

        private static void WriteColored(string text, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ResetColor();
        }
    }
}
