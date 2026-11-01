using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using fNbt;

namespace NbtEditor
{
    internal class Program
    {
        // Extensions: Recognized NBT files for export and --all batch processing
        private static readonly string[] NbtExtensions = { ".dat", ".nbt", ".dat_old" };

        // Since JSON does not store type info, these named lists are forced to specific fixed types
        private static readonly HashSet<string> DoubleListNames = new(StringComparer.Ordinal) { "Pos", "Motion" };
        private static readonly HashSet<string> FloatListNames = new(StringComparer.Ordinal) { "Rotation", "HandDropChances", "ArmorDropChances" };

        private static readonly char[] FractionMarkers = { '.', 'e', 'E' };
        private const int MaxJsonDepth = 512;

        private static readonly JsonWriterOptions JsonWriterOpts = new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private enum SetResult
        {
            NotFound,
            Updated,
            InvalidValue
        }

        // =====================================================================
        //  Entry point
        // =====================================================================

        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch { /* ignore if no console */ }

            if (args.Length == 0)
            {
                ShowHelp();
                return 0;
            }

            try
            {
                string first = args[0];
                bool ok;

                if (first is "--help" or "-h" or "/?")
                {
                    ShowHelp();
                    return 0;
                }
                // JSON -> NBT  (--jsontonbt <file>.json -o <file>.dat)  [-jsontonbt legacy syntax also accepted]
                else if ((first == "--jsontonbt" || first == "-jsontonbt") && args.Length >= 4 && args[2] == "-o")
                {
                    ok = ConvertJsonToNbt(args[1], args[3]);
                }
                // Convert all JSONs in directory to NBT  (--all <dir>  /  --import-all <dir>)
                else if ((first == "--all" || first == "--import-all") && args.Length >= 2)
                {
                    ok = ConvertAllFilesInDirectory(args[1]);
                }
                // Export all NBTs to JSON preserving folder structure  (--export-all <dir>)
                else if (first == "--export-all" && args.Length >= 2)
                {
                    ok = ExportAllToDirectory(args[1]);
                }
                // <file> --find "Query"
                else if (args.Length >= 3 && args[1] == "--find")
                {
                    ok = SearchInNbt(args[0], args[2]);
                }
                // <file> --set "TagName" "Value"
                else if (args.Length >= 4 && args[1] == "--set")
                {
                    ok = SetValueInNbt(args[0], args[2], args[3]);
                }
                // <file> --info
                else if (args.Length >= 2 && args[1] == "--info")
                {
                    ok = ShowFileInfo(args[0]);
                }
                // <file> --structure
                else if (args.Length >= 2 && args[1] == "--structure")
                {
                    ok = ShowStructureInfo(args[0]);
                }
                // <file> --player
                else if (args.Length >= 2 && args[1] == "--player")
                {
                    ok = ShowPlayerSummary(args[0]);
                }
                // <file> --validate
                else if (args.Length >= 2 && args[1] == "--validate")
                {
                    ok = ValidateNbtFile(args[0]);
                }
                // <file> -o <file>.json
                else if (args.Length >= 3 && args[1] == "-o")
                {
                    ok = ConvertNbtToJson(args[0], args[2]);
                }
                else
                {
                    PrintError("Invalid argument format.");
                    ShowHelp();
                    return 1;
                }

                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                PrintError($"An unexpected error occurred: {ex.Message}");
                return 1;
            }
        }

        // =====================================================================
        //  Commands
        // =====================================================================

        private static bool ConvertNbtToJson(string inputPath, string outputPath)
        {
            if (!File.Exists(inputPath))
            {
                PrintError($"Input file not found: '{inputPath}'");
                return false;
            }

            if (IsSamePath(inputPath, outputPath))
            {
                PrintError("Input and output paths are the same. Refusing to overwrite the source file.");
                return false;
            }

            try
            {
                PrintInfo($"Reading '{inputPath}' and converting to JSON...");
                BackupOnce(outputPath);
                WriteNbtAsJson(inputPath, outputPath);
                PrintSuccess($"File saved successfully: '{outputPath}'");
                return true;
            }
            catch (Exception ex)
            {
                PrintFailure("Failed to process NBT file.", ex);
                return false;
            }
        }

        private static bool ConvertJsonToNbt(string inputPath, string outputPath)
        {
            if (!File.Exists(inputPath))
            {
                PrintError($"Input file not found: '{inputPath}'");
                return false;
            }

            if (IsSamePath(inputPath, outputPath))
            {
                PrintError("Input and output paths are the same. Refusing to overwrite the source file.");
                return false;
            }

            try
            {
                PrintInfo($"Parsing '{inputPath}' and converting to NBT...");

                // Convert first; if JSON is invalid, avoid creating an unnecessary backup
                NbtFile nbtFile = ReadJsonAsNbtFile(inputPath);

                BackupOnce(outputPath);
                EnsureDirectoryFor(outputPath);
                WriteAtomically(outputPath, tempPath => nbtFile.SaveToFile(tempPath, NbtCompression.GZip));

                PrintSuccess($"NBT file created successfully: '{outputPath}'");
                return true;
            }
            catch (JsonException jsonEx)
            {
                PrintError("Invalid JSON structure!");
                Console.WriteLine($"Message    : {jsonEx.Message}");
                Console.WriteLine($"Location   : Line {(jsonEx.LineNumber ?? 0) + 1}, Column {(jsonEx.BytePositionInLine ?? 0) + 1}");
                return false;
            }
            catch (Exception ex)
            {
                PrintFailure("Unable to convert JSON to NBT.", ex);
                return false;
            }
        }

        private static bool ConvertAllFilesInDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                PrintError($"Directory not found: '{directoryPath}'");
                return false;
            }

            try
            {
                string[] jsonFiles = Directory.GetFiles(directoryPath, "*.json");
                Array.Sort(jsonFiles, StringComparer.OrdinalIgnoreCase);

                if (jsonFiles.Length == 0)
                {
                    PrintInfo($"No JSON files found in '{directoryPath}'.");
                    return true;
                }

                PrintInfo($"Found {jsonFiles.Length} JSON file(s). Starting batch conversion...");

                int successCount = 0;
                foreach (string jsonFile in jsonFiles)
                {
                    string outputPath = GetNbtOutputPath(jsonFile);
                    try
                    {
                        PrintInfo($"Processing: {Path.GetFileName(jsonFile)} -> {Path.GetFileName(outputPath)}");

                        NbtFile nbtFile = ReadJsonAsNbtFile(jsonFile);

                        BackupOnce(outputPath);
                        WriteAtomically(outputPath, tempPath => nbtFile.SaveToFile(tempPath, NbtCompression.GZip));
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        PrintError($"Failed to convert {Path.GetFileName(jsonFile)}: {ex.Message}");
                    }
                }

                string summary = $"Batch conversion complete! Successfully converted {successCount}/{jsonFiles.Length} file(s).";
                if (successCount == jsonFiles.Length) PrintSuccess(summary);
                else PrintError(summary);

                return successCount == jsonFiles.Length;
            }
            catch (Exception ex)
            {
                PrintError($"Batch process failed: {ex.Message}");
                return false;
            }
        }

        private static bool ExportAllToDirectory(string targetDirectory)
        {
            try
            {
                string fullSource = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDirectory));
                DirectoryInfo sourceDir = new DirectoryInfo(fullSource);
                if (!sourceDir.Exists)
                {
                    PrintError($"Directory not found: '{targetDirectory}'");
                    return false;
                }

                // Output directory: "Output-<FolderName>" next to the source directory
                string parentPath = sourceDir.Parent?.FullName ?? sourceDir.FullName;
                string outputDirName = "Output-" + (sourceDir.Parent == null ? "root" : sourceDir.Name);
                string outputDirPath = Path.Combine(parentPath, outputDirName);

                PrintInfo($"Source Directory: '{sourceDir.FullName}'");
                PrintInfo($"Target Directory: '{outputDirPath}'");

                int convertedCount = 0;
                int failedCount = 0;
                ProcessDirectoryRecursive(sourceDir, sourceDir.FullName, outputDirPath, ref convertedCount, ref failedCount);

                if (failedCount == 0)
                {
                    PrintSuccess($"Export completed successfully! Total {convertedCount} file(s) converted to JSON.");
                    return true;
                }

                PrintError($"Export finished with errors: {convertedCount} file(s) converted, {failedCount} failed.");
                return false;
            }
            catch (Exception ex)
            {
                PrintError($"Export process failed: {ex.Message}");
                return false;
            }
        }

        private static void ProcessDirectoryRecursive(DirectoryInfo currentDir, string rootSourcePath, string rootTargetPath, ref int convertedCount, ref int failedCount)
        {
            string relativePath = Path.GetRelativePath(rootSourcePath, currentDir.FullName);
            string targetSubDir = relativePath == "." ? rootTargetPath : Path.Combine(rootTargetPath, relativePath);
            Directory.CreateDirectory(targetSubDir);

            foreach (FileInfo file in currentDir.GetFiles())
            {
                if (!HasNbtExtension(file.Name)) continue;

                string outputFilePath = Path.Combine(targetSubDir, file.Name + ".json");
                PrintInfo($"Converting: {file.Name} -> {file.Name}.json");

                try
                {
                    WriteNbtAsJson(file.FullName, outputFilePath);
                    convertedCount++;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    PrintError($"Failed to convert '{file.Name}': {ex.Message}");
                }
            }

            foreach (DirectoryInfo subDir in currentDir.GetDirectories())
            {
                // Skip if output directory falls inside the source to avoid recursive looping
                if (IsSamePath(subDir.FullName, rootTargetPath)) continue;

                ProcessDirectoryRecursive(subDir, rootSourcePath, rootTargetPath, ref convertedCount, ref failedCount);
            }
        }

        private static bool ShowFileInfo(string inputPath)
        {
            if (!File.Exists(inputPath))
            {
                PrintError($"Input file not found: '{inputPath}'");
                return false;
            }

            try
            {
                FileInfo fileInfo = new FileInfo(inputPath);
                PrintInfo($"Analyzing NBT file: '{inputPath}'...");

                NbtFile nbtFile = LoadNbt(inputPath);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n--- NBT FILE INFORMATION ---");
                Console.ResetColor();
                Console.WriteLine($"File Name    : {fileInfo.Name}");
                Console.WriteLine($"File Size    : {fileInfo.Length} bytes ({(fileInfo.Length / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} KB)");
                Console.WriteLine($"Root Tag Name: '{nbtFile.RootTag?.Name ?? "Unnamed Root"}'");
                Console.WriteLine($"Root Tag Type: {nbtFile.RootTag?.TagType}");
                Console.WriteLine($"Compression  : {nbtFile.FileCompression}");
                Console.WriteLine("----------------------------");
                return true;
            }
            catch (Exception ex)
            {
                PrintFailure("Failed to read NBT file info.", ex);
                return false;
            }
        }

        private static bool ShowStructureInfo(string inputPath)
        {
            if (!File.Exists(inputPath))
            {
                PrintError($"Input file not found: '{inputPath}'");
                return false;
            }

            try
            {
                PrintInfo($"Analyzing structure file: '{inputPath}'...");
                NbtFile nbtFile = LoadNbt(inputPath);

                if (nbtFile.RootTag is not NbtCompound root)
                {
                    PrintError("Invalid structure file format.");
                    return false;
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n--- STRUCTURE SUMMARY ---");
                Console.ResetColor();

                // In real structure files, "size" is an Int list; some tools write IntArray. Both are supported.
                int[]? dims = ReadIntTriple(root.Get("size"));
                if (dims != null)
                {
                    Console.WriteLine($"Dimensions (X x Y x Z): {dims[0]} x {dims[1]} x {dims[2]}");
                }
                else
                {
                    Console.WriteLine("Dimensions: Not found (may not be a standard structure file)");
                }

                if (root.Get("blocks") is NbtList blocksList)
                {
                    Console.WriteLine($"Total Blocks Recorded : {blocksList.Count}");
                }
                else
                {
                    Console.WriteLine("Blocks List: Not found");
                }

                if (root.Get("palette") is NbtList paletteList)
                {
                    Console.WriteLine($"Palette Entries       : {paletteList.Count}");
                }

                double? dataVersion = ReadNumber(root.Get("DataVersion"));
                if (dataVersion.HasValue)
                {
                    Console.WriteLine($"Data Version          : {FormatNumber(dataVersion.Value)}");
                }

                Console.WriteLine("-------------------------");
                return true;
            }
            catch (Exception ex)
            {
                PrintFailure("Failed to read structure file.", ex);
                return false;
            }
        }

        private static bool ShowPlayerSummary(string inputPath)
        {
            if (!File.Exists(inputPath))
            {
                PrintError($"Input file not found: '{inputPath}'");
                return false;
            }

            try
            {
                PrintInfo($"Extracting player summary from: '{inputPath}'...");
                NbtFile nbtFile = LoadNbt(inputPath);

                if (nbtFile.RootTag is not NbtCompound rootCompound)
                {
                    PrintError("Invalid player data file.");
                    return false;
                }

                // playerdata/<uuid>.dat -> at root; level.dat -> Data > Player
                NbtCompound player = FindPlayerCompound(rootCompound);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n--- PLAYER SUMMARY ---");
                Console.ResetColor();

                double? health = ReadNumber(player.Get("Health"));
                double? foodLevel = ReadNumber(player.Get("foodLevel"));
                double? xpLevel = ReadNumber(player.Get("XpLevel"));

                Console.WriteLine($"Health    : {(health.HasValue ? FormatNumber(health.Value) : "Unknown")}");
                Console.WriteLine($"Food Level: {(foodLevel.HasValue ? FormatNumber(foodLevel.Value) : "Unknown")}");
                Console.WriteLine($"XP Level  : {(xpLevel.HasValue ? FormatNumber(xpLevel.Value) : "Unknown")}");

                if (player.Get("Pos") is NbtList posList && posList.Count >= 3)
                {
                    double? px = ReadNumber(posList[0]);
                    double? py = ReadNumber(posList[1]);
                    double? pz = ReadNumber(posList[2]);

                    if (px.HasValue && py.HasValue && pz.HasValue)
                    {
                        CultureInfo inv = CultureInfo.InvariantCulture;
                        Console.WriteLine($"Position  : X: {px.Value.ToString("F2", inv)}, Y: {py.Value.ToString("F2", inv)}, Z: {pz.Value.ToString("F2", inv)}");
                    }
                    else
                    {
                        Console.WriteLine("Position  : Unknown");
                    }
                }
                else
                {
                    Console.WriteLine("Position  : Unknown");
                }

                Console.WriteLine("----------------------");
                return true;
            }
            catch (Exception ex)
            {
                PrintFailure("Failed to read player summary.", ex);
                return false;
            }
        }

        private static bool ValidateNbtFile(string inputPath)
        {
            if (!File.Exists(inputPath))
            {
                PrintError($"Input file not found: '{inputPath}'");
                return false;
            }

            try
            {
                PrintInfo($"Validating NBT file integrity: '{inputPath}'...");
                NbtFile nbtFile = LoadNbt(inputPath); // throws InvalidDataException if root tag is missing

                PrintSuccess($"Validation successful! The NBT file is healthy and structurally sound (compression: {nbtFile.FileCompression}).");
                return true;
            }
            catch (Exception ex)
            {
                PrintFailure("Validation failed! The NBT file is corrupted or unreadable.", ex);
                return false;
            }
        }

        private static bool SearchInNbt(string inputPath, string searchQuery)
        {
            if (!File.Exists(inputPath))
            {
                PrintError($"Input file not found: '{inputPath}'");
                return false;
            }

            try
            {
                PrintInfo($"Searching for '{searchQuery}' in '{inputPath}'...");
                NbtFile nbtFile = LoadNbt(inputPath);

                int matchCount = 0;
                SearchTagRecursive(nbtFile.RootTag!, string.Empty, searchQuery, ref matchCount);

                PrintSuccess($"Search complete. Found {matchCount} match(es) for '{searchQuery}'.");
                return true;
            }
            catch (Exception ex)
            {
                PrintFailure("Failed to search inside NBT file.", ex);
                return false;
            }
        }

        private static bool SetValueInNbt(string inputPath, string tagName, string newValueStr)
        {
            if (!File.Exists(inputPath))
            {
                PrintError($"Input file not found: '{inputPath}'");
                return false;
            }

            try
            {
                PrintInfo($"Updating tag '{tagName}' in '{inputPath}'...");
                NbtFile nbtFile = LoadNbt(inputPath);

                SetResult result = UpdateTagRecursive(nbtFile.RootTag!, tagName, newValueStr);

                switch (result)
                {
                    case SetResult.Updated:
                        // Backup only if changes are actually made and only taken for the first time
                        BackupOnce(inputPath);
                        NbtCompression compression = ResolveCompression(nbtFile.FileCompression);
                        WriteAtomically(inputPath, tempPath => nbtFile.SaveToFile(tempPath, compression));
                        PrintSuccess($"Tag '{tagName}' successfully updated and saved to '{inputPath}'!");
                        return true;

                    case SetResult.InvalidValue:
                        // Error message was already printed inside UpdateTagRecursive
                        return false;

                    default:
                        PrintError($"No editable tag named '{tagName}' was found (only Byte, Short, Int, Long, Float, Double and String tags can be changed).");
                        return false;
                }
            }
            catch (Exception ex)
            {
                PrintFailure("Failed to update NBT file.", ex);
                return false;
            }
        }

        // =====================================================================
        //  --set helpers
        // =====================================================================

        private static SetResult UpdateTagRecursive(NbtTag tag, string targetName, string newValueStr)
        {
            if (tag is NbtCompound compound)
            {
                foreach (NbtTag child in compound)
                {
                    if (child.Name != null && child.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase) && IsSettable(child))
                    {
                        if (TryUpdateTagValue(child, newValueStr)) return SetResult.Updated;

                        PrintError($"'{newValueStr}' is not a valid value for tag '{child.Name}' of type {child.TagType}.");
                        return SetResult.InvalidValue;
                    }

                    if (child is NbtCompound || child is NbtList)
                    {
                        SetResult nested = UpdateTagRecursive(child, targetName, newValueStr);
                        if (nested != SetResult.NotFound) return nested;
                    }
                }
            }
            else if (tag is NbtList list)
            {
                foreach (NbtTag child in list)
                {
                    if (child is NbtCompound || child is NbtList)
                    {
                        SetResult nested = UpdateTagRecursive(child, targetName, newValueStr);
                        if (nested != SetResult.NotFound) return nested;
                    }
                }
            }

            return SetResult.NotFound;
        }

        private static bool IsSettable(NbtTag tag)
        {
            return tag.TagType is NbtTagType.Byte or NbtTagType.Short or NbtTagType.Int
                or NbtTagType.Long or NbtTagType.Float or NbtTagType.Double or NbtTagType.String;
        }

        private static bool TryUpdateTagValue(NbtTag tag, string value)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;

            switch (tag)
            {
                case NbtByte nbtByte:
                    if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) { nbtByte.Value = 1; return true; }
                    if (value.Equals("false", StringComparison.OrdinalIgnoreCase)) { nbtByte.Value = 0; return true; }
                    if (sbyte.TryParse(value, NumberStyles.Integer, inv, out sbyte signedByte))
                    {
                        nbtByte.Value = unchecked((byte)signedByte);
                        return true;
                    }
                    if (byte.TryParse(value, NumberStyles.Integer, inv, out byte unsignedByte))
                    {
                        nbtByte.Value = unsignedByte;
                        return true;
                    }
                    return false;

                case NbtShort nbtShort:
                    if (short.TryParse(value, NumberStyles.Integer, inv, out short shortValue))
                    {
                        nbtShort.Value = shortValue;
                        return true;
                    }
                    return false;

                case NbtInt nbtInt:
                    if (int.TryParse(value, NumberStyles.Integer, inv, out int intValue))
                    {
                        nbtInt.Value = intValue;
                        return true;
                    }
                    return false;

                case NbtLong nbtLong:
                    if (long.TryParse(value, NumberStyles.Integer, inv, out long longValue))
                    {
                        nbtLong.Value = longValue;
                        return true;
                    }
                    return false;

                case NbtFloat nbtFloat:
                    if (float.TryParse(value, NumberStyles.Float, inv, out float floatValue))
                    {
                        nbtFloat.Value = floatValue;
                        return true;
                    }
                    return false;

                case NbtDouble nbtDouble:
                    if (double.TryParse(value, NumberStyles.Float, inv, out double doubleValue))
                    {
                        nbtDouble.Value = doubleValue;
                        return true;
                    }
                    return false;

                case NbtString nbtString:
                    nbtString.Value = value;
                    return true;

                default:
                    return false;
            }
        }

        // =====================================================================
        //  --find helpers
        // =====================================================================

        private static void SearchTagRecursive(NbtTag tag, string path, string query, ref int matchCount)
        {
            string valueStr = GetValueString(tag);
            bool nameMatches = !string.IsNullOrEmpty(tag.Name) && tag.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
            bool valueMatches = valueStr.Length > 0 && valueStr.Contains(query, StringComparison.OrdinalIgnoreCase);

            if (nameMatches || valueMatches)
            {
                matchCount++;
                string shownPath = path.Length == 0 ? "(root)" : path;
                string valuePart = valueStr.Length > 0 ? $" | Value: '{Truncate(valueStr, 80)}'" : string.Empty;

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[MATCH #{matchCount}] Path: '{shownPath}' (Type: {tag.TagType}){valuePart}");
                Console.ResetColor();
            }

            if (tag is NbtCompound compound)
            {
                foreach (NbtTag child in compound)
                {
                    string childName = child.Name ?? string.Empty;
                    string childPath = path.Length == 0 ? childName : path + "." + childName;
                    SearchTagRecursive(child, childPath, query, ref matchCount);
                }
            }
            else if (tag is NbtList list)
            {
                int index = 0;
                foreach (NbtTag child in list)
                {
                    SearchTagRecursive(child, $"{path}[{index}]", query, ref matchCount);
                    index++;
                }
            }
        }

        private static string GetValueString(NbtTag tag)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;

            switch (tag)
            {
                case NbtByte nbtByte: return unchecked((sbyte)nbtByte.Value).ToString(inv);
                case NbtShort nbtShort: return nbtShort.Value.ToString(inv);
                case NbtTag tagInt when tagInt is NbtInt nbtInt: return nbtInt.Value.ToString(inv);
                case NbtLong nbtLong: return nbtLong.Value.ToString(inv);
                case NbtFloat nbtFloat: return nbtFloat.Value.ToString(inv);
                case NbtDouble nbtDouble: return nbtDouble.Value.ToString(inv);
                case NbtString nbtString: return nbtString.Value ?? string.Empty;
                case NbtByteArray byteArray: return string.Join(",", byteArray.Value.Select(x => unchecked((sbyte)x)));
                case NbtIntArray intArray: return string.Join(",", intArray.Value);
                case NbtLongArray longArray: return string.Join(",", longArray.Value);
                default: return string.Empty; // Compound / List
            }
        }

        // =====================================================================
        //  NBT -> JSON
        // =====================================================================

        private static NbtFile LoadNbt(string path)
        {
            NbtFile nbtFile = new NbtFile();
            nbtFile.LoadFromFile(path);

            if (nbtFile.RootTag == null)
            {
                throw new InvalidDataException("The NBT file does not contain a root tag.");
            }

            return nbtFile;
        }

        private static void WriteNbtAsJson(string inputPath, string outputPath)
        {
            NbtFile nbtFile = LoadNbt(inputPath);
            EnsureDirectoryFor(outputPath);

            WriteAtomically(outputPath, tempPath =>
            {
                using FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                using Utf8JsonWriter writer = new Utf8JsonWriter(stream, JsonWriterOpts);
                WriteTagValue(writer, nbtFile.RootTag!);
            });
        }

        private static void WriteTagValue(Utf8JsonWriter writer, NbtTag tag)
        {
            switch (tag)
            {
                case NbtCompound compound:
                    writer.WriteStartObject();
                    foreach (NbtTag child in compound)
                    {
                        writer.WritePropertyName(child.Name ?? string.Empty);
                        WriteTagValue(writer, child);
                    }
                    writer.WriteEndObject();
                    break;

                case NbtList list:
                    writer.WriteStartArray();
                    foreach (NbtTag item in list)
                    {
                        WriteTagValue(writer, item);
                    }
                    writer.WriteEndArray();
                    break;

                case NbtByte nbtByte:
                    writer.WriteNumberValue((int)unchecked((sbyte)nbtByte.Value)); // Bytes are signed in Minecraft
                    break;

                case NbtShort nbtShort:
                    writer.WriteNumberValue(nbtShort.Value);
                    break;

                case NbtInt nbtInt:
                    writer.WriteNumberValue(nbtInt.Value);
                    break;

                case NbtLong nbtLong:
                    writer.WriteNumberValue(nbtLong.Value);
                    break;

                case NbtFloat nbtFloat:
                    writer.WriteNumberValue(nbtFloat.Value);
                    break;

                case NbtDouble nbtDouble:
                    writer.WriteNumberValue(nbtDouble.Value);
                    break;

                case NbtString nbtString:
                    writer.WriteStringValue(nbtString.Value ?? string.Empty);
                    break;

                case NbtByteArray byteArray:
                    writer.WriteStartArray();
                    foreach (byte b in byteArray.Value)
                    {
                        writer.WriteNumberValue((int)unchecked((sbyte)b));
                    }
                    writer.WriteEndArray();
                    break;

                case NbtIntArray intArray:
                    writer.WriteStartArray();
                    foreach (int i in intArray.Value)
                    {
                        writer.WriteNumberValue(i);
                    }
                    writer.WriteEndArray();
                    break;

                case NbtLongArray longArray:
                    writer.WriteStartArray();
                    foreach (long l in longArray.Value)
                    {
                        writer.WriteNumberValue(l);
                    }
                    writer.WriteEndArray();
                    break;
            }
        }

        // =====================================================================
        //  JSON -> NBT
        // =====================================================================

        private static NbtFile ReadJsonAsNbtFile(string jsonPath)
        {
            string jsonString = File.ReadAllText(jsonPath);
            JsonDocumentOptions docOptions = new JsonDocumentOptions { MaxDepth = MaxJsonDepth };

            using JsonDocument doc = JsonDocument.Parse(jsonString, docOptions);
            JsonElement root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Root element of JSON must be an Object.");
            }

            NbtCompound rootCompound = (NbtCompound)ParseJsonElement(string.Empty, root);
            return new NbtFile(rootCompound);
        }

        private static NbtTag ParseJsonElement(string tagName, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    {
                        NbtCompound compound = new NbtCompound(tagName);
                        foreach (JsonProperty prop in element.EnumerateObject())
                        {
                            NbtTag child = ParseJsonElement(prop.Name, prop.Value);
                            if (child != null) compound.Add(child);
                        }
                        return compound;
                    }

                case JsonValueKind.Array:
                    {
                        JsonElement[] items = element.EnumerateArray().ToArray();

                        if (tagName.Equals("UUID", StringComparison.OrdinalIgnoreCase))
                        {
                            int[] intArr = items.Select(x => x.TryGetInt32(out int val) ? val : (int)x.GetInt64()).ToArray();
                            return new NbtIntArray(tagName, intArr);
                        }

                        if (DoubleListNames.Contains(tagName))
                        {
                            NbtList list = new NbtList(tagName, NbtTagType.Double);
                            foreach (JsonElement item in items) list.Add(new NbtDouble(item.GetDouble()));
                            return list;
                        }

                        if (FloatListNames.Contains(tagName))
                        {
                            NbtList list = new NbtList(tagName, NbtTagType.Float);
                            foreach (JsonElement item in items) list.Add(new NbtFloat((float)item.GetDouble()));
                            return list;
                        }

                        // Check if all elements are numbers and at least one has a fractional part
                        bool allNumbers = items.Length > 0 && items.All(x => x.ValueKind == JsonValueKind.Number);
                        bool hasFraction = allNumbers && items.Any(x => ContainsFractionMarker(x.GetRawText()));

                        if (allNumbers && hasFraction)
                        {
                            NbtList list = new NbtList(tagName, NbtTagType.Double);
                            foreach (JsonElement item in items) list.Add(new NbtDouble(item.GetDouble()));
                            return list;
                        }

                        NbtList defaultList = new NbtList(tagName);
                        foreach (JsonElement item in items)
                        {
                            NbtTag parsedItem = ParseJsonElement(string.Empty, item);
                            if (parsedItem != null)
                            {
                                parsedItem.Name = string.Empty;
                                try
                                {
                                    defaultList.Add(parsedItem);
                                }
                                catch
                                {
                                    defaultList.Add(new NbtString(item.ToString()));
                                }
                            }
                        }
                        return defaultList;
                    }

                case JsonValueKind.String:
                    {
                        string val = element.GetString() ?? string.Empty;
                        return string.IsNullOrEmpty(tagName) ? new NbtString(val) : new NbtString(tagName, val);
                    }

                case JsonValueKind.Number:
                    {
                        if (element.TryGetInt32(out int iVal))
                            return string.IsNullOrEmpty(tagName) ? new NbtInt(iVal) : new NbtInt(tagName, iVal);
                        if (element.TryGetInt64(out long lVal))
                            return string.IsNullOrEmpty(tagName) ? new NbtLong(lVal) : new NbtLong(tagName, lVal);
                        if (element.TryGetDouble(out double dVal))
                            return string.IsNullOrEmpty(tagName) ? new NbtDouble(dVal) : new NbtDouble(tagName, dVal);

                        throw new InvalidDataException($"Unsupported numeric value for key '{tagName}'.");
                    }

                case JsonValueKind.True:
                case JsonValueKind.False:
                    {
                        byte bVal = (byte)(element.GetBoolean() ? 1 : 0);
                        return string.IsNullOrEmpty(tagName) ? new NbtByte(bVal) : new NbtByte(tagName, bVal);
                    }

                default:
                    return null;
            }
        }

        // =====================================================================
        //  General helpers & utilities
        // =====================================================================

        private static NbtCompound FindPlayerCompound(NbtCompound root)
        {
            // If "Health" exists at root, it's a direct playerdata file
            if (root.Get("Health") != null) return root;

            // In level.dat, player data is located under Data > Player
            if (root.Get("Data") is NbtCompound data && data.Get("Player") is NbtCompound levelPlayer)
            {
                return levelPlayer;
            }

            // Fallback: return root if not found
            return root;
        }

        private static int[]? ReadIntTriple(NbtTag tag)
        {
            if (tag is NbtIntArray arr && arr.Value.Length >= 3)
            {
                return new[] { arr.Value[0], arr.Value[1], arr.Value[2] };
            }
            if (tag is NbtList list && list.Count >= 3 && list.TagType == NbtTagType.Int)
            {
                int[] res = new int[3];
                for (int i = 0; i < 3; i++) res[i] = ((NbtInt)list[i]).Value;
                return res;
            }
            return null;
        }

        private static double? ReadNumber(NbtTag tag)
        {
            return tag switch
            {
                NbtByte b => b.Value,
                NbtShort s => s.Value,
                NbtInt i => i.Value,
                NbtLong l => l.Value,
                NbtFloat f => f.Value,
                NbtDouble d => d.Value,
                _ => null
            };
        }

        private static string FormatNumber(double val)
        {
            return val.ToString(val % 1 == 0 ? "F0" : "F2", CultureInfo.InvariantCulture);
        }

        private static bool ContainsFractionMarker(string text)
        {
            foreach (char c in FractionMarkers)
            {
                if (text.Contains(c)) return true;
            }
            return false;
        }

        private static bool HasNbtExtension(string fileName)
        {
            foreach (string ext in NbtExtensions)
            {
                if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string GetNbtOutputPath(string jsonFile)
        {
            // If ends with .dat.json, strip .json to get .dat
            if (jsonFile.EndsWith(".dat.json", StringComparison.OrdinalIgnoreCase))
            {
                return jsonFile.Substring(0, jsonFile.Length - 5);
            }
            // If ends with .nbt.json, strip .json to get .nbt
            if (jsonFile.EndsWith(".nbt.json", StringComparison.OrdinalIgnoreCase))
            {
                return jsonFile.Substring(0, jsonFile.Length - 5);
            }
            return Path.ChangeExtension(jsonFile, ".dat");
        }

        private static bool IsSamePath(string path1, string path2)
        {
            try
            {
                string full1 = Path.GetFullPath(path1);
                string full2 = Path.GetFullPath(path2);
                return string.Equals(full1, full2, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void BackupOnce(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    string backupPath = filePath + ".bak";
                    if (!File.Exists(backupPath))
                    {
                        File.Copy(filePath, backupPath, false);
                        PrintInfo($"Backup created: '{backupPath}'");
                    }
                }
            }
            catch (Exception ex)
            {
                PrintInfo($"Warning: Could not create backup file: {ex.Message}");
            }
        }

        private static void EnsureDirectoryFor(string filePath)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private static void WriteAtomically(string targetPath, Action<string> writeAction)
        {
            EnsureDirectoryFor(targetPath);
            string tempPath = targetPath + ".tmp";

            try
            {
                writeAction(tempPath);
                if (File.Exists(targetPath)) File.Delete(targetPath);
                File.Move(tempPath, targetPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        private static NbtCompression ResolveCompression(NbtCompression original)
        {
            // If file was uncompressed or custom, fallback safely to GZip or keep original
            return original == NbtCompression.None ? NbtCompression.None : NbtCompression.GZip;
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }

        private static void PrintInfo(string message)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[INFO] {message}");
            Console.ResetColor();
        }

        private static void PrintSuccess(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[SUCCESS] {message}");
            Console.ResetColor();
        }

        private static void PrintError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] {message}");
            Console.ResetColor();
        }

        private static void PrintFailure(string message, Exception ex)
        {
            PrintError(message);
            Console.WriteLine($"Details: {ex.Message}");
        }

        private static void ShowHelp()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Usage:");
            Console.WriteLine("  NBTEditor <file_name>.dat_or_nbt -o <file_name>.json");
            Console.WriteLine("  NBTEditor --jsontonbt <file_name>.json -o <file_name>.dat_or_nbt");
            Console.WriteLine("  NBTEditor <file_name>.dat_or_nbt --find \"SearchQuery\"");
            Console.WriteLine("  NBTEditor <file_name>.dat_or_nbt --set \"TagName\" \"NewValue\"");
            Console.WriteLine("  NBTEditor <file_name>.dat_or_nbt --info");
            Console.WriteLine("  NBTEditor <file_name>.nbt --structure");
            Console.WriteLine("  NBTEditor <file_name>.dat --player");
            Console.WriteLine("  NBTEditor <file_name>.dat --validate");
            Console.WriteLine("  NBTEditor --all <directory_path>");
            Console.WriteLine("  NBTEditor --export-all <directory_path>");
            Console.ResetColor();
        }
    }
}