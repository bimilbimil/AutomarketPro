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

For development and building from source, see [CONTRIBUTING.md](CONTRIBUTING.md).

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

## Contributing

Interested in contributing? Check out [CONTRIBUTING.md](CONTRIBUTING.md) for development setup, build instructions, and contribution guidelines.

## Repository

GitHub: https://github.com/bimilbimil/AutomarketPro


