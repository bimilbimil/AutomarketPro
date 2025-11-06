# AutomarketPro

Automates inventory scanning, marketboard listing, and vendor selling using Universalis data and retainer control.

## Features

- **Automated Inventory Scanning**: Scans your inventory for all sellable items
- **Market Price Lookup**: Uses Universalis API to fetch current market prices
- **Smart Listing**: Automatically lists profitable items on the Market Board
- **Vendor Automation**: Sells unprofitable items to vendors via retainers
- **Retainer Management**: Cycles through all available retainers automatically
- **Configurable Settings**: Customize undercut amounts, profit thresholds, and more
- **Ignore List**: Exclude specific items from processing
- **Debug Logging**: Comprehensive logging for troubleshooting

## Installation

### Method 1: Experimental Plugin (Recommended)

1. Open Dalamud Settings by typing `/xlsettings` in-game
2. Navigate to the **Experimental** tab
3. Look for **"Add Plugin Repository"** or **"Custom Plugin Repositories"** section
4. Click **"Add"** or **"+"** to add a new repository
5. Enter the repository JSON URL:
   ```
   https://raw.githubusercontent.com/bimilbimil/AutomarketPro/main/repo.json
   ```
6. Click **"Save"** or **"OK"**
7. The plugin should now appear in your experimental plugins list
8. Check the box to enable **AutomarketPro**
9. Reload Dalamud or restart the game

**Note:** If the plugin doesn't appear, you may need to manually add it using Method 2 or Method 3.

### Method 2: Manual Installation from Release

1. Download the latest release zip from [GitHub Releases](https://github.com/bimilbimil/AutomarketPro/releases)
2. Extract the zip file contents to your Dalamud plugins directory:
   - **Windows (XIVLauncher)**: `%APPDATA%\XIVLauncher\addon\Hooks\dev\plugins`
   - **macOS (XIV on Mac)**: `~/Library/Application Support/XIV on Mac/dalamud/Hooks/dev/plugins`
3. The zip contains:
   - `AutomarketPro.dll` - The plugin assembly
   - `AutomarketPro.json` - Plugin manifest
   - `AutomarketPro.yaml` - Alternative manifest (if needed)
   
**Note:** Dependencies like ECommons and ImGui.NET are provided by Dalamud and do not need to be included in the package.
4. Restart Dalamud or reload plugins

### Method 3: Build from Source

1. Build the plugin using the instructions in the "Building from Source" section below
2. Copy the built files (`AutomarketPro.dll`, `AutomarketPro.json`) to your Dalamud plugins directory:
   - **Windows (XIVLauncher)**: `%APPDATA%\XIVLauncher\addon\Hooks\dev\plugins`
   - **macOS (XIV on Mac)**: `~/Library/Application Support/XIV on Mac/dalamud/Hooks/dev/plugins`
3. Restart Dalamud or reload plugins

## Building from Source

### Prerequisites

- .NET 9.0 SDK
- Dalamud development environment set up

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

### Creating Release Packages

To create a zip package for distribution:

**For Release:**
```bash
make package
```
This creates `dist/AutomarketPro.zip` containing:
- `AutomarketPro.dll` - Main plugin assembly
- `AutomarketPro.json` - Plugin manifest
- `AutomarketPro.yaml` - Alternative manifest (optional)

**Note:** Dependencies (ECommons, ImGui.NET) are provided by Dalamud and should not be included in the package.

**For Dev Installation:**
```bash
make package-dev
```
This creates `dist/AutomarketPro-dev.zip` with the same contents.

**Note:** The zip files are created in the `dist/` directory and can be uploaded to GitHub Releases or used for manual installation.

**Important:** When creating a new release on GitHub:
1. Create a release tag (e.g., `v1.0.0.0`)
2. Upload the `AutomarketPro.zip` file from `dist/` to the release
3. Update `repo.json` with the new version number and release URL:
   - Update `AssemblyVersion` to match the new version
   - Update `DownloadLinkInstall`, `DownloadLinkUpdate`, and `DownloadLinkTesting` URLs to point to the new release zip
   - Commit and push the updated `repo.json` to the repository

## Usage

1. Open the plugin UI using `/automarket` command or through Dalamud's plugin installer
2. Configure your settings in the Settings tab:
   - Undercut amount
   - Minimum profit threshold
   - Action delays
   - Filter options (skip HQ items, collectables, gear)
3. Click "Scan Only" to scan your inventory and see market prices
4. Click "Start Full Cycle" to begin automated listing and vendoring (Make sure Retainer List is open!)
   1. Moving your mouse may cause some issues, let the automation finish before doing anything!

### Commands

- `/automarket` - Open the main UI
- `/automarket start` - Start full automation cycle
- `/automarket stop` - Stop automation
- `/automarket pause` - Pause/resume automation
- `/automarket summary` - Show last run summary
- `/automarket config` - Open settings tab

## Configuration

### Market Board Settings

- **Undercut Amount**: How much to undercut the lowest price (default: 1 gil)
- **Min Profit Threshold**: Minimum profit required to list on MB (default: 100 gil)
- **Auto-undercut**: Automatically undercut lowest price when listing

### Automation Settings

- **Action Delay**: Delay between automation actions (default: 500ms)
- **Retainer Delay**: Delay between switching retainers (default: 2000ms)
- **List Only Mode**: List all items on MB regardless of profitability
- **Vendor Only Mode**: Vendor all items regardless of profitability

### Filter Settings

- **Skip HQ Items**: Don't process high-quality items
- **Skip Collectables**: Don't process collectable items
- **Skip Gear**: Don't process gear items

## Troubleshooting

Enable Debug Logs in the Settings tab to see detailed logging information. This will help identify any issues with:
- Item scanning
- Market price fetching
- Retainer interactions
- UI automation

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Repository

GitHub: https://github.com/bimilbimil/AutomarketPro

## Disclaimer

This plugin automates interactions with the game. Use at your own risk and in accordance with Square Enix's Terms of Service.

