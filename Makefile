# Makefile for AutomarketPro Plugin
# Usage: make build    - Build and deploy the plugin
#        make clean    - Clean build artifacts
#        make deploy   - Just copy files (after building)

# Project settings
PROJECT_NAME = AutomarketPro
CSPROJ = $(PROJECT_NAME).csproj
DLL_NAME = $(PROJECT_NAME).dll
JSON_NAME = $(PROJECT_NAME).json

# Build directories
BUILD_DIR = bin/Debug
BUILD_DLL = $(BUILD_DIR)/$(DLL_NAME)
BUILD_JSON = $(BUILD_DIR)/$(JSON_NAME)

# Source files
SOURCE_JSON = $(JSON_NAME)

# Plugin installation directory (XIV on Mac) CHANGE IF NEEDED
PLUGIN_DIR = ~/Library/Application\ Support/XIV\ on\ Mac/dalamud/Hooks/dev/plugins
PLUGIN_DLL = $(PLUGIN_DIR)/$(DLL_NAME)
PLUGIN_JSON = $(PLUGIN_DIR)/$(JSON_NAME)

# .NET build configuration
CONFIGURATION = Debug

.PHONY: all build clean deploy install help

# Default target
all: build

# Build and deploy the plugin
build: $(BUILD_DLL) deploy
	@echo "✅ Build and deployment complete!"
	@echo "   DLL: $(BUILD_DLL)"
	@echo "   Installed to: $(PLUGIN_DIR)"

# Build the DLL
$(BUILD_DLL): $(CSPROJ) AutomarketPro.cs
	@echo "🔨 Building $(PROJECT_NAME)..."
	dotnet build -c $(CONFIGURATION)
	@test -f $(BUILD_DLL) || (echo "❌ Build failed - DLL not found" && exit 1)

# Copy JSON to build output (if it doesn't exist)
$(BUILD_JSON): $(SOURCE_JSON)
	@cp $(SOURCE_JSON) $(BUILD_JSON)

# Deploy files to plugin directory (requires build first)
deploy: $(BUILD_DLL) $(PLUGIN_DIR)
	@echo "📦 Deploying plugin files..."
	@cp $(BUILD_DLL) $(PLUGIN_DLL)
	@cp $(SOURCE_JSON) $(PLUGIN_JSON)
	@if [ -f $(BUILD_DIR)/automarketlogo.webp ]; then \
		cp $(BUILD_DIR)/automarketlogo.webp $(PLUGIN_DIR)/automarketlogo.webp; \
		echo "   ✅ Copied automarketlogo.webp"; \
	fi
	@echo "   ✅ Copied $(DLL_NAME)"
	@echo "   ✅ Copied $(JSON_NAME)"

# Create plugin directory if it doesn't exist
$(PLUGIN_DIR):
	@echo "📁 Creating plugin directory..."
	@mkdir -p $(PLUGIN_DIR)

# Clean build artifacts
clean:
	@echo "🧹 Cleaning build artifacts..."
	dotnet clean
	@echo "✅ Clean complete"

# Rebuild from scratch
rebuild: clean build

# Just build without deploying
build-only:
	@echo "🔨 Building only (no deployment)..."
	dotnet build -c $(CONFIGURATION)

# Show plugin files info
info:
	@echo "Plugin Information:"
	@echo "  Project: $(PROJECT_NAME)"
	@echo "  Build DLL: $(BUILD_DLL)"
	@echo "  Plugin Directory: $(PLUGIN_DIR)"
	@echo ""
	@if [ -f $(BUILD_DLL) ]; then \
		ls -lh $(BUILD_DLL); \
	else \
		echo "  ❌ DLL not built yet. Run 'make build' first."; \
	fi
	@if [ -d $(PLUGIN_DIR) ]; then \
		echo ""; \
		echo "Installed files:"; \
		ls -lh $(PLUGIN_DIR)/$(PROJECT_NAME).* 2>/dev/null || echo "  No files installed"; \
	fi

# Help message
help:
	@echo "AutomarketPro Plugin Makefile"
	@echo ""
	@echo "Available targets:"
	@echo "  make build      - Build and deploy plugin (default)"
	@echo "  make build-only - Build without deploying"
	@echo "  make deploy     - Deploy files (after building)"
	@echo "  make clean      - Clean build artifacts"
	@echo "  make rebuild    - Clean and rebuild"
	@echo "  make info       - Show plugin file information"
	@echo "  make help       - Show this help message"
	@echo ""
	@echo "Plugin will be installed to:"
	@echo "  $(PLUGIN_DIR)"

