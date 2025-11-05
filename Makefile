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

# Package plugin for release
package: $(BUILD_DLL) $(BUILD_JSON)
	@echo "📦 Creating release package..."
	@mkdir -p dist
	@rm -f dist/$(PROJECT_NAME).zip
	@cd $(BUILD_DIR) && \
		zip -q ../../dist/$(PROJECT_NAME).zip $(DLL_NAME) 2>/dev/null && \
		cd .. && \
		zip -q dist/$(PROJECT_NAME).zip $(JSON_NAME) 2>/dev/null && \
		([ -f $(PROJECT_NAME).yaml ] && zip -q dist/$(PROJECT_NAME).zip $(PROJECT_NAME).yaml 2>/dev/null || true) && \
		([ -f automarketlogo.webp ] && zip -q dist/$(PROJECT_NAME).zip automarketlogo.webp 2>/dev/null || true) && \
		([ -f automarketlogo.png ] && zip -q dist/$(PROJECT_NAME).zip automarketlogo.png 2>/dev/null || true)
	@if [ -f dist/$(PROJECT_NAME).zip ]; then \
		echo "✅ Package created: dist/$(PROJECT_NAME).zip"; \
		ls -lh dist/$(PROJECT_NAME).zip; \
		echo ""; \
		echo "Package contents:"; \
		unzip -l dist/$(PROJECT_NAME).zip | grep -E "\.(dll|json|yaml|webp|png)$$" || echo "  (checking contents...)"; \
	else \
		echo "❌ Package creation failed"; \
		exit 1; \
	fi

# Package plugin for dev installation
package-dev: $(BUILD_DLL) $(BUILD_JSON)
	@echo "📦 Creating dev package..."
	@mkdir -p dist
	@rm -f dist/$(PROJECT_NAME)-dev.zip
	@cd $(BUILD_DIR) && \
		zip -q ../../dist/$(PROJECT_NAME)-dev.zip $(DLL_NAME) 2>/dev/null && \
		cd .. && \
		zip -q dist/$(PROJECT_NAME)-dev.zip $(JSON_NAME) 2>/dev/null && \
		([ -f $(PROJECT_NAME).yaml ] && zip -q dist/$(PROJECT_NAME)-dev.zip $(PROJECT_NAME).yaml 2>/dev/null || true) && \
		([ -f automarketlogo.webp ] && zip -q dist/$(PROJECT_NAME)-dev.zip automarketlogo.webp 2>/dev/null || true) && \
		([ -f automarketlogo.png ] && zip -q dist/$(PROJECT_NAME)-dev.zip automarketlogo.png 2>/dev/null || true)
	@if [ -f dist/$(PROJECT_NAME)-dev.zip ]; then \
		echo "✅ Dev package created: dist/$(PROJECT_NAME)-dev.zip"; \
		ls -lh dist/$(PROJECT_NAME)-dev.zip; \
		echo ""; \
		echo "Package contents:"; \
		unzip -l dist/$(PROJECT_NAME)-dev.zip | grep -E "\.(dll|json|yaml|webp|png)$$" || echo "  (checking contents...)"; \
	else \
		echo "❌ Package creation failed"; \
		exit 1; \
	fi

# Help message
help:
	@echo "AutomarketPro Plugin Makefile"
	@echo ""
	@echo "Available targets:"
	@echo "  make build      - Build and deploy plugin (default)"
	@echo "  make build-only - Build without deploying"
	@echo "  make deploy     - Deploy files (after building)"
	@echo "  make package    - Create release zip package"
	@echo "  make package-dev - Create dev zip package"
	@echo "  make clean      - Clean build artifacts"
	@echo "  make rebuild    - Clean and rebuild"
	@echo "  make info       - Show plugin file information"
	@echo "  make help       - Show this help message"
	@echo ""
	@echo "Plugin will be installed to:"
	@echo "  $(PLUGIN_DIR)"

