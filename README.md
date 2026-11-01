# NBTEditor

A fast, lightweight command-line tool written in **C#** for working with Minecraft's **NBT (Named Binary Tag)** files. Convert `.dat` / `.nbt` files to human-readable **JSON** (and back), search inside them, edit values, validate files, and inspect player or structure data, all from your terminal.

---

## Features

- **NBT → JSON**: convert any `.dat` / `.nbt` file to pretty-printed JSON
- **JSON → NBT**: convert JSON back into a GZip-compressed NBT file (`--jsontonbt`)
- **Batch export**: convert a whole folder tree to JSON while preserving the folder structure
- **Batch import**: convert every `.json` file in a folder back to `.dat`
- **Search**: find tags by name or value (case-insensitive, recursive)
- **Edit values**: change a tag's value directly from the command line, with type-checked input and an automatic backup of the original
- **File info**: size, root tag name/type and compression type
- **Structure summary**: dimensions and block count for structure `.nbt` files
- **Player summary**: health, food level, XP level and position from `playerdata` / `level.dat` style files
- **Validation**: check whether an NBT file is readable and structurally sound
- **Safe by default**: the original file is backed up once as `.bak` (never overwritten by later edits), and files are written to a temporary file first, so a failed write can't leave you with a half-written file
- **Script friendly**: exit code `0` on success and `1` on errors
- **Colored console output** (`[INFO]`, `[SUCCESS]`, `[ERROR]`)

---

## Download

Grab the latest ready-to-use build from the [**Releases**](https://github.com/aetherdll/NBTEditor/releases) page and extract the `.zip` file to any folder.

The zip contains:

| File | Purpose |
| --- | --- |
| `NBTEditor.exe` | The program itself |
| `NBTEditor.dll` | Main application library |
| `fNbt.dll` | NBT reading/writing library (required) |
| `NBTEditor.deps.json` | Dependency information |
| `NBTEditor.runtimeconfig.json` | .NET runtime configuration |
| `NBTEditor.runtimeconfig.dev.json` | Development runtime configuration (optional) |
| `NBTEditor.pdb` | Debug symbols (optional) |

> **Note:** `fNbt.dll` is already included in the zip, so there is nothing else to download or install except the .NET runtime below.
>
> **Important:** Keep all files together in the same folder. `NBTEditor.exe` will not work without `NBTEditor.dll`, `fNbt.dll` and the `.json` configuration files next to it.

### Requirements

- Windows
- [**.NET 10.0 Runtime**](https://dotnet.microsoft.com/download/dotnet/10.0) (required). This project is built with .NET 10.0, so older versions will not work. If you get a ".NET is required" message when starting the program, install the runtime from the link. Only the **Runtime** is needed, not the SDK.

### Quick start

NBTEditor is a command-line tool, so run it from a terminal rather than by double-clicking:

1. Extract the zip to a folder, for example `C:\NBTEditor`.
2. Open that folder, click the address bar, type `cmd` and press Enter. This opens a terminal in the folder.
3. Run a command:

```bat
NBTEditor.exe level.dat -o level.json
```

Tip: put the file you want to convert in the same folder, or give the full path, for example `NBTEditor.exe "C:\Users\You\saves\World\level.dat" -o level.json`. Use quotes if the path contains spaces.

Running `NBTEditor.exe` with no arguments prints the help text.

---

## Usage

```text
NBTEditor <file>.dat_or_nbt -o <file>.json
NBTEditor --jsontonbt <file>.json -o <file>.dat_or_nbt
NBTEditor <file>.dat_or_nbt --find "SearchQuery"
NBTEditor <file>.dat_or_nbt --set "TagName" "NewValue"
NBTEditor <file>.dat_or_nbt --info
NBTEditor <file>.nbt --structure
NBTEditor <file>.dat --player
NBTEditor <file>.dat --validate
NBTEditor --all <directory_path>
NBTEditor --export-all <directory_path>
```

Running the program without arguments prints this help text. In the examples below, `NBTEditor` means `NBTEditor.exe` on Windows.

### Commands

| Command | Description |
| --- | --- |
| `<file> -o <out.json>` | Convert an NBT file to JSON |
| `--jsontonbt <file.json> -o <out.dat>` | Convert JSON to a GZip-compressed NBT file (an existing output file is backed up as `.bak` first) |
| `<file> --find "<query>"` | Search tag names and values (case-insensitive) |
| `<file> --set "<tag>" "<value>"` | Update the first tag with the given name (a `.bak` backup of the original is created first) |
| `<file> --info` | Show file size, root tag name/type and compression |
| `<file> --structure` | Show size (X × Y × Z) and block count of a structure file |
| `<file> --player` | Show health, food level, XP level and position |
| `<file> --validate` | Check whether the file can be read correctly |
| `--all <dir>` | Convert all `.json` files in a directory to `.dat` (existing `.dat` files with the same name are overwritten) |
| `--export-all <dir>` | Convert all `.dat` / `.nbt` files in a directory (recursively) to JSON |

### Examples

**Convert `level.dat` to JSON**

```bash
NBTEditor level.dat -o level.json
```

**Convert the JSON back to NBT**

```bash
NBTEditor --jsontonbt level.json -o level.dat
```

**Search for a tag or value**

```bash
NBTEditor level.dat --find "LevelName"
```

```text
[MATCH #1] Tag Name: 'LevelName' (Type: String) | Value: 'My World'
```

**Change a value**

```bash
NBTEditor level.dat --set "LevelName" "My New World"
```

A backup (`level.dat.bak`) is created automatically the first time you edit a file. An existing `.bak` is never overwritten, so it always keeps your original file. The value must match the tag's type: for example, you can't put text into an Int tag, and in that case the file is left unchanged and an error is shown. Decimal numbers always use a dot (`1.5`), regardless of your system language.

**Show a player summary**

```bash
NBTEditor player.dat --player
```

```text
--- PLAYER SUMMARY ---
Health    : 20
Food Level: 20
XP Level  : 5
Position  : X: 120.50, Y: 64.00, Z: -35.25
----------------------
```

**Export an entire folder to JSON**

```bash
NBTEditor --export-all ./world
```

This creates `Output-world` next to the source folder, mirroring the original folder structure. Each file is saved with `.json` appended, for example `level.dat` → `level.dat.json`.

---

## How the conversion works

### NBT → JSON

| NBT type | JSON output |
| --- | --- |
| Compound | Object |
| List | Array |
| Byte / Short / Int / Long | Number |
| Float / Double | Number |
| String | String |
| Byte Array | Array of numbers (signed, -128 to 127) |
| Int Array / Long Array | Array of numbers |

### JSON → NBT

| JSON type | NBT output |
| --- | --- |
| Object | Compound |
| Array | List |
| String | String |
| Number | Int, or Long if it doesn't fit, or Double for decimals |
| `true` / `false` | Byte (`1` / `0`) |

Special handling for common Minecraft tags:

- `UUID` → Int Array
- `Pos`, `Motion` → List of Doubles
- `Rotation` → List of Floats
- Any other array that contains only numbers with at least one decimal value → List of Doubles

Tag names `Pos`, `Motion` and `Rotation` are case-sensitive.

`--jsontonbt` writes **GZip-compressed** files, which is the format Minecraft uses for `level.dat` and player data. JSON files up to 512 levels deep are supported.

---

## Limitations

- JSON has no equivalent for every NBT type, so a round trip (NBT → JSON → NBT) can change some tag types (for example Byte/Short/Float values come back as Int/Double, and Byte/Long arrays come back as Lists).
- `--set` supports Byte, Short, Int, Long, Float, Double and String tags only, and updates the **first** tag found with the given name (the search is case-insensitive).
- `--all` only processes `.json` files in the given directory (not subdirectories).
- Always keep a backup of your world before editing or re-importing data, and **close Minecraft** before modifying world files.

---

## Contributing

Contributions, issues and feature requests are welcome. Feel free to open an issue or submit a pull request.

1. Fork the project
2. Create your branch (`git checkout -b feature/my-feature`)
3. Commit your changes (`git commit -m "Add my feature"`)
4. Push the branch (`git push origin feature/my-feature`)
5. Open a pull request

---

## License

This project is licensed under the **GNU General Public License v3.0 (GPL-3.0)**. See the [LICENSE](LICENSE) file for details.

---

## Acknowledgements

- [fNbt](https://github.com/fragmer/fNbt): NBT reading/writing library
- Minecraft is a trademark of Mojang AB. This project is not affiliated with or endorsed by Mojang or Microsoft.s

## Acknowledgements

- [fNbt](https://github.com/fragmer/fNbt): NBT reading/writing library
- Minecraft is a trademark of Mojang AB. This project is not affiliated with or endorsed by Mojang or Microsoft.
