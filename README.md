# MarkdownKit

English | [简体中文](README_CN.md)

MarkdownKit is a WPF Markdown rendering library: it takes Markdown text and produces a read-only `FlowDocument`. Parsing is handled by [Markdig](https://github.com/xoofx/markdig); layout, colors, and diagram drawing are all done locally, with no browser, Node, or Java dependency.

This library is the rendering core of [MdViewPro](https://gitee.com/tea4go/mdview), and can also be referenced directly by other WPF applications.

## Applications Built with This Project

[MdViewPro](https://gitee.com/tea4go/mdview) is a Markdown viewer and editor for Windows that combines editing, split view, and full-window preview, and can also view PDF, Office documents, and JSON / YAML. Its Markdown preview is rendered by MarkdownKit; editing, file management, PDF / Office preview, and cloud image hosting are implemented by the host application.

## Features

- Markdown parsing based on Markdig, with custom WPF layout; colors and font sizes follow the host theme.
- Syntax highlighting for code blocks, copy button, optional line numbers; long lines do not wrap and scroll horizontally when too wide.
- Tables, bidirectional footnote navigation, task lists, strikethrough, highlighter, and YAML front matter.
- LaTeX math: inline `$...$` and block `$$...$$`, parsed by WpfMath.
- Images fit the preview width (shrink only, never enlarge) and support per-device width tiers; attachments are resolved from the document directory or a `files` directory.
- Remote images support host-injected authentication and certificate validation; a vector broken-image placeholder is drawn on failure.
- Obsidian-style wiki links: `[[target]]`, `[[target|alias]]`, `[[target#heading]]`.
- Mermaid: `sequenceDiagram`, `flowchart` / `graph`, `pie`.
- PlantUML: sequence, component/deployment, state, activity, and class/ER diagrams; mind maps, WBS, JSON/YAML/INI/TOML/XML trees, and Gantt charts.
- Vector rendering for inline SVG and `.svg` image files; stays sharp at any zoom.
- Structured text (JSON, YAML, INI, TOML, XML) can be drawn as zoomable tree views with selectable text; JSON parsing accepts JSON5 single quotes, bare keys, and multiline strings.
- Rendering failures fall back to source text so one bad diagram or formula cannot break the whole preview.

## Requirements

| Item | Requirement |
| --- | --- |
| Runtime | .NET Framework 4.7.2 |
| UI framework | WPF (`PresentationCore` / `PresentationFramework` / `WindowsBase`) |
| Language version | C# 7.3 |
| Build tool | Visual Studio 2022 or later (use its bundled MSBuild) |
| Build-time dependencies | Markdig 0.37.0, WpfMath 2.1.0, Microsoft.NETFramework.ReferenceAssemblies 1.0.3 |

`Microsoft.NETFramework.ReferenceAssemblies` supplies the .NET Framework reference assemblies through NuGet, so no separate Developer Pack installation is needed.

## Building

Open `MarkdownKit.sln` in Visual Studio and build. For command-line builds, use the MSBuild bundled with Visual Studio from a Developer Command Prompt:

```bat
:: Restore NuGet packages
msbuild MarkdownKit.sln -t:Restore -p:Configuration=Release

:: Build
msbuild MarkdownKit.sln -p:Configuration=Release
```

The output is `bin\Release\MarkdownKit.dll`.

> Do not build this project with `dotnet build`. It uses a legacy-style csproj; `dotnet build` restores NuGet packages but does not turn PackageReference items into compiler references, so it reports missing Markdig / WpfMath types. Use Visual Studio's MSBuild instead.

## Quick Start

```csharp
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using MarkdownKit;

// 1. Configure the theme snapshot. A built-in light theme is used if this is never called.
MarkdownTheme.Configure(new MarkdownStyle
{
    Markdown = MarkdownPalette.Light(),          // or MarkdownPalette.Dark()
    PreviewFamily = new FontFamily("Microsoft YaHei"),
    PreviewFontWeight = FontWeights.Normal,
    EditorFamily = new FontFamily("Consolas"),
    EditorFontSize = 0,                          // 0 means scale with body text
    EditorFontWeight = FontWeights.Normal,
    DiagramFamily = new FontFamily("Microsoft YaHei"),
    DiagramFontSize = 16,
    NoteFold = 14,
    IsDark = false,
    DefaultFont = "Microsoft YaHei",
    Background = Brushes.White,
    Foreground = Brushes.Black,
    Muted = Brushes.Gray,
    Border = Brushes.LightGray,
    Copied = Brushes.SeaGreen
});

// 2. Inject host capabilities as needed (defaults: built-in Chinese strings, anonymous downloads, strict certificate validation).
MarkdownResources.Translate = key =>
{
    switch (key)
    {
        case "copyCode": return "Copy code";
        default: return null;                    // returning empty falls back to the built-in text
    }
};
MarkdownResources.ResolveImage = url =>
    new MarkdownImageRequest { Url = url, Authorization = null };
// Inject AcceptCertificate only for self-signed image servers; leave it null to use system certificate validation.

// 3. Render. baseDir is the current document directory (pass null for a new, unsaved document).
List<HeadingInfo> outline;
List<BlockAnchor> anchors;
FlowDocument document = MarkdownRenderer.Render(
    markdown,
    16,                                          // body font size
    @"C:\notes",                                 // base directory for relative image paths
    previewBox,                                  // preview control; images fit its width
    target => { /* handle internal links; see PreviewLinkTarget */ },
    out outline,
    out anchors);

previewBox.Document = document;

// 4. Structured tree view: find a zoomable / selectable view (null if none).
IZoomableView zoom = MarkdownZoom.Find(document);
ISelectableTextView selectable = zoom as ISelectableTextView;
```

## Public API

### Rendering Entry Points

`MarkdownRenderer` (static class)

| Member | Description |
| --- | --- |
| `Render(string markdown, double fontSize)` | Minimal render; returns an empty document or the rendered result. |
| `Render(string markdown, double fontSize, out List<HeadingInfo> outline)` | Renders and collects the heading outline. |
| `Render(string markdown, double fontSize, string baseDir, FrameworkElement imageHost, Action<PreviewLinkTarget> linkOpener, out List<HeadingInfo> outline, out List<BlockAnchor> anchors)` | Full render. `baseDir` resolves relative image paths; `imageHost` controls image fit width; `linkOpener` handles internal links; `anchors` collects source-line positions of top-level blocks. |
| `Outline(string markdown)` | Parses headings only, without layout. Useful for editor-mode outlines. |
| `RenderPlain(string text, double fontSize)` | Lays out plain text without Markdown parsing. Useful for streaming output. |
| `StrictLineBreak` (static field) | When true, a single newline inside a paragraph also breaks the line. Default false. |
| `SynchronousRemoteImages` (static field) | When true, downloads remote images synchronously before `Render` returns. For one-shot layout such as export; keep false for preview. |

`HeadingInfo`: `Level` (1–6), `Text` (plain text), `SourceLine` (0-based), `Target` (the rendered `Paragraph` in preview mode; null in editor mode), `Page` (reserved for host PDF bookmarks; default -1).

`BlockAnchor`: `SourceLine` (0-based) and the corresponding WPF `Block`, used to synchronize scrolling between editor and preview.

### Theming

- `MarkdownPalette`: the full color set. Built-in `Light()` / `Dark()`; custom palettes are supported.
- `MarkdownStyle`: theme input, including palette, body font, code font and size, diagram font and size, dark/light flag, and general brushes.
- `MarkdownTheme`: the static theme snapshot read by the renderer. Call `Configure(style)` when the theme or font size changes; `BoldTypeface(family)` picks a font that has real bold glyphs.

### Host Hooks

`MarkdownResources` (static class):

| Member | Description |
| --- | --- |
| `Translate` | Text translation function. Built-in keys: `copyCode`, `codeLineNumbers`, `footnoteJump`, `footnoteBack`. Returning empty falls back to the built-in Chinese text. |
| `ResolveImage` | Returns a `MarkdownImageRequest` for an image URL, used for URL rewriting and `Authorization` on private image hosts. Return null for anonymous, unchanged downloads. |
| `AcceptCertificate` | Certificate validation callback for remote image downloads. Null uses system validation. |

### Other Public Types

| Type | Purpose |
| --- | --- |
| `MarkdownAssets` | Attachment directory convention: `ImageDir = "files"`. |
| `PreviewLinkTarget` | Internal link target: wiki name, relative path, anchor, footnote target, etc. |
| `WikiLinkParser` / `WikiLinkExtension` / `WikiLinkInline` | Markdig extension for wiki links. |
| `SyntaxHighlighter` | Reusable syntax highlighter: `Highlight(code, language)` returns `IReadOnlyList<CodeToken>`; `Resolve(language)` gets the lexer rules; `FamilyOf(language)` gets the syntax family. |
| `TokenKind` / `CodeToken` / `SyntaxFamily` | Token kind, token structure, and syntax family. |
| `MarkdownZoom` / `IZoomableView` / `ISelectableTextView` | Finds zoomable, text-selectable views in rendered output (currently tree views). |
| `MarkdownStructured` | Namespace `MarkdownKit.PlantUml`; `CanDrawTree(text, format)` determines whether structured text can be drawn as a tree. |

## Supported Markdown Syntax and Extensions

### Basic Syntax

Tables (pipe tables), YAML front matter (displayed as key/value pairs), footnotes, task lists, strikethrough `~~...~~`, and highlighter `==...==`.

The pipeline enables only these extensions and deliberately avoids Markdig's `UseAdvancedExtensions()`: it turns on letter/Roman-numeral ordered-list markers and would split dictionary-style definition lines such as "n. example" or "v. to do" into nested lists.

### Images

```markdown
![description](image.jpg)
![description](files/image.jpg)

<!-- Legacy form: cap at a percentage of the preview width -->
![description](image.jpg){width=50%}

<!-- New form: specify multiple tiers, selected by the preview's physical width -->
![description](image.jpg)<!--{width=50% phone_width=80% pad_width=30% pc_width=10%}-->
```

- Image lookup order: absolute paths are used as-is; relative paths are first resolved against `baseDir` (the current document directory), then `baseDir/files/`, and finally `files/` directories up to 8 levels above the parent directory. This lets documents in second- or third-level library subdirectories reference library-root attachments by filename alone.
- A new, unsaved document has no `baseDir`, so relative image paths cannot be resolved.
- Device tiers are chosen from the preview's content width: below 7 inches is phone, 7–14 inches is tablet, and 14 inches or more is a 2-in-1 PC. Missing tiers fall back to `width`.
- Every width is an upper bound: images narrower than the bound keep their original size and are never enlarged.
- Remote images get authentication through `MarkdownResources.ResolveImage`; a vector broken-image icon is shown on failure.

### Wiki Links

```markdown
[[target]]
[[target|display text]]
[[target#heading]]
```

Consistent with Obsidian's wiki-link syntax. Only `[[ ]]` is recognized; ordinary `[text](url)` links are unaffected. Click handling is delegated to the host's `linkOpener`.

### Code Blocks

- The fenced language identifier selects the highlighting rules, for example ` ```csharp ` or ` ```json5 `.
- Supported syntax families: C/C++/C#, Java, JavaScript/TypeScript, Go, Rust, PHP, Swift, Kotlin, Scala, Dart, Groovy, Objective-C, Python, Shell/Bash/batch, Ruby, Perl, YAML, INI, TOML, PowerShell, Dockerfile, Makefile, Pascal/Delphi, Visual Basic, CSS/SCSS/Less, R, Lua, the SQL family, JSON/JSONC/JSON5, and HTML/XML/XAML/SVG/Vue.
- Code blocks have a copy button; line numbers are collapsed by default and expand via the "line numbers" button; long lines do not wrap and scroll horizontally when too wide, with scrollbars shown on hover.
- A language of `mermaid` or `plantuml` / `puml` / `uml` attempts diagram rendering; if that fails, it falls back to a normal code block.

### Math

- Inline: `$E=mc^2$`
- Block: a standalone `$$...$$` paragraph

Unrecognized formulas show their source unchanged. Common aliases `\dfrac`, `\tfrac`, `\ `, `\quad`, and `\qquad` are rewritten to equivalent forms before parsing.

### Mermaid

| Diagram type | Description |
| --- | --- |
| `sequenceDiagram` | Sequence diagram; translated to the PlantUML sequence model and reusing the same layout and drawing code. |
| `flowchart` / `graph` | Layered flowchart. Supports rectangular, rounded, diamond, and circular nodes, solid/dashed edges, arrows, and edge labels. Does not support subgraphs, style classes, or more than three nodes on one line. |
| `pie` | Pie chart with sectors and a legend. |

Other Mermaid diagram types return null and fall back to source text.

### PlantUML

Use `plantuml` / `puml` / `uml` as the code-block language, or start the content with `@startuml`. Parsing, layout, and vector drawing are all done in this library, with no Java and no network access.

| Diagram type | Syntax |
| --- | --- |
| Sequence diagram | `@startuml ... @enduml` |
| Component/deployment diagram | Inside `@startuml`, use `[component]`, `component`, `package`, etc. |
| State diagram | Inside `@startuml`, use `state`, `[*]`, etc. |
| Activity diagram | Inside `@startuml`, use `start`, `stop`, `:action;`, `if ... then`, etc. |
| Class / ER diagram | Inside `@startuml`, use `class`, `interface`, `entity`, `<<PK>>`, etc. |
| Mind map | `@startmindmap` |
| WBS | `@startwbs` |
| JSON tree | `@startjson` |
| YAML tree | `@startyaml` |
| INI tree | `@startini` |
| TOML tree | `@starttoml` |
| XML tree | `@startxml` |
| Gantt chart | `@startgantt` |

The same `@startuml` source is parsed as several diagram types and scored; the best match wins. If none match, it returns null and falls back to a code block.

Tree views support zoom (in, out, 1:1) and drag-to-select text for copying. Hosts access this through `MarkdownZoom.Find` and `IZoomableView` / `ISelectableTextView`. Gantt charts are static renders and do not implement these interfaces.

### SVG

- Inline `<svg>...</svg>` inside raw HTML blocks is drawn as vector graphics and fits the preview width.
- `.svg` image files are also rendered as vectors.
- Supported elements: `svg`, `g`, `rect`, `circle`, `ellipse`, `line`, `polyline`, `polygon`, `path`, `text`, `tspan`.
- Supports style inheritance, `viewBox` scaling, and `transform`.
- Does not support scripts, external references, gradients, filters, clipping, or `use`.

### Structured Text Tree Views

`MarkdownStructured.CanDrawTree(text, format)` accepts format identifiers such as `json`, `yaml`/`yml`, `ini`/`conf`, `toml`, and `xml`. When it returns true, the host wraps the source in the corresponding `@start<kind>` code block and passes it to `MarkdownRenderer.Render` to get a tree view. Top-level scalars, empty objects, and similar content that cannot form a tree return false.

## Rendering Pipeline

Main flow of a single `Render`:

1. `Markdig.Markdown.Parse(markdown, Pipeline)` parses into a Markdig syntax tree.
2. `ImageWidthAttribute.Apply` extracts `{width=...}` / HTML-comment width markers after images and attaches them to the image nodes.
3. `AddBlocks` converts each block into WPF layout elements:
   - headings, paragraphs, lists, quotes, tables, footnotes;
   - code blocks go through syntax highlighting and attempt Mermaid / PlantUML;
   - math goes through WpfMath;
   - images use `MarkdownImage`, and SVG uses `SvgRenderer`.
4. `LinkFootnotes` connects in-text references with the footnote entries at the end for bidirectional navigation.
5. Returns the `FlowDocument` and fills `outline` and `anchors`.

The image directory, host control, link callback, and footnote registries are kept in static fields during rendering and cleared in `try/finally`. Therefore:

> `MarkdownRenderer.Render` must be called serially on the UI thread and must not be re-entered concurrently.

## Theming and Colors

`MarkdownPalette` colors are grouped by purpose:

- Body and headings: `Text`, `Heading`, `HeadingRule1`, `HeadingRule2`, `Link`, `MarkBackground`.
- Quotes: `QuoteText`, `QuoteBorder`, `QuoteBackground`.
- Code: `CodeBackground`, `CodeBorder`, and token colors (keyword, string, comment, number, function, type, tag, attribute, property, section, variable).
- Inline code: `InlineCodeBackground`, `InlineCodeText`.
- Tables: `TableBackground`, `TableBorder`, `TableHeaderBackground`, `TableEvenRowBackground`.
- Other: `SegmentFill`.

`MarkdownStyle` can also set a separate code font size (`EditorFontSize`); 0 means scale with body text. Diagram font size is fixed to `DiagramFontSize` so shape constants and layout proportions stay stable.

After changing the theme, call `Configure` and render again; existing `FlowDocument` instances do not recolor automatically.

## Project Layout

```
MarkdownKit/
├─ MarkdownKit.sln
├─ MarkdownKit.csproj
├─ MarkdownRenderer.cs         Rendering entry point and WPF layout for all block types (core file)
├─ MarkdownTheme.cs            Static theme snapshot read by the renderer
├─ MarkdownStyle.cs            Theme configuration supplied by the host
├─ MarkdownPalette.cs          Light/dark palettes and all color fields
├─ MarkdownResources.cs        Text, image authentication, and certificate validation hooks
├─ MarkdownAssets.cs           Attachment directory convention (files)
├─ MarkdownImage.cs            Image control: width cap and device-tier scaling
├─ ImageWidthAttribute.cs      Image width marker parsing
├─ WikiLink.cs                 Markdig extension for wiki links
├─ SyntaxHighlighter.cs        Syntax highlighting for code
├─ BrokenImageIcon.cs          Vector broken-image placeholder icon
├─ Formula/
│  ├─ FormulaRenderer.cs       LaTeX formula rendering entry point (WpfMath)
│  └─ FormulaElement.cs        Formula element layout and baseline calculation
├─ Mermaid/
│  ├─ MermaidRenderer.cs       Mermaid rendering entry point
│  ├─ MermaidSequenceParser.cs Sequence diagram parsing
│  ├─ MermaidFlowchart.cs      Flowchart parsing
│  ├─ MermaidFlowchartView.cs  Flowchart layout and drawing
│  ├─ MermaidPie.cs            Pie chart parsing
│  └─ MermaidPieView.cs        Pie chart drawing
├─ PlantUml/
│  ├─ PlantUmlRenderer.cs      PlantUML rendering entry point and diagram type detection
│  ├─ PlantUmlPreprocessor.cs  Preprocessing
│  ├─ Sequence*.cs             Sequence diagram model, parsing, and layout
│  ├─ PlantUmlSequenceView.cs  Sequence diagram view
│  ├─ Component*.cs            Component/deployment diagrams and class/ER diagrams
│  ├─ State*.cs                State diagrams
│  ├─ Activity*.cs             Activity diagrams
│  ├─ Gantt*.cs                Gantt charts
│  ├─ Tree*.cs                 Mind maps / WBS / JSON / YAML / INI / TOML / XML trees
│  ├─ ConfigTreeReaders.cs     Structured text readers
│  ├─ DiagramZoom.cs           Zoom and text selection interfaces
│  ├─ LegendBox.cs             Legend
│  └─ StructuredTreeCheck.cs   Structured-text tree eligibility check
└─ Svg/
   └─ SvgRenderer.cs           SVG to WPF DrawingImage
```

## Development Conventions

1. **Register new source files manually.** The project uses a legacy csproj with no wildcard includes; every new `.cs` file must also be added to `<Compile Include="..."/>` in `MarkdownKit.csproj`, or it will not be compiled.
2. **The language version is C# 7.3.** Do not use C# 8+ syntax (switch expressions, nullable reference types, `using` declarations, range operators, etc.).
3. **Fail gracefully, don't throw.** Diagrams, formulas, and SVG that cannot be recognized return null so the caller can fall back to a code block or raw text.
4. **Take colors from the theme.** New rendering elements should get colors from `MarkdownTheme` / `MarkdownPalette`; hard-coded colors break dark mode.
5. **Keep rendering serial on the UI thread.** The rendering context is stored in static fields and does not support concurrent calls; asynchronous image decoding and similar work must return to the UI thread before updating the document.
6. **Extend the Markdig pipeline carefully.** Enable only the extensions you need; avoid `UseAdvancedExtensions()` changing how ordinary body text is parsed.
7. **Use the fixed base font size for diagram dimensions.** Shape constants are measured against `DiagramFontSize`; when the font size changes, layout scales proportionally, so do not adjust constants for individual diagrams.

### Common Extension Points

| Need | Entry point |
| --- | --- |
| Add a highlighting language | Language alias `switch` and `Syntax` definitions in `SyntaxHighlighter` |
| Add a Mermaid diagram type | Add dispatch in `MermaidRenderer.TryCreate` and a parser/view |
| Add a PlantUML diagram type | `PlantUmlRenderer.TryCreate` plus the corresponding Parser / Layout / View |
| Change image sizing behavior | `MarkdownImage` and `ImageWidthAttribute` |
| Add Markdown syntax | The `Pipeline` at the top of `MarkdownRenderer` and the `AddBlocks` dispatch |
| Expose a new host capability | Add a static hook in `MarkdownResources` or a setting in `MarkdownStyle` |

## Known Limitations

- This library only renders a read-only `FlowDocument`; it does not include an editor, file management, or PDF export. Those are the host's responsibility.
- Raw HTML blocks are only scanned for inline `<svg>`; other HTML is not rendered.
- Mermaid / PlantUML support a common subset; complex syntax falls back to code blocks.
- Image width markers are only recognized immediately after an image, so braces in formulas or code are not affected.
- Remote image download, authentication, and certificate policies depend on host injection; the defaults are anonymous downloads with strict certificate validation.
- After theme or font-size changes, re-render; existing documents do not update their colors automatically.

## License

This project is released under the [MIT License](LICENSE). When copying, modifying, or distributing the code or binaries, retain the copyright and permission notices in `LICENSE`.
