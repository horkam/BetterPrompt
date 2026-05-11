# BetterPrompt

A Windows desktop tool that helps your team write better Claude Code prompts. Point it at a codebase, and it transforms vague requests into precise, token-efficient prompts that reference actual file paths, class names, and method names — so Claude spends less time searching and more time coding.

**100% local and free.** No API keys. No usage costs. Powered by rule-based optimization and a locally-running Ollama model.

---

## Download

[![Download Installer](https://img.shields.io/github/v/release/horkam/BetterPrompt?style=for-the-badge&label=Download%20Installer&color=E8871E&logo=windows&logoColor=white)](https://github.com/horkam/BetterPrompt/releases/latest/download/BetterPrompt-Setup.exe)
&nbsp;
[![Latest Release](https://img.shields.io/github/v/release/horkam/BetterPrompt?style=flat-square&color=555)](https://github.com/horkam/BetterPrompt/releases/latest)
[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue.svg?style=flat-square)](LICENSE)
[![Buy Me a Coffee](https://img.shields.io/badge/Buy%20me%20a%20coffee-%E2%98%95-orange?style=flat-square)](https://github.com/sponsors/horkam)

**Requirements:** Windows 10 or later (64-bit) · .NET 8 is bundled — no separate install needed

> **First time?** Click the orange button above, run `BetterPrompt-Setup.exe`, and follow the wizard.  
> **Upgrading?** Just run the new installer over the top — no need to uninstall first.

To pull an optional local AI model after installing:
```
ollama pull llama3.2:3b
```

---

## Why BetterPrompt?

Vague prompts are expensive. When you write *"add an API for the workout stuff"*, Claude Code has to search your entire project to figure out what you mean — burning tokens and time. BetterPrompt turns that into:

```
Add a REST API endpoint for motion tracking.

Relevant locations found in codebase:
  - class `MotionController` in Controllers/MotionController.cs
  - `GetMotions()` in Services/MotionService.cs
  - `Motion` in Models/Motion.cs
```

Claude knows exactly where to look. Fewer tokens. Better results.

---

## Features

### Codebase Indexing
- Indexes up to 150 files (configurable) across your project
- Extracts class names, method signatures, interfaces, and properties from C#, TypeScript/JavaScript, and Python source files
- Respects `.gitignore` and skips noise directories (`bin`, `obj`, `node_modules`, `.git`, etc.)
- Builds a file tree so the app understands your project's structure

### Rule-Based Optimizer
Runs deterministic cleanup passes on every prompt before Ollama ever sees it:
- Strips meta-instructions: *"can you"*, *"please"*, *"look at the code and"*, *"go ahead and"*, *"search for"*, *"in the codebase"*, etc.
- Promotes vague openers (*"I'd like to"*, *"I want to"*, *"We need to"*) into direct action verbs
- Collapses redundant whitespace without disturbing injected file references
- Injects precise codebase references — file paths, class names, and method names ranked by relevance

### Keyword Expansion
Bridges the gap between what you say and what your codebase calls things:
- **Static synonym clusters** — 20 built-in domain groups cover common developer vocabulary:
  - `movements` → `motion, action, gesture, exercise, activity, rep, repetition, move`
  - `auth` → `authentication, login, signin, credentials, access, permission, authorization, identity`
  - `database` → `db, store, repository, repo, storage, persistence, table, collection`
  - `payment` → `billing, charge, invoice, transaction, order, purchase, checkout`
  - and more: API/routing, errors, users, data models, notifications, search, files, logging, tests, scheduling, analytics, products, roles
- **Stem stripping** — `"movements"` → `"movement"`, `"exercises"` → `"exercise"` before lookup
- **Ollama expansion** (optional) — for domain-specific terms outside the built-in groups, asks the local model for additional synonyms

### Ollama Integration (Optional)
When Ollama is running with a supported model, a second pass rewrites the rule-cleaned prose for clarity and specificity. The codebase reference block is split off before Ollama sees it and re-attached unconditionally afterward, so injected file paths are never lost.

Supported models (selectable in Settings):

| Model | Size | Notes |
|---|---|---|
| `llama3.2:3b` | ~2 GB | Recommended default — fast, well-rounded |
| `llama3.2:1b` | ~1.3 GB | Lightest option, fastest response |
| `llama3.1:8b` | ~5 GB | More capable, needs ~6 GB RAM |
| `qwen2.5-coder:3b` | ~2 GB | Code-focused, excellent for prompt rewriting |
| `qwen2.5-coder:7b` | ~5 GB | Best code quality, needs ~6 GB RAM |
| `phi3:mini` | ~2.3 GB | Microsoft's compact model |
| `phi3.5:mini` | ~2.2 GB | Newer Phi, slightly better quality |
| `mistral:7b` | ~4.1 GB | Strong general-purpose model |
| `codellama:7b` | ~3.8 GB | Meta's code-specialized model |
| `deepseek-coder:6.7b` | ~3.8 GB | Excellent for code tasks |

### Shared Learning Store
Every optimization is saved to `.betterPrompt/learning.json` in the root of your indexed codebase. When a new prompt closely resembles a past one (Jaccard similarity ≥ 65%, configurable), the prior result is returned instantly — no Ollama call needed.

Because the file lives inside the codebase, **committing it to source control means your whole team benefits from every optimization anyone has ever run.** The more you use it, the faster it gets.

### Stats Dashboard
After each optimization, a stats strip shows:
- Rules applied count
- Whether an Ollama pass ran
- Whether the result came from the learning cache (and the similarity score)
- Characters removed vs. original

### Embedded Terminal
The Prompt Organizer tab includes a fully interactive terminal panel powered by the Windows ConPTY API and rendered with [xterm.js](https://xtermjs.org) inside a WebView2 control.

- Automatically starts PowerShell in the indexed codebase directory when indexing completes
- Full PTY support — arrow keys, tab completion, colors, and interactive CLI tools like `claude` all work correctly
- Resizable via a drag splitter between the prompt area and the terminal
- **Send Prompt to Claude Code** — types `claude <optimized prompt>` directly into the terminal and presses Enter
- **Ask AI About Prompt** — pastes the optimized prompt text at the current terminal cursor without submitting, so you can review or prepend a command first
- **Restart** — kills and restarts the terminal session in the current codebase directory
- **Open in Window** — opens Windows Terminal (or PowerShell) in an external window for tasks that need more space

---

## Getting Started

### Install (recommended)

1. Click the **Download Installer** badge above
2. Run `BetterPrompt-Setup.exe` and follow the wizard — .NET 8 is bundled, nothing else to install
3. *(Optional)* Install [Ollama](https://ollama.com) and pull a model for AI-assisted rewriting:
   ```
   ollama pull llama3.2:3b
   ```

### Build from source

**Prerequisites:** Windows 10/11 · [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
git clone https://github.com/horkam/BetterPrompt.git
cd BetterPrompt
dotnet run --project BetterPrompt/BetterPrompt.csproj
```

Or open `BetterPrompt.slnx` in Visual Studio 2022+ and press F5.

### Releasing a new version

Every merge to `main` triggers an automatic release. The version component that bumps depends on the branch prefix:

| Branch prefix | Example | Version bump |
|---|---|---|
| `feature/*` | `feature/new-export` | Minor — `1.5.2 → 1.6.0` |
| `fix/*` | `fix/crash-on-load` | Patch — `1.6.0 → 1.6.1` |
| `breaking/*` | `breaking/settings-rewrite` | Major — `1.6.1 → 2.0.0` |

The workflow builds a self-contained installer and publishes it to the [Releases](https://github.com/horkam/BetterPrompt/releases) page within a few minutes. The version badges above update automatically once the release is live.

To cut a specific version manually, use the **Manual Release** workflow from the Actions tab.

---

## Usage

1. **Select your codebase** — click Browse and point to a project root, or type the path directly.
2. **Index** — click Index Codebase. Progress is shown in the status bar. Indexing a typical project takes a few seconds.
3. **Enter your prompt** — type a rough, natural-language request in the left panel.
4. **Optimize** — click Optimize Prompt. The right panel shows the result with the source label (Rules / Rules + Ollama / Cache).
5. **Copy** — click Copy Prompt and paste into Claude Code, or use one of the terminal buttons to send it directly.

### Terminal workflow

After indexing, the terminal at the bottom of the Prompt Organizer tab is ready to use:

1. Optimize your prompt as usual.
2. Click **Send Prompt to Claude Code** to run it immediately, or **Ask AI About Prompt** to paste the text for manual editing.
3. Interact with Claude Code in the terminal as normal — the PTY gives it a real TTY so interactive mode works.
4. If you need more terminal space, click **Open in Window** to pop out to a full Windows Terminal session.

### Tips

- You do not need to index a codebase to use BetterPrompt. Rule-based cleanup and Ollama rewriting work on any prompt without one.
- The prompt input is disabled until a codebase is indexed, but you can still optimize bare prompts by clicking Optimize without indexing first — the app will create a temporary context.
- Commit `.betterPrompt/learning.json` to your repo so teammates inherit your learning history.
- Add `.betterPrompt/learning.json` to `.gitignore` if you want optimizations to stay local.

---

## How the Pipeline Works

```
Raw prompt
    │
    ▼
[1] Check learning cache  ──── cache hit ──────────────────► return cached result
    │ miss
    ▼
[2] Expand keywords
    │  static synonym lookup + stem stripping + optional Ollama expansion
    ▼
[3] Rule-based optimizer
    │  strip meta-instructions → normalize action verb → collapse whitespace
    │  → inject codebase references using expanded keywords
    ▼
[4] Split prose / locations block
    ▼
[5] Ollama pass (if enabled)   ← prose only (locations block protected)
    │
    ▼
    Re-attach locations block unconditionally
    │
    ▼
[6] Save to learning store
    │
    ▼
Optimized prompt
```

---

## Configuration

Settings are saved to `%AppData%\BetterPrompt\settings.json` and editable from the Settings tab in the app.

| Setting | Default | Description |
|---|---|---|
| `OllamaUrl` | `http://localhost:11434` | Ollama API base URL |
| `OllamaModel` | `llama3.2:3b` | Model used for optimization and keyword expansion |
| `UseOllama` | `true` | Enable/disable the Ollama pass entirely |
| `SimilarityThreshold` | `0.65` | Jaccard score required for a learning cache hit (0–1) |
| `MaxFilesToIndex` | `150` | Maximum source files to index per codebase |
| `MaxSignatureLinesPerFile` | `40` | Maximum signature lines extracted per file |
| `ExcludedDirectories` | see below | Directories skipped during indexing |
| `CodeExtensions` | see below | File extensions treated as indexable source code |

**Default excluded directories:** `.git`, `.vs`, `bin`, `obj`, `node_modules`, `.next`, `dist`, `out`, `build`, `packages`, `.nuget`

**Default code extensions:** `.cs`, `.ts`, `.tsx`, `.js`, `.jsx`, `.py`, `.go`, `.java`, `.cpp`, `.h`, `.rs`, `.swift`, `.kt`

---

## Project Structure

```
BetterPrompt/
├── Assets/
│   └── app.ico                     # Application icon
├── Models/
│   ├── AppSettings.cs              # User-configurable settings
│   ├── CodebaseContext.cs          # Indexed codebase state (file tree + signatures)
│   ├── LearningEntry.cs            # A single stored optimization
│   └── OptimizationResult.cs      # Result returned by the pipeline
├── Services/
│   ├── CodebaseIndexer.cs          # Walks the codebase and extracts signatures
│   ├── CodebaseSearcher.cs         # Scores files/classes/methods against keywords
│   ├── ConPtyService.cs            # Windows ConPTY wrapper (pseudo-console + pipe I/O)
│   ├── KeywordExpander.cs          # Synonym expansion (static clusters + Ollama)
│   ├── LearningStore.cs            # Read/write .betterPrompt/learning.json
│   ├── OllamaOptimizer.cs          # HTTP client for the Ollama /api/chat endpoint
│   ├── PromptOptimizerService.cs   # Orchestrates the full optimization pipeline
│   ├── RuleBasedOptimizer.cs       # Deterministic cleanup rules + reference injection
│   ├── SettingsService.cs          # Load/save AppSettings from disk
│   └── SimilarityMatcher.cs        # Tokenization + Jaccard similarity
├── Themes/
│   └── Dark.xaml                   # Full dark theme resource dictionary
├── ViewModels/
│   └── MainViewModel.cs            # MVVM view model (CommunityToolkit.Mvvm)
├── Converters/                     # WPF value converters
├── MainWindow.xaml                 # Main window layout
└── BetterPrompt.csproj
```

---

## Tech Stack

- **.NET 8 / WPF** — Windows desktop UI
- **CommunityToolkit.Mvvm** — source-generated observable properties and relay commands
- **Windows ConPTY** — pseudo-console API for real PTY support in the embedded terminal
- **Microsoft WebView2 + xterm.js** — Chromium-based terminal renderer with full ANSI/VT support
- **Ollama** — local LLM inference, completely free and offline
- **System.Text.Json** — settings and learning store serialization
- No cloud APIs. No telemetry. No accounts.

---

## Contributing

Pull requests welcome. A few things to know:

- The learning store at `.betterPrompt/learning.json` is intentionally committed to project repos — don't add it to *BetterPrompt's* own `.gitignore`.
- Ollama availability is checked at startup and on every settings save. If the status stays red, verify the model name matches exactly what `ollama list` shows.
- The `SplitLocationsBlock` / re-attach pattern in `PromptOptimizerService` is intentional — small models reliably drop appended blocks when instructed to preserve them, so the split is structural rather than instruction-based.
