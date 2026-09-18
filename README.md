# mdtohtml

A small .NET 10 console tool that converts a directory of Markdown (`.md`) files into
HTML (`.html`) using the [`marked`](https://github.com/markedjs/marked) CLI, and
automatically rewrites internal `.md` hyperlinks to point to the generated `.html` files.

## Features

- Recursively converts every `.md` file under an input directory to `.html`, preserving the folder structure in the output directory.
- Rewrites internal links that end in `.md` to `.html` in the generated output, including HTML `href`/`src` attributes and Markdown-style `](...)` links.
- Preserves URL fragments/anchors and query strings (e.g. `guide.md#top` -> `guide.html#top`).
- Verifies the `marked` CLI is available before running, and prints the install command if it is missing.
- Defaults input and output to the current directory when not specified.

## Tech details

- **Language / Runtime:** C# on **.NET 10**
- **Project type:** Console application (top-level statements)
- **Conversion engine:** the external **`marked`** npm CLI, invoked via **Node.js**
  - To be robust against npm shim issues (e.g. install paths containing spaces), the tool
	 locates and runs marked's script directly with node: `node <marked.js> -i <in> -o <out>`.
	 It discovers `marked.js` via the marked shim location or `npm root -g`, and falls back to
	 the `marked` executable if needed.
- **Link rewriting:** performed in C# with a regular expression over the generated HTML.

## Requirements

To run the tool, the target machine needs:

- **Windows x64**
- **.NET 10 (x64) runtime** — the published build is framework-dependent
- **Node.js** on `PATH`
- **`marked`** installed globally:

```bash
npm install -g marked
```

## Usage

```bash
mdtohtml.exe -i <input-dir> -o <output-dir>
```

Options:

| Option | Alias | Description |
| --- | --- | --- |
| `-i` | `--input` | Input directory containing `.md` files (default: current directory) |
| `-o` | `--output` | Output directory for generated `.html` files (default: current directory) |
| `-h` | `--help` | Show usage help |

### Examples

Convert all Markdown in `docs` into `site`:

```bash
mdtohtml.exe -i .\docs -o .\site
```

Convert Markdown in the current directory in place:

```bash
mdtohtml.exe
```

## Building from source

```bash
# Build
dotnet build mdtohtml.csproj -c Release

# Run
dotnet run --project mdtohtml.csproj -c Release -- -i .\docs -o .\site
```

## Publishing

Framework-dependent (small; requires the .NET 10 x64 runtime on the target machine):

```bash
dotnet publish mdtohtml.csproj -c Release -r win-x64 --self-contained false -o publish
```

Self-contained single file (larger; no runtime needed on the target machine):

```bash
dotnet publish mdtohtml.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## License

MIT
