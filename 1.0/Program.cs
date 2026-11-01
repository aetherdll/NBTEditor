using System;
using System.IO;
using System.Text.Json;
using fNbt;

namespace NbtEditor
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                ShowHelp();
                return;
            }

            // Mode 1: JSON to NBT (-jsontonbt <file>.json -o <file>.dat)
            if (args[0] == "-jsontonbt" && args.Length >= 4 && args[2] == "-o")
            {
                ConvertJsonToNbt(args[1], args[3]);
            }
            // Mode 2: Search inside NBT (<file>.dat --find "Query")
            else if (args.Length >= 2 && args[1] == "--find")
            {
                SearchInNbt(args[0], args[2]);
            }
            // Mode 3: Set value directly (<file>.dat --set "TagName" "Value")
            else if (args.Length >= 4 && args[1] == "--set")
            {
                SetValueInNbt(args[0], args[2], args[3]);
            }
            // Mode 4: Show NBT file info (<file>.dat --info)
            else if (args.Length >= 2 && args[1] == "--info")
            {
                ShowFileInfo(args[0]);
            }
            // Mode 5: Show structure details (<file>.nbt --structure)
            else if (args.Length >= 2 && args[1] == "--structure")
            {
                ShowStructureInfo(args[0]);
            }
            // Mode 6: Show player summary (<file>.dat --player)
            else if (args.Length >= 2 && args[1] == "--player")
            {
                ShowPlayerSummary(args[0]);
            }
            // Mode 7: Validate NBT file (<file>.dat --validate)
            else if (args.Length >= 2 && args[1] == "--validate")
            {
                ValidateNbtFile(args[0]);
            }
            // Mode 8: NBT to JSON (<file>.dat -o <file>.json)
            else if (args.Length >= 3 && args[1] == "-o")
            {
                ConvertNbtToJson(args[0], args[2]);
            }
            // Mode 9: Convert all JSON files in a directory to DAT (--all <directory_path>)
            else if (args.Length >= 2 && args[0] == "--all")
            {
                ConvertAllFilesInDirectory(args[1]);
            }
            // Mode 10: Export all DAT/NBT files to JSON preserving folder structure (--export-all <directory_path>)
            else if (args.Length >= 2 && args[0] == "--export-all")
            {
                ExportAllToDirectory(args[1]);
            }
            else
            {
                PrintError("Invalid argument format.");
                ShowHelp();
            }
        }

        private static void ExportAllToDirectory(string targetDirectory)
        {
            try
            {
                DirectoryInfo sourceDir = new DirectoryInfo(targetDirectory);
                if (!sourceDir.Exists)
                {
                    PrintError($"Directory not found: '{targetDirectory}'");
                    return;
                }

                // Output klasör adını oluştur: Output-KlasorAdi
                string parentPath = sourceDir.Parent != null ? sourceDir.Parent.FullName : sourceDir.FullName;
                string outputDirName = "Output-" + sourceDir.Name;
                string outputDirPath = Path.Combine(parentPath, outputDirName);

                PrintInfo($"Source Directory: '{sourceDir.FullName}'");
                PrintInfo($"Target Directory: '{outputDirPath}'");

                // Klasör ağacını ve içindeki .dat/.nbt dosyalarını tara
                int convertedCount = 0;
                ProcessDirectoryRecursive(sourceDir, sourceDir.FullName, outputDirPath, ref convertedCount);

                PrintSuccess($"Export completed successfully! Total {convertedCount} file(s) converted to JSON.");
            }
            catch (Exception ex)
            {
                PrintError($"Export process failed: {ex.Message}");
            }
        }

        private static void ProcessDirectoryRecursive(DirectoryInfo currentDir, string rootSourcePath, string rootTargetPath, ref int convertedCount)
        {
            // Hedef alt klasör yolunu oluştur ve oluştur/garantile
            string relativePath = Path.GetRelativePath(rootSourcePath, currentDir.FullName);
            string targetSubDir = relativePath == "." ? rootTargetPath : Path.Combine(rootTargetPath, relativePath);

            if (!Directory.Exists(targetSubDir))
            {
                Directory.CreateDirectory(targetSubDir);
            }

            // Klasördeki .dat ve .nbt dosyalarını al
            FileInfo[] files = currentDir.GetFiles();
            foreach (FileInfo file in files)
            {
                string ext = file.Extension.ToLower();
                if (ext == ".dat" || ext == ".nbt")
                {
                    string outputFilePath = Path.Combine(targetSubDir, file.Name + ".json");
                    PrintInfo($"Converting: {file.Name} -> {file.Name}.json");

                    try
                    {
                        ConvertNbtToJsonInternal(file.FullName, outputFilePath);
                        convertedCount++;
                    }
                    catch (Exception ex)
                    {
                        PrintError($"Failed to convert '{file.Name}': {ex.Message}");
                    }
                }
            }

            // Alt klasörler için yinelemeli (recursive) tara
            foreach (DirectoryInfo subDir in currentDir.GetDirectories())
            {
                // Çıktı klasörünü tekrar taramamak için kontrol
                if (subDir.Name.StartsWith("Output-")) continue;

                ProcessDirectoryRecursive(subDir, rootSourcePath, rootTargetPath, ref convertedCount);
            }
        }

        private static void ConvertNbtToJsonInternal(string inputPath, string outputPath)
        {
            var nbtFile = new NbtFile();
            nbtFile.LoadFromFile(inputPath);

            if (nbtFile.RootTag == null)
            {
                throw new InvalidDataException("The NBT file does not contain a root tag.");
            }

            var jsonElement = ConvertNbtTagToJsonElement(nbtFile.RootTag);
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string jsonString = JsonSerializer.Serialize(jsonElement, options);
            File.WriteAllText(outputPath, jsonString);
        }

        private static void ConvertAllFilesInDirectory(string directoryPath)
        {
            try
            {
                if (!Directory.Exists(directoryPath))
                {
                    PrintError($"Directory not found: '{directoryPath}'");
                    return;
                }

                string[] jsonFiles = Directory.GetFiles(directoryPath, "*.json");
                if (jsonFiles.Length == 0)
                {
                    PrintInfo($"No JSON files found in '{directoryPath}'.");
                    return;
                }

                PrintInfo($"Found {jsonFiles.Length} JSON file(s). Starting batch conversion...");

                int successCount = 0;
                foreach (string jsonFile in jsonFiles)
                {
                    string outputPath = Path.ChangeExtension(jsonFile, ".dat");
                    try
                    {
                        PrintInfo($"Processing: {Path.GetFileName(jsonFile)} -> {Path.GetFileName(outputPath)}");

                        string jsonString = File.ReadAllText(jsonFile);
                        using (JsonDocument jsonDocument = JsonDocument.Parse(jsonString))
                        {
                            JsonElement rootElement = jsonDocument.RootElement;
                            if (rootElement.ValueKind != JsonValueKind.Object)
                            {
                                PrintError($"Skipped {Path.GetFileName(jsonFile)}: Root element must be an Object.");
                                continue;
                            }

                            NbtCompound rootCompound = ConvertJsonToNbtCompound(rootElement, string.Empty);
                            var nbtFile = new NbtFile(rootCompound);
                            nbtFile.SaveToFile(outputPath, NbtCompression.GZip);
                            successCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        PrintError($"Failed to convert {Path.GetFileName(jsonFile)}: {ex.Message}");
                    }
                }

                PrintSuccess($"Batch conversion complete! Successfully converted {successCount}/{jsonFiles.Length} file(s).");
            }
            catch (Exception ex)
            {
                PrintError($"Batch process failed: {ex.Message}");
            }
        }

        private static void ShowFileInfo(string inputPath)
        {
            try
            {
                if (!File.Exists(inputPath))
                {
                    PrintError($"Input file not found: '{inputPath}'");
                    return;
                }

                FileInfo fileInfo = new FileInfo(inputPath);
                PrintInfo($"Analyzing NBT file: '{inputPath}'...");

                var nbtFile = new NbtFile();
                nbtFile.LoadFromFile(inputPath);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n--- NBT FILE INFORMATION ---");
                Console.ResetColor();
                Console.WriteLine($"File Name    : {fileInfo.Name}");
                Console.WriteLine($"File Size    : {fileInfo.Length} bytes ({fileInfo.Length / 1024.0:F2} KB)");
                Console.WriteLine($"Root Tag Name: '{nbtFile.RootTag?.Name ?? "Unnamed Root"}'");
                Console.WriteLine($"Root Tag Type: {nbtFile.RootTag?.TagType}");
                Console.WriteLine($"Compression  : {nbtFile.FileCompression}");
                Console.WriteLine("----------------------------");
            }
            catch (Exception ex)
            {
                PrintError("Failed to read NBT file info.");
                Console.WriteLine($"Details: {ex.Message}");
            }
        }

        private static void ShowStructureInfo(string inputPath)
        {
            try
            {
                if (!File.Exists(inputPath))
                {
                    PrintError($"Input file not found: '{inputPath}'");
                    return;
                }

                PrintInfo($"Analyzing structure file: '{inputPath}'...");
                var nbtFile = new NbtFile();
                nbtFile.LoadFromFile(inputPath);

                if (nbtFile.RootTag == null || nbtFile.RootTag.TagType != NbtTagType.Compound)
                {
                    PrintError("Invalid structure file format.");
                    return;
                }

                var root = (NbtCompound)nbtFile.RootTag;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n--- STRUCTURE SUMMARY ---");
                Console.ResetColor();

                if (root.Get<NbtIntArray>("size") is NbtIntArray sizeArray)
                {
                    var dims = sizeArray.Value;
                    if (dims.Length >= 3)
                    {
                        Console.WriteLine($"Dimensions (X x Y x Z): {dims[0]} x {dims[1]} x {dims[2]}");
                    }
                }
                else
                {
                    Console.WriteLine("Dimensions: Not found (may not be a standard structure file)");
                }

                if (root.Get<NbtList>("blocks") is NbtList blocksList)
                {
                    Console.WriteLine($"Total Blocks Recorded : {blocksList.Count}");
                }
                else
                {
                    Console.WriteLine("Blocks List: Not found");
                }
                Console.WriteLine("-------------------------");
            }
            catch (Exception ex)
            {
                PrintError("Failed to read structure file.");
                Console.WriteLine($"Details: {ex.Message}");
            }
        }

        private static void ShowPlayerSummary(string inputPath)
        {
            try
            {
                if (!File.Exists(inputPath))
                {
                    PrintError($"Input file not found: '{inputPath}'");
                    return;
                }

                PrintInfo($"Extracting player summary from: '{inputPath}'...");
                var nbtFile = new NbtFile();
                nbtFile.LoadFromFile(inputPath);

                if (nbtFile.RootTag == null || nbtFile.RootTag.TagType != NbtTagType.Compound)
                {
                    PrintError("Invalid player data file.");
                    return;
                }

                var root = (NbtCompound)nbtFile.RootTag;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n--- PLAYER SUMMARY ---");
                Console.ResetColor();

                float health = root.Get<NbtFloat>("Health")?.Value ?? -1;
                int foodLevel = root.Get<NbtInt>("foodLevel")?.Value ?? -1;
                int xpLevel = root.Get<NbtInt>("XpLevel")?.Value ?? -1;

                Console.WriteLine($"Health    : {(health >= 0 ? health.ToString() : "Unknown")}");
                Console.WriteLine($"Food Level: {(foodLevel >= 0 ? foodLevel.ToString() : "Unknown")}");
                Console.WriteLine($"XP Level  : {(xpLevel >= 0 ? xpLevel.ToString() : "Unknown")}");

                if (root.Get<NbtList>("Pos") is NbtList posList && posList.Count >= 3)
                {
                    double x = ((NbtDouble)posList[0]).Value;
                    double y = ((NbtDouble)posList[1]).Value;
                    double z = ((NbtDouble)posList[2]).Value;
                    Console.WriteLine($"Position  : X: {x:F2}, Y: {y:F2}, Z: {z:F2}");
                }
                else
                {
                    Console.WriteLine("Position  : Unknown");
                }
                Console.WriteLine("----------------------");
            }
            catch (Exception ex)
            {
                PrintError("Failed to read player summary.");
                Console.WriteLine($"Details: {ex.Message}");
            }
        }

        private static void ValidateNbtFile(string inputPath)
        {
            try
            {
                if (!File.Exists(inputPath))
                {
                    PrintError($"Input file not found: '{inputPath}'");
                    return;
                }

                PrintInfo($"Validating NBT file integrity: '{inputPath}'...");
                var nbtFile = new NbtFile();
                nbtFile.LoadFromFile(inputPath);

                if (nbtFile.RootTag != null)
                {
                    PrintSuccess("Validation successful! The NBT file is healthy and structurally sound.");
                }
                else
                {
                    PrintError("Validation failed: Root tag is missing.");
                }
            }
            catch (Exception ex)
            {
                PrintError("Validation failed! The NBT file is corrupted or unreadable.");
                Console.WriteLine($"Details: {ex.Message}");
            }
        }

        private static void ConvertNbtToJson(string inputPath, string outputPath)
        {
            try
            {
                if (!File.Exists(inputPath))
                {
                    PrintError($"Input file not found: '{inputPath}'");
                    return;
                }

                PrintInfo($"Reading '{inputPath}' and converting to JSON...");
                ConvertNbtToJsonInternal(inputPath, outputPath);
                PrintSuccess($"File saved successfully: '{outputPath}'");
            }
            catch (Exception ex)
            {
                PrintError("Failed to process NBT file.");
                Console.WriteLine($"Details: {ex.Message}");
            }
        }

        private static void SearchInNbt(string inputPath, string searchQuery)
        {
            try
            {
                if (!File.Exists(inputPath))
                {
                    PrintError($"Input file not found: '{inputPath}'");
                    return;
                }

                PrintInfo($"Searching for '{searchQuery}' in '{inputPath}'...");
                var nbtFile = new NbtFile();
                nbtFile.LoadFromFile(inputPath);

                if (nbtFile.RootTag == null)
                {
                    PrintError("The NBT file does not contain a root tag.");
                    return;
                }

                int matchCount = 0;
                SearchTagRecursive(nbtFile.RootTag, searchQuery, ref matchCount);

                PrintSuccess($"Search complete. Found {matchCount} match(es) for '{searchQuery}'.");
            }
            catch (Exception ex)
            {
                PrintError("Failed to search inside NBT file.");
                Console.WriteLine($"Details: {ex.Message}");
            }
        }

        private static void SetValueInNbt(string inputPath, string tagName, string newValueStr)
        {
            try
            {
                if (!File.Exists(inputPath))
                {
                    PrintError($"Input file not found: '{inputPath}'");
                    return;
                }

                string backupPath = inputPath + ".bak";
                File.Copy(inputPath, backupPath, true);
                PrintInfo($"Backup created: '{backupPath}'");

                PrintInfo($"Updating tag '{tagName}' in '{inputPath}'...");
                var nbtFile = new NbtFile();
                nbtFile.LoadFromFile(inputPath);

                if (nbtFile.RootTag == null)
                {
                    PrintError("The NBT file does not contain a root tag.");
                    return;
                }

                bool updated = UpdateTagRecursive(nbtFile.RootTag, tagName, newValueStr);

                if (updated)
                {
                    nbtFile.SaveToFile(inputPath, NbtCompression.GZip);
                    PrintSuccess($"Tag '{tagName}' successfully updated and saved to '{inputPath}'!");
                }
                else
                {
                    PrintError($"Tag with name '{tagName}' was not found in the root compound.");
                }
            }
            catch (Exception ex)
            {
                PrintError("Failed to update NBT file.");
                Console.WriteLine($"Details: {ex.Message}");
            }
        }

        private static bool UpdateTagRecursive(NbtTag tag, string targetName, string newValueStr)
        {
            if (tag == null) return false;

            if (tag.TagType == NbtTagType.Compound)
            {
                var compound = (NbtCompound)tag;
                foreach (var child in compound)
                {
                    if (child.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                    {
                        UpdateTagValue(child, newValueStr);
                        return true;
                    }

                    if (child.TagType == NbtTagType.Compound || child.TagType == NbtTagType.List)
                    {
                        if (UpdateTagRecursive(child, targetName, newValueStr)) return true;
                    }
                }
            }
            else if (tag.TagType == NbtTagType.List)
            {
                var list = (NbtList)tag;
                foreach (var child in list)
                {
                    if (child.TagType == NbtTagType.Compound || child.TagType == NbtTagType.List)
                    {
                        if (UpdateTagRecursive(child, targetName, newValueStr)) return true;
                    }
                }
            }

            return false;
        }

        private static void UpdateTagValue(NbtTag tag, string newValueStr)
        {
            switch (tag.TagType)
            {
                case NbtTagType.Byte:
                    if (byte.TryParse(newValueStr, out byte bVal)) ((NbtByte)tag).Value = bVal;
                    break;
                case NbtTagType.Short:
                    if (short.TryParse(newValueStr, out short sVal)) ((NbtShort)tag).Value = sVal;
                    break;
                case NbtTagType.Int:
                    if (int.TryParse(newValueStr, out int iVal)) ((NbtInt)tag).Value = iVal;
                    break;
                case NbtTagType.Long:
                    if (long.TryParse(newValueStr, out long lVal)) ((NbtLong)tag).Value = lVal;
                    break;
                case NbtTagType.Float:
                    if (float.TryParse(newValueStr, out float fVal)) ((NbtFloat)tag).Value = fVal;
                    break;
                case NbtTagType.Double:
                    if (double.TryParse(newValueStr, out double dVal)) ((NbtDouble)tag).Value = dVal;
                    break;
                case NbtTagType.String:
                    ((NbtString)tag).Value = newValueStr;
                    break;
                default:
                    throw new NotSupportedException($"Updating tag type {tag.TagType} is not supported directly via --set.");
            }
        }

        private static void SearchTagRecursive(NbtTag tag, string query, ref int matchCount)
        {
            if (tag == null) return;

            bool nameMatches = !string.IsNullOrEmpty(tag.Name) && tag.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
            bool valueMatches = false;
            string tagValueStr = string.Empty;

            switch (tag.TagType)
            {
                case NbtTagType.Compound:
                    var compound = (NbtCompound)tag;
                    foreach (var child in compound)
                    {
                        SearchTagRecursive(child, query, ref matchCount);
                    }
                    break;

                case NbtTagType.List:
                    var list = (NbtList)tag;
                    foreach (var child in list)
                    {
                        SearchTagRecursive(child, query, ref matchCount);
                    }
                    break;

                case NbtTagType.Byte:
                    tagValueStr = ((NbtByte)tag).Value.ToString();
                    break;
                case NbtTagType.Short:
                    tagValueStr = ((NbtShort)tag).Value.ToString();
                    break;
                case NbtTagType.Int:
                    tagValueStr = ((NbtInt)tag).Value.ToString();
                    break;
                case NbtTagType.Long:
                    tagValueStr = ((NbtLong)tag).Value.ToString();
                    break;
                case NbtTagType.Float:
                    tagValueStr = ((NbtFloat)tag).Value.ToString();
                    break;
                case NbtTagType.Double:
                    tagValueStr = ((NbtDouble)tag).Value.ToString();
                    break;
                case NbtTagType.String:
                    tagValueStr = ((NbtString)tag).Value ?? string.Empty;
                    break;
            }

            if (!string.IsNullOrEmpty(tagValueStr))
            {
                valueMatches = tagValueStr.Contains(query, StringComparison.OrdinalIgnoreCase);
            }

            if (nameMatches || valueMatches)
            {
                matchCount++;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[MATCH #{matchCount}] Tag Name: '{tag.Name}' (Type: {tag.TagType}) | Value: '{tagValueStr}'");
                Console.ResetColor();
            }
        }

        private static object ConvertNbtTagToJsonElement(NbtTag tag)
        {
            if (tag == null) return null;

            switch (tag.TagType)
            {
                case NbtTagType.Compound:
                    var compound = (NbtCompound)tag;
                    var dict = new System.Collections.Generic.Dictionary<string, object>();
                    foreach (var childTag in compound)
                    {
                        dict[childTag.Name] = ConvertNbtTagToJsonElement(childTag);
                    }
                    return dict;

                case NbtTagType.List:
                    var list = (NbtList)tag;
                    var listObj = new System.Collections.Generic.List<object>();
                    foreach (var childTag in list)
                    {
                        listObj.Add(ConvertNbtTagToJsonElement(childTag));
                    }
                    return listObj;

                case NbtTagType.Byte:
                    return ((NbtByte)tag).Value;
                case NbtTagType.Short:
                    return ((NbtShort)tag).Value;
                case NbtTagType.Int:
                    return ((NbtInt)tag).Value;
                case NbtTagType.Long:
                    return ((NbtLong)tag).Value;
                case NbtTagType.Float:
                    return ((NbtFloat)tag).Value;
                case NbtTagType.Double:
                    return ((NbtDouble)tag).Value;
                case NbtTagType.String:
                    return ((NbtString)tag).Value;
                case NbtTagType.ByteArray:
                    return ((NbtByteArray)tag).Value;
                case NbtTagType.IntArray:
                    return ((NbtIntArray)tag).Value;
                case NbtTagType.LongArray:
                    return ((NbtLongArray)tag).Value;

                default:
                    return null;
            }
        }

        private static void ConvertJsonToNbt(string inputPath, string outputPath)
        {
            if (!File.Exists(inputPath))
            {
                PrintError($"Input file not found: '{inputPath}'");
                return;
            }

            try
            {
                if (File.Exists(outputPath))
                {
                    string backupPath = outputPath + ".bak";
                    File.Copy(outputPath, backupPath, true);
                    PrintInfo($"Existing output backed up to: '{backupPath}'");
                }

                PrintInfo($"Parsing '{inputPath}' and converting to NBT...");
                string jsonString = File.ReadAllText(inputPath);

                using (JsonDocument jsonDocument = JsonDocument.Parse(jsonString))
                {
                    JsonElement rootElement = jsonDocument.RootElement;

                    if (rootElement.ValueKind != JsonValueKind.Object)
                    {
                        throw new InvalidDataException("Root element of JSON must be an Object.");
                    }

                    NbtCompound rootCompound = ConvertJsonToNbtCompound(rootElement, string.Empty);
                    var nbtFile = new NbtFile(rootCompound);
                    nbtFile.SaveToFile(outputPath, NbtCompression.GZip);

                    PrintSuccess($"NBT file created successfully: '{outputPath}'");
                }
            }
            catch (JsonException jsonEx)
            {
                PrintError("Invalid JSON structure!");
                Console.WriteLine($"Message    : {jsonEx.Message}");
                Console.WriteLine($"Location   : Line {jsonEx.LineNumber}, Column {jsonEx.BytePositionInLine}");
            }
            catch (Exception ex)
            {
                PrintError("Unable to convert JSON to NBT.");
                Console.WriteLine($"Details: {ex.Message}");
            }
        }

        private static NbtCompound ConvertJsonToNbtCompound(JsonElement element, string tagName)
        {
            var compound = new NbtCompound(tagName);

            foreach (JsonProperty property in element.EnumerateObject())
            {
                NbtTag tag = ConvertJsonElementToNbtTag(property.Name, property.Value);
                if (tag != null)
                {
                    compound.Add(tag);
                }
            }

            return compound;
        }

        private static NbtTag ConvertJsonElementToNbtTag(string tagName, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    return ConvertJsonToNbtCompound(element, tagName);

                case JsonValueKind.Array:
                    if (tagName.Equals("UUID", StringComparison.OrdinalIgnoreCase))
                    {
                        var intList = new System.Collections.Generic.List<int>();
                        foreach (JsonElement item in element.EnumerateArray())
                        {
                            if (item.TryGetInt32(out int val)) intList.Add(val);
                            else if (item.TryGetInt64(out long lval)) intList.Add((int)lval);
                        }
                        return new NbtIntArray(tagName, intList.ToArray());
                    }

                    if (tagName.Equals("Pos", StringComparison.OrdinalIgnoreCase) ||
                        tagName.Equals("Motion", StringComparison.OrdinalIgnoreCase))
                    {
                        var doubleListTag = new NbtList(tagName, NbtTagType.Double);
                        foreach (JsonElement item in element.EnumerateArray())
                        {
                            double dVal = item.GetDouble();
                            doubleListTag.Add(new NbtDouble(dVal));
                        }
                        return doubleListTag;
                    }

                    if (tagName.Equals("Rotation", StringComparison.OrdinalIgnoreCase))
                    {
                        var floatListTag = new NbtList(tagName, NbtTagType.Float);
                        foreach (JsonElement item in element.EnumerateArray())
                        {
                            float fVal = (float)item.GetDouble();
                            floatListTag.Add(new NbtFloat(fVal));
                        }
                        return floatListTag;
                    }

                    var listTag = new NbtList(tagName);
                    foreach (JsonElement arrayItem in element.EnumerateArray())
                    {
                        NbtTag itemTag = ConvertJsonElementToNbtTag(string.Empty, arrayItem);
                        if (itemTag != null)
                        {
                            itemTag.Name = string.Empty;
                            try
                            {
                                listTag.Add(itemTag);
                            }
                            catch
                            {
                                listTag.Add(new NbtString(arrayItem.ToString()));
                            }
                        }
                    }
                    return listTag;

                case JsonValueKind.String:
                    string stringValue = element.GetString() ?? string.Empty;
                    return string.IsNullOrEmpty(tagName) ? new NbtString(stringValue) : new NbtString(tagName, stringValue);

                case JsonValueKind.Number:
                    if (element.TryGetInt32(out int intValue))
                    {
                        return string.IsNullOrEmpty(tagName) ? new NbtInt(intValue) : new NbtInt(tagName, intValue);
                    }
                    if (element.TryGetInt64(out long longValue))
                    {
                        return string.IsNullOrEmpty(tagName) ? new NbtLong(longValue) : new NbtLong(tagName, longValue);
                    }
                    if (element.TryGetDouble(out double doubleValue))
                    {
                        return string.IsNullOrEmpty(tagName) ? new NbtDouble(doubleValue) : new NbtDouble(tagName, doubleValue);
                    }
                    throw new InvalidDataException($"Unsupported numeric value for key '{tagName}'.");

                case JsonValueKind.True:
                case JsonValueKind.False:
                    byte bVal = (byte)(element.GetBoolean() ? 1 : 0);
                    return string.IsNullOrEmpty(tagName) ? new NbtByte(bVal) : new NbtByte(tagName, bVal);

                default:
                    return null;
            }
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

        private static void ShowHelp()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Usage:");
            Console.WriteLine("  NBTEditor <file_name>.dat_or_nbt -o <file_name>.json");
            Console.WriteLine("  NBTEditor -jsontonbt <file_name>.json -o <file_name>.dat_or_nbt");
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