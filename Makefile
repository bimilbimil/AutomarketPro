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
# Usage: make package [RELEASE_TAG=v1.0.0]
# If RELEASE_TAG is not provided, uses existing DownloadLink URLs
package: $(BUILD_DLL) $(BUILD_JSON)
	@echo "📦 Creating release package..."
	@echo "🕒 Updating repo.json LastUpdate timestamp and AssemblyVersion..."
	@TIMESTAMP=$$(date +%s); \
	if command -v jq >/dev/null 2>&1; then \
		CURRENT_VERSION=$$(jq -r '.[0].AssemblyVersion' repo.json); \
		MAJOR=$$(echo $$CURRENT_VERSION | cut -d. -f1); \
		MINOR=$$(echo $$CURRENT_VERSION | cut -d. -f2); \
		PATCH=$$(echo $$CURRENT_VERSION | cut -d. -f3); \
		BUILD=$$(echo $$CURRENT_VERSION | cut -d. -f4); \
		NEW_BUILD=$$((BUILD + 1)); \
		NEW_VERSION="$$MAJOR.$$MINOR.$$PATCH.$$NEW_BUILD"; \
		REPO_URL=$$(jq -r '.[0].RepoUrl' repo.json); \
		if [ -n "$$RELEASE_TAG" ]; then \
			DOWNLOAD_URL="$$REPO_URL/releases/download/$$RELEASE_TAG/$(PROJECT_NAME).zip"; \
			jq --arg ts $$TIMESTAMP --arg ver $$NEW_VERSION --arg url $$DOWNLOAD_URL '.[0].LastUpdate = ($$ts | tonumber) | .[0].AssemblyVersion = $$ver | .[0].DownloadLinkInstall = $$url | .[0].DownloadLinkUpdate = $$url | .[0].DownloadLinkTesting = $$url' repo.json > repo.json.tmp && mv repo.json.tmp repo.json; \
			echo "✅ Updated LastUpdate to $$TIMESTAMP, AssemblyVersion to $$NEW_VERSION, and DownloadLinks to $$RELEASE_TAG"; \
		else \
			jq --arg ts $$TIMESTAMP --arg ver $$NEW_VERSION '.[0].LastUpdate = ($$ts | tonumber) | .[0].AssemblyVersion = $$ver' repo.json > repo.json.tmp && mv repo.json.tmp repo.json; \
			echo "✅ Updated LastUpdate to $$TIMESTAMP and AssemblyVersion to $$NEW_VERSION"; \
			echo "⚠️  Note: DownloadLinks unchanged. To update them, run: make package RELEASE_TAG=v1.0.1"; \
		fi; \
	else \
		sed -i.bak "s/\"LastUpdate\": [0-9]*/\"LastUpdate\": $$TIMESTAMP/" repo.json && rm -f repo.json.bak; \
		echo "✅ Timestamp updated (jq not available, AssemblyVersion not updated)"; \
	fi
	@mkdir -p dist
	@rm -f dist/$(PROJECT_NAME).zip
	@cd $(BUILD_DIR) && \
		([ -f $(DLL_NAME) ] && zip -q ../../dist/$(PROJECT_NAME).zip $(DLL_NAME) || (echo "❌ $(DLL_NAME) not found" && exit 1)) && \
		([ -f ECommons.dll ] && zip -q ../../dist/$(PROJECT_NAME).zip ECommons.dll || echo "⚠️  ECommons.dll not found - may cause loading issues") && \
		([ -f $(DLL_NAME:.dll=.deps.json) ] && zip -q ../../dist/$(PROJECT_NAME).zip $(DLL_NAME:.dll=.deps.json) || echo "⚠️  $(DLL_NAME:.dll=.deps.json) not found") && \
		cd ../.. && \
		([ -f $(JSON_NAME) ] && zip -q dist/$(PROJECT_NAME).zip $(JSON_NAME) || (echo "❌ $(JSON_NAME) not found" && exit 1)) && \
		([ -f $(PROJECT_NAME).yaml ] && zip -q dist/$(PROJECT_NAME).zip $(PROJECT_NAME).yaml || true)
	@if [ -f dist/$(PROJECT_NAME).zip ]; then \
		echo "✅ Package created: dist/$(PROJECT_NAME).zip"; \
		ls -lh dist/$(PROJECT_NAME).zip; \
		echo ""; \
		echo "Package contents:"; \
		unzip -l dist/$(PROJECT_NAME).zip | grep -E "\.(dll|json|yaml)$$" || echo "  (checking contents...)"; \
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
		([ -f $(DLL_NAME) ] && zip -q ../../dist/$(PROJECT_NAME)-dev.zip $(DLL_NAME) || (echo "❌ $(DLL_NAME) not found" && exit 1)) && \
		([ -f ECommons.dll ] && zip -q ../../dist/$(PROJECT_NAME)-dev.zip ECommons.dll || echo "⚠️  ECommons.dll not found - may cause loading issues") && \
		([ -f $(DLL_NAME:.dll=.deps.json) ] && zip -q ../../dist/$(PROJECT_NAME)-dev.zip $(DLL_NAME:.dll=.deps.json) || echo "⚠️  $(DLL_NAME:.dll=.deps.json) not found") && \
		cd ../.. && \
		([ -f $(JSON_NAME) ] && zip -q dist/$(PROJECT_NAME)-dev.zip $(JSON_NAME) || (echo "❌ $(JSON_NAME) not found" && exit 1)) && \
		([ -f $(PROJECT_NAME).yaml ] && zip -q dist/$(PROJECT_NAME)-dev.zip $(PROJECT_NAME).yaml || true)
	@if [ -f dist/$(PROJECT_NAME)-dev.zip ]; then \
		echo "✅ Dev package created: dist/$(PROJECT_NAME)-dev.zip"; \
		ls -lh dist/$(PROJECT_NAME)-dev.zip; \
		echo ""; \
		echo "Package contents:"; \
		unzip -l dist/$(PROJECT_NAME)-dev.zip | grep -E "\.(dll|json|yaml)$$" || echo "  (checking contents...)"; \
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

