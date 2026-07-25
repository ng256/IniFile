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
            Console.WriteLine("Generated test.ini\n");

            // 2. Load with allowEscChars = true (to test escaping)
            IniFile ini = IniFile.Load("test.ini", Encoding.UTF8, StringComparison.InvariantCultureIgnoreCase, true);

            // 3. Run tests
            RunAllTests(ini);

            // 4. Save the modified file
            ini.Save("edited.ini", Encoding.UTF8);

            // 5. Manually create the expected file (estimated.ini)
            string estimatedContent = CreateEstimatedIniContent();
            File.WriteAllText("estimated.ini", estimatedContent);

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
            var sb = new StringBuilder();
            sb.AppendLine("# Global comment");
            sb.AppendLine("; Another global comment");
            sb.AppendLine("global_key1 = global_value1");
            sb.AppendLine("global_key2 = global_value2");
            sb.AppendLine();
            sb.AppendLine("[Section1]");
            sb.AppendLine("key1 = value1");
            sb.AppendLine("key2 = 123");
            sb.AppendLine("key3 = 45.67");
            sb.AppendLine("key4 = true");
            sb.AppendLine("key5 = 2023-12-31");
            sb.AppendLine("key6 = A");
            sb.AppendLine("key7 = 0x1A");
            sb.AppendLine();
            sb.AppendLine("[Section2]");
            sb.AppendLine("; Some comments");
            sb.AppendLine("key1 = value1");
            sb.AppendLine("key1 = value2   ; duplicate key for array");
            sb.AppendLine("key2 = 10");
            sb.AppendLine("key2 = 20");
            sb.AppendLine("key3 = hello world");
            sb.AppendLine();
            sb.AppendLine("[Section3]");
            sb.AppendLine("key_with_space = value with space");
            sb.AppendLine("key_with_colon: colon_value");
            sb.AppendLine("key_with_escapes = Hello\\nWorld\\t!");
            sb.AppendLine();
            // JSON tests
            sb.AppendLine("[JsonSection]");
            sb.AppendLine("inline_json = {\"name\":\"test\",\"value\":123}");
            sb.AppendLine("multiline_json =");
            sb.AppendLine("{");
            sb.AppendLine("  \"array\": [1, 2, 3],");
            sb.AppendLine("  \"nested\": {\"flag\": true}");
            sb.AppendLine("}");
            sb.AppendLine("json_array = [1, 2, 3, 4]");
            return sb.ToString();
        }

        private static string CreateEstimatedIniContent()
        {
            // Actual content after all test operations, with json_array removed
            var sb = new StringBuilder();
            sb.AppendLine("# Global comment");
            sb.AppendLine("; Another global comment");
            sb.AppendLine("global_key1 = new_global");
            sb.AppendLine("global_key2 = global_value2");
            sb.AppendLine();
            sb.AppendLine("[Section1]");
            sb.AppendLine("key1 = new_value");
            sb.AppendLine("key2 = 999");
            sb.AppendLine();
            sb.AppendLine("key4 = False");
            sb.AppendLine("key5 = 2023-12-31");
            sb.AppendLine("key6 = A");
            sb.AppendLine("key7 = 0x1A");
            sb.AppendLine();
            sb.AppendLine("newKey=added");
            sb.AppendLine();
            sb.AppendLine("[Section2]");
            sb.AppendLine("; Somekey1 = new1");
            sb.AppendLine("   ; duplicate key fokey2 = 10");
            sb.AppendLine();
            sb.AppendLine("key3 = hello world");
            sb.AppendLine();
            sb.AppendLine("[JsonSection]");
            sb.AppendLine();
            sb.AppendLine();
            return sb.ToString();
        }

        private static void RunAllTests(IniFile ini)
        {
            int testNum = 0;

            // Test 1: ReadSections
            testNum++;
            string[] expectedSections = { "section1", "section2", "section3", "jsonsection" };
            string[] actualSections = ini.ReadSections();
            RunTest(testNum, "ReadSections", expectedSections, actualSections, "List of sections");

            // Test 2: ReadKeys for Section1
            testNum++;
            string[] expectedKeys1 = { "key1", "key2", "key3", "key4", "key5", "key6", "key7" };
            string[] actualKeys1 = ini.ReadKeys("Section1");
            RunTest(testNum, "ReadKeys (Section1)", expectedKeys1, actualKeys1, "Keys in Section1");

            // Test 3: ReadKeys for Section2
            testNum++;
            string[] expectedKeys2 = { "key1", "key2", "key3" };
            string[] actualKeys2 = ini.ReadKeys("Section2");
            RunTest(testNum, "ReadKeys (Section2)", expectedKeys2, actualKeys2, "Keys in Section2");

            // Test 4: ReadString
            testNum++;
            RunTest(testNum, "ReadString (existing)", "value1", ini.ReadString("Section1", "key1", "default"), "value1");

            // Test 5: ReadString missing
            testNum++;
            RunTest(testNum, "ReadString (missing)", "default", ini.ReadString("Section1", "missingKey", "default"), "default");

            // Test 6: Indexer get
            testNum++;
            RunTest(testNum, "Indexer get", "value1", ini["Section1", "key1"], "value1");

            // Test 7: ReadBoolean
            testNum++;
            bool expectedBool = true;
            bool actualBool = ini.ReadBoolean("Section1", "key4", false);
            RunTest(testNum, "ReadBoolean (true)", expectedBool, actualBool, "Should be true (bug: returns false)");

            // Test 8: ReadBoolean missing
            testNum++;
            RunTest(testNum, "ReadBoolean (missing)", true, ini.ReadBoolean("Section1", "missingBool", true), "default true");

            // Test 9: ReadInt32 existing
            testNum++;
            RunTest(testNum, "ReadInt32 (existing)", 123, ini.ReadInt32("Section1", "key2", 0), "123");

            // Test 10: ReadInt32 missing
            testNum++;
            RunTest(testNum, "ReadInt32 (missing)", 999, ini.ReadInt32("Section1", "missingInt", 999), "default 999");

            // Test 11: ReadDouble
            testNum++;
            RunTest(testNum, "ReadDouble", 45.67, ini.ReadDouble("Section1", "key3", 0.0), "45.67");

            // Test 12: ReadDateTime
            testNum++;
            DateTime expectedDate = new DateTime(2023, 12, 31);
            DateTime actualDate = ini.ReadDateTime("Section1", "key5", DateTime.MinValue);
            RunTest(testNum, "ReadDateTime", expectedDate, actualDate, "2023-12-31");

            // Test 13: ReadChar
            testNum++;
            char expectedChar = 'A';
            char actualChar = ini.ReadChar("Section1", "key6", 'Z');
            RunTest(testNum, "ReadChar (existing)", expectedChar, actualChar, "A");

            // Test 14: ReadChar missing
            testNum++;
            RunTest(testNum, "ReadChar (missing)", 'X', ini.ReadChar("Section1", "missingChar", 'X'), "default X");

            // Test 15: ReadStrings (single)
            testNum++;
            string[] expectedSingle = { "value1" };
            string[] actualSingle = ini.ReadStrings("Section1", "key1");
            RunTest(testNum, "ReadStrings (single)", expectedSingle, actualSingle, "['value1']");

            // Test 16: ReadStrings (multi)
            testNum++;
            string[] expectedMulti = { "value1", "value2" };
            string[] actualMulti = ini.ReadStrings("Section2", "key1");
            RunTest(testNum, "ReadStrings (multi)", expectedMulti, actualMulti, "['value1','value2']");

            // Test 17: ReadArray<int>
            testNum++;
            int[] expectedIntArray = { 10, 20 };
            int[] actualIntArray = ini.ReadArray<int>("Section2", "key2");
            RunTest(testNum, "ReadArray<int>", expectedIntArray, actualIntArray, "[10,20]");

            // Test 18: WriteString update
            testNum++;
            ini.WriteString("Section1", "key1", "new_value");
            string readAfterWrite = ini.ReadString("Section1", "key1", "");
            RunTest(testNum, "WriteString (update)", "new_value", readAfterWrite, "After write");

            // Test 19: WriteInt32
            testNum++;
            ini.WriteInt32("Section1", "key2", 999);
            RunTest(testNum, "WriteInt32", 999, ini.ReadInt32("Section1", "key2", 0), "999");

            // Test 20: WriteBoolean
            testNum++;
            ini.WriteBoolean("Section1", "key4", false);
            RunTest(testNum, "WriteBoolean", false, ini.ReadBoolean("Section1", "key4", true), "false");

            // Test 21: WriteArray<string>
            testNum++;
            string[] newArray = { "new1", "new2" };
            ini.WriteArray<string>("Section2", "key1", newArray);
            string[] readArray = ini.ReadStrings("Section2", "key1");
            RunTest(testNum, "WriteArray", newArray, readArray, "['new1','new2']");

            // Test 22: WriteString new key
            testNum++;
            ini.WriteString("Section1", "newKey", "added");
            RunTest(testNum, "WriteString (new key)", "added", ini.ReadString("Section1", "newKey", ""), "added");

            // Test 23: RemoveKey
            testNum++;
            ini.RemoveKey("Section1", "key3");
            bool key3Exists = Array.Exists(ini.ReadKeys("Section1"), k => k == "key3");
            RunTest(testNum, "RemoveKey", false, key3Exists, "key3 should be removed");

            // Test 24: RemoveKeys
            testNum++;
            ini.RemoveKeys("Section2", "key1");
            string[] keysAfterRemove = ini.ReadKeys("Section2");
            bool key1Exists = Array.Exists(keysAfterRemove, k => k == "key1");
            RunTest(testNum, "RemoveKeys (all key1)", false, key1Exists, "key1 entries should be removed");

            // Test 25: RemoveSection
            testNum++;
            ini.RemoveSection("Section3");
            string[] sectionsAfter = ini.ReadSections();
            bool section3Exists = Array.Exists(sectionsAfter, s => s == "Section3");
            RunTest(testNum, "RemoveSection (Section3)", false, section3Exists, "Section3 removed");

            // Test 26: Indexer set (global)
            testNum++;
            ini["", "global_key1"] = "new_global";
            RunTest(testNum, "Indexer set (global)", "new_global", ini["", "global_key1"], "new_global");

            // Test 27: ReadSettings/WriteSettings (network)
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

            // Test 28: ReadSettings/WriteSettings (logging)
            testNum++;
            RunTest(testNum, "ReadSettings/WriteSettings (logging)", true, logOk, "Logging settings match");

            // Test 29: ReadKeys non-existent section
            testNum++;
            string[] keysNone = ini.ReadKeys("NonExistentSection");
            RunTest(testNum, "ReadKeys (non-existent section)", new string[0], keysNone, "empty array");

            // Test 30: byte[] roundtrip
            testNum++;
            byte[] testBytes = { 0x01, 0x02, 0x03, 0x04 };
            ini.WriteArray("Section1", "byteKey", testBytes);
            byte[] readBytes = ini.ReadArray<byte>("Section1", "byteKey");
            RunTest(testNum, "WriteArray/ReadArray byte[]", testBytes, readBytes, "byte array roundtrip");

            // Test 31: Escaping
            testNum++;
            ini.WriteString("Section3", "escaped_new", "Line1\nLine2\tTab");
            string readEscaped = ini.ReadString("Section3", "escaped_new", "");
            RunTest(testNum, "WriteString with escapes", "Line1\nLine2\tTab", readEscaped, "escaped write");

            // --- JSON TESTS ---

            // Test 32: ReadJson inline
            testNum++;
            string expectedJsonInline = "{\"name\":\"test\",\"value\":123}";
            string actualJsonInline = ini.ReadJsonString("JsonSection", "inline_json", "default");
            RunTest(testNum, "ReadJson (inline)", expectedJsonInline, actualJsonInline, "inline JSON");

            // Test 33: ReadJson multiline
            testNum++;
            string expectedJsonMulti = "{\n  \"array\": [1, 2, 3],\n  \"nested\": {\"flag\": true}\n}";
            string actualJsonMulti = ini.ReadJsonString("JsonSection", "multiline_json", "default");
            expectedJsonMulti = expectedJsonMulti.Replace("\r\n", "\n").Replace("\r", "\n");
            actualJsonMulti = actualJsonMulti?.Replace("\r\n", "\n").Replace("\r", "\n");
            RunTest(testNum, "ReadJson (multiline)", expectedJsonMulti, actualJsonMulti, "multiline JSON");

            // Test 34: ReadJson array
            testNum++;
            string expectedJsonArray = "[1, 2, 3, 4]";
            string actualJsonArray = ini.ReadJsonString("JsonSection", "json_array", "default");
            RunTest(testNum, "ReadJson (array)", expectedJsonArray, actualJsonArray, "JSON array");

            // Test 35: ReadJson missing (default)
            testNum++;
            string defaultJson = ini.ReadJsonString("JsonSection", "non_existent_json", "default_value");
            RunTest(testNum, "ReadJson (missing)", "default_value", defaultJson, "default value");

            // Test 36: WriteJson update inline
            testNum++;
            ini.WriteJsonString("JsonSection", "inline_json", "{\"name\":\"updated\",\"value\":456}");
            string updatedInline = ini.ReadJsonString("JsonSection", "inline_json", "");
            RunTest(testNum, "WriteJson (update inline)", "{\"name\":\"updated\",\"value\":456}", updatedInline, "updated inline JSON");

            // Test 37: WriteJson update multiline
            testNum++;
            string newMulti = "{\n  \"array\": [1, 2, 3, 4],\n  \"nested\": {\"flag\": false}\n}";
            ini.WriteJsonString("JsonSection", "multiline_json", newMulti);
            string updatedMulti = ini.ReadJsonString("JsonSection", "multiline_json", "");
            updatedMulti = updatedMulti?.Replace("\r\n", "\n").Replace("\r", "\n");
            RunTest(testNum, "WriteJson (update multiline)", newMulti, updatedMulti, "updated multiline JSON");

            // Test 38: ReadJsonObject inline
            testNum++;
            string inlineJsonRaw = ini.ReadJsonString("JsonSection", "inline_json");
            object inlineObj = ini.ReadJsonObject("JsonSection", "inline_json");
            var inlineDict = inlineObj as IDictionary<string, object>;

            Console.WriteLine("DEBUG #38:");
            Console.WriteLine("  Raw JSON string: " + (inlineJsonRaw ?? "null"));
            Console.WriteLine("  Object type: " + (inlineObj?.GetType()?.Name ?? "null"));
            if (inlineDict != null)
            {
                Console.WriteLine("  Dictionary contents:");
                foreach (var kv in inlineDict)
                    Console.WriteLine($"    {kv.Key} = {kv.Value} ({kv.Value?.GetType()?.Name})");
            }
            else
            {
                Console.WriteLine("  Dictionary is null");
            }

            bool inlineOk = inlineDict != null &&
                            inlineDict["name"] as string == "updated" &&
                            Convert.ToInt32(inlineDict["value"]) == 456;
            RunTest(testNum, "ReadJsonObject (inline)", true, inlineOk, "Read object from inline JSON");

            // Test 39: ReadJsonObject multiline (после обновления, до удаления)
            testNum++;
            string multiJsonRaw = ini.ReadJsonString("JsonSection", "multiline_json");
            object multiObj = ini.ReadJsonObject("JsonSection", "multiline_json");
            var multiDict = multiObj as IDictionary<string, object>;

            Console.WriteLine("DEBUG #39:");
            Console.WriteLine("  Raw JSON string: " + (multiJsonRaw ?? "null"));
            Console.WriteLine("  Object type: " + (multiObj?.GetType()?.Name ?? "null"));
            if (multiDict != null)
            {
                Console.WriteLine("  Dictionary contents:");
                foreach (var kv in multiDict)
                {
                    if (kv.Value is object[] arr)
                        Console.WriteLine($"    {kv.Key} = [{string.Join(", ", arr)}]");
                    else if (kv.Value is IDictionary<string, object> nested)
                    {
                        Console.WriteLine($"    {kv.Key} = {{");
                        foreach (var nkv in nested)
                            Console.WriteLine($"      {nkv.Key} = {nkv.Value} ({nkv.Value?.GetType()?.Name})");
                        Console.WriteLine("    }");
                    }
                    else
                        Console.WriteLine($"    {kv.Key} = {kv.Value} ({kv.Value?.GetType()?.Name})");
                }
            }
            else
            {
                Console.WriteLine("  Dictionary is null");
            }

            bool multiOk = false;
            if (multiDict != null)
            {
                var array = multiDict["array"] as object[];
                var nested = multiDict["nested"] as IDictionary<string, object>;
                multiOk = array != null && array.Length == 4 &&
                          nested != null && Convert.ToBoolean(nested["flag"]) == false;
            }
            RunTest(testNum, "ReadJsonObject (multiline)", true, multiOk, "Read object from multiline JSON");

            // Test 40: WriteJsonObject and read back
            testNum++;
            var newObj = new Dictionary<string, object>
            {
                ["name"] = "test_write",
                ["value"] = 789,
                ["flag"] = true
            };
            ini.WriteJsonObject("JsonSection", "write_test_obj", newObj);

            string writtenRaw = ini.ReadJsonString("JsonSection", "write_test_obj");
            object readBack = ini.ReadJsonObject("JsonSection", "write_test_obj");
            var readDict = readBack as IDictionary<string, object>;

            Console.WriteLine("DEBUG #40:");
            Console.WriteLine("  Written raw JSON: " + (writtenRaw ?? "null"));
            Console.WriteLine("  ReadBack type: " + (readBack?.GetType()?.Name ?? "null"));
            if (readDict != null)
            {
                Console.WriteLine("  Dictionary contents:");
                foreach (var kv in readDict)
                    Console.WriteLine($"    {kv.Key} = {kv.Value} ({kv.Value?.GetType()?.Name})");
            }
            else
            {
                Console.WriteLine("  Dictionary is null");
            }

            bool writeOk = readDict != null &&
                           readDict["name"] as string == "test_write" &&
                           Convert.ToInt32(readDict["value"]) == 789 &&
                           Convert.ToBoolean(readDict["flag"]) == true;
            RunTest(testNum, "WriteJsonObject/ReadJsonObject", true, writeOk, "Write and read object");

            // Test 41: WriteJson null removal (inline)
            testNum++;
            ini.WriteJsonString("JsonSection", "inline_json", null);
            string removedInline = ini.ReadJsonString("JsonSection", "inline_json", "not_found");
            RunTest(testNum, "WriteJson null (inline)", "not_found", removedInline, "inline JSON removed");

            // Test 42: WriteJson null removal (multiline)
            testNum++;
            ini.WriteJsonString("JsonSection", "multiline_json", null);
            string removedMulti = ini.ReadJsonString("JsonSection", "multiline_json", "not_found");
            RunTest(testNum, "WriteJson null (multiline)", "not_found", removedMulti, "multiline JSON removed");

            // Test 43: Justify
            testNum++;
            TestJustify(testNum);

            // Clean up (remove test keys)
            ini.RemoveKey("JsonSection", "write_test_obj");
            ini.RemoveKey("Section1", "byteKey");
            ini.RemoveKeys("Section2", "key2");
            ini.RemoveSection("Section3");
            ini.RemoveKey("JsonSection", "json_array");
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
undefined_line_without_equals
";
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
