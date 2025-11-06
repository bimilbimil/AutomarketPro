# Contributing to AutomarketPro

Thank you for your interest in contributing to AutomarketPro! This guide will help you get started with development.

## Development Setup

### Prerequisites

- .NET 9.0 SDK
- Dalamud development environment set up
- Git

### Getting Started

1. Clone the repository:
   ```bash
   git clone https://github.com/bimilbimil/AutomarketPro.git
   cd AutomarketPro
   ```

2. Build the project:
   ```bash
   dotnet build
   ```

   Or use the Makefile:
   ```bash
   make build
   ```

3. For development installation, you can use:
   ```bash
   make deploy
   ```
   This will copy the built files to your Dalamud plugins directory.

## Building from Source

### Build Steps

1. Clone the repository:
   ```bash
   git clone https://github.com/bimilbimil/AutomarketPro.git
   cd AutomarketPro
   ```

2. Build the project:
   ```bash
   dotnet build
   ```

   Or use the Makefile:
   ```bash
   make build
   ```

3. Copy the built files (`AutomarketPro.dll`, `AutomarketPro.json`) to your Dalamud plugins directory:
   - **Windows (XIVLauncher)**: `%APPDATA%\XIVLauncher\addon\Hooks\dev\plugins`
   - **macOS (XIV on Mac)**: `~/Library/Application Support/XIV on Mac/dalamud/Hooks/dev/plugins`

## Creating Release Packages

### For Release

```bash
make package
```

This creates `dist/AutomarketPro.zip` containing:
- `AutomarketPro.dll` - Main plugin assembly
- `AutomarketPro.json` - Plugin manifest
- `AutomarketPro.yaml` - Alternative manifest (optional)
- `ECommons.dll` - Dependency (required)
- `AutomarketPro.deps.json` - Dependency manifest (required)

**Note:** The `make package` command automatically:
- Updates `LastUpdate` timestamp in `repo.json`
- Increments `AssemblyVersion` (build number)
- Optionally updates `DownloadLink` URLs if you specify `RELEASE_TAG`:

```bash
make package RELEASE_TAG=v1.0.1
```

### For Dev Installation

```bash
make package-dev
```

This creates `dist/AutomarketPro-dev.zip` with the same contents.

**Note:** The zip files are created in the `dist/` directory and can be uploaded to GitHub Releases or used for manual installation.

## Creating GitHub Releases

When creating a new release on GitHub:

1. Build and package:
   ```bash
   make package
   ```

2. Create a release tag (e.g., `v1.0.0.0` or `v1.0.1`)

3. Upload the `AutomarketPro.zip` file from `dist/` to the release

4. If using a new release tag, update `repo.json`:
   ```bash
   make package RELEASE_TAG=v1.0.1
   ```
   This will automatically update:
   - `AssemblyVersion` (incremented)
   - `LastUpdate` (current timestamp)
   - `DownloadLinkInstall`, `DownloadLinkUpdate`, and `DownloadLinkTesting` URLs

5. Commit and push the updated `repo.json` to the repository

**Note:** You can reuse the same release tag and just replace the zip file. Dalamud will detect updates based on the `AssemblyVersion` in `repo.json`.

## Development Workflow

### Available Makefile Targets

- `make build` - Build and deploy the plugin (default)
- `make build-only` - Build without deploying
- `make deploy` - Deploy files (after building)
- `make package` - Create release zip package
- `make package-dev` - Create dev zip package
- `make clean` - Clean build artifacts
- `make rebuild` - Clean and rebuild
- `make info` - Show plugin file information
- `make help` - Show help message

### Testing

Enable Debug Logs in the plugin Settings tab to see detailed logging information. This will help identify any issues with:
- Item scanning
- Market price fetching
- Retainer interactions
- UI automation

## Code Style

- Follow C# coding conventions
- Use meaningful variable and method names
- Add comments for complex logic
- Keep methods focused and single-purpose

## Submitting Changes

1. Create a feature branch from `main`
2. Make your changes
3. Test thoroughly
4. Submit a pull request with a clear description of changes

## Questions?

If you have questions about contributing, please open an issue on GitHub.

