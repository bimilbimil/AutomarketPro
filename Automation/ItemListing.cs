using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ECommons;
using AutomarketPro.Models;
using InventoryManager = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager;
using AgentInventoryContext = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentInventoryContext;

namespace AutomarketPro.Automation
{
    /// <summary>
    /// Handles item listing automation - opening context menus, clicking buttons, getting prices, etc.
    /// </summary>
    public class ItemListing
    {
        // AutomarketProPlugin is in AutomarketPro namespace (will be moved to Core later)
        private readonly AutomarketPro.AutomarketProPlugin Plugin;
        
        // Logging delegates - will be set by RetainerAutomation
        public Action<string>? Log { get; set; }
        public Action<string, Exception?>? LogError { get; set; }
        
        public ItemListing(AutomarketPro.AutomarketProPlugin plugin)
        {
            Plugin = plugin;
        }

        public async Task<bool> ListItemOnMarket(ScannedItem item, CancellationToken token)
        {
            try
            {
                if (!await OpenItemContextMenu(item, token))
                {
                    LogError?.Invoke($"[AutoMarket] Failed to open context menu for {item.ItemName}", null);
                    return false;
                }
                
                if (!await ClickPutUpForSale(item, token))
                {
                    LogError?.Invoke($"[AutoMarket] Failed to click 'Put Up for Sale' for {item.ItemName}", null);
                    return false;
                }
                
                uint lowestPrice = item.ListingPrice;
                await Task.Delay(60, token);
                
                // Skip price comparison if Data Center Scan is enabled (we already have the cached price)
                bool skipComparePrices = Plugin.Configuration.DataCenterScan;
                
                bool priceFound = false;
                if (!skipComparePrices)
                {
                    // Only compare prices if Data Center Scan is not enabled
                    for (int compareAttempt = 0; compareAttempt < 2; compareAttempt++)
                    {
                        if (compareAttempt > 0)
                        {
                            await Task.Delay(180, token);
                        }
                        
                        bool clickedCompare = false;
                        unsafe
                        {
                            if (ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Client.UI.AddonRetainerSell>("RetainerSell", out var retainerSellForCompare) 
                                && ECommons.GenericHelpers.IsAddonReady(&retainerSellForCompare->AtkUnitBase))
                            {
                                ECommons.Automation.Callback.Fire(&retainerSellForCompare->AtkUnitBase, true, 4);
                                clickedCompare = true;
                            }
                        }
                        
                        if (clickedCompare)
                        {
                            await Task.Delay(180, token);
                            var price = await GetLowestPriceFromComparePrices(item, token);
                            if (price > 0)
                            {
                                var undercutAmount = Plugin.Configuration.UndercutAmount;
                                lowestPrice = price > undercutAmount ? (uint)(price - undercutAmount) : 1;
                                priceFound = true;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    // Data Center Scan is enabled - use the cached price from EvaluateProfitability
                    priceFound = true; // Mark as found since we're using the pre-calculated price
                    Log?.Invoke($"[AutoMarket] Using cached data center price for {item.ItemName}: {lowestPrice} (skipping compare prices)");
                }
                
                await Task.Delay(60, token);
                
                nint retainerSellPtr = nint.Zero;
                bool retainerSellReady = false;
                
                for (int attempts = 0; attempts < 30; attempts++)
                {
                    await Task.Delay(60, token);
                    
                    unsafe
                    {
                        if (ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Client.UI.AddonRetainerSell>("RetainerSell", out var tempRetainerSell) 
                            && ECommons.GenericHelpers.IsAddonReady(&tempRetainerSell->AtkUnitBase))
                        {
                            retainerSellPtr = (nint)tempRetainerSell;
                            retainerSellReady = true;
                            Log?.Invoke($"[AutoMarket] RetainerSell addon is ready (attempt {attempts + 1})");
                            break;
                        }
                    }
                }
                
                if (!retainerSellReady)
                {
                    LogError?.Invoke("[AutoMarket] RetainerSell addon not found or not ready after waiting 3 seconds", null);
                    return false;
                }
                
                unsafe
                {
                    try
                    {
                        var retainerSell = (FFXIVClientStructs.FFXIV.Client.UI.AddonRetainerSell*)retainerSellPtr;
                        
                        if (ECommons.GenericHelpers.TryGetAddonByName<AtkUnitBase>("ItemSearchResult", out var itemSearchAddon))
                        {
                            itemSearchAddon->Close(true);
                        }
                        
                        var ui = &retainerSell->AtkUnitBase;
                        
                        if (lowestPrice > 0)
                        {
                            retainerSell->AskingPrice->SetValue((int)lowestPrice);
                            
                            if (item.Quantity > 1)
                            {
                                retainerSell->Quantity->SetValue(item.Quantity);
                            }
                            
                            ECommons.Automation.Callback.Fire(&retainerSell->AtkUnitBase, true, 0);
                            ui->Close(true);
                            return true;
                        }
                        else
                        {
                            LogError?.Invoke("[AutoMarket] No price to set", null);
                            ECommons.Automation.Callback.Fire(&retainerSell->AtkUnitBase, true, 1); // cancel
                            ui->Close(true);
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError?.Invoke($"[AutoMarket] Exception setting price and confirming: {ex.Message}", ex);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"[AutoMarket] Error listing item {item.ItemName}: {ex.Message}", ex);
                return false;
            }
        }
        
        public async Task<bool> OpenItemContextMenu(ScannedItem item, CancellationToken token)
        {
            try
            {
                Log?.Invoke($"[AutoMarket] Finding item {item.ItemName} (ID: {item.ItemId}) in inventory...");
                
                // Find the item in inventory
                unsafe
                {
                    var inventoryManager = InventoryManager.Instance();
                    if (inventoryManager == null)
                    {
                        LogError?.Invoke("[AutoMarket] InventoryManager is null", null);
                        return false;
                    }
                    
                    InventoryType[] inventoryTypes = {
                        InventoryType.Inventory1,
                        InventoryType.Inventory2,
                        InventoryType.Inventory3,
                        InventoryType.Inventory4
                    };
                    
                    InventoryType foundType = InventoryType.Inventory1;
                    int foundSlot = -1;
                    
                    foreach (var type in inventoryTypes)
                    {
                        var container = inventoryManager->GetInventoryContainer(type);
                        if (container == null) continue;
                        
                        for (int slot = 0; slot < container->Size; slot++)
                        {
                            var slotItem = container->GetInventorySlot(slot);
                            if (slotItem != null && slotItem->ItemId == item.ItemId)
                            {
                                foundType = type;
                                foundSlot = slot;
                                Log?.Invoke($"[AutoMarket] Found item at {type} slot {slot}");
                                break;
                            }
                        }
                        
                        if (foundSlot >= 0) break;
                    }
                    
                    if (foundSlot < 0)
                    {
                        LogError?.Invoke($"[AutoMarket] Item {item.ItemName} not found in inventory", null);
                        return false;
                    }
                    
                    // Open context menu using AgentInventoryContext
                    var agent = AgentInventoryContext.Instance();
                    if (agent == null)
                    {
                        LogError?.Invoke("[AutoMarket] AgentInventoryContext is null", null);
                        return false;
                    }
                    
                    Log?.Invoke($"[AutoMarket] Opening context menu for item at {foundType} slot {foundSlot}");
                    agent->OpenForItemSlot(foundType, foundSlot, 0, 0);
                }
                
                await Task.Delay(120, token); // Wait for context menu to appear
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"[AutoMarket] Error opening context menu: {ex.Message}", null);
                return false;
            }
        }
        
        public async Task<bool> ClickPutUpForSale(ScannedItem item, CancellationToken token)
        {
            try
            {
                // Wait for context menu to appear - use RaptureAtkUnitManager like SelectString
                ECommons.UIHelpers.AddonMasterImplementations.AddonMaster.ContextMenu contextMenu = null;
                nint contextMenuPtr = nint.Zero;
                for (int attempts = 0; attempts < 30; attempts++)
                {
                    await Task.Delay(60, token);
                    try
                    {
                        unsafe
                        {
                            if (ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Client.UI.AddonContextMenu>("ContextMenu", out var contextMenuAddon))
                            {
                                if (ECommons.GenericHelpers.IsAddonReady(&contextMenuAddon->AtkUnitBase))
                                {
                                    contextMenuPtr = (nint)contextMenuAddon;
                                }
                            }
                        }
                        
                        if (contextMenuPtr != nint.Zero)
                        {
                            try
                            {
                                contextMenu = new ECommons.UIHelpers.AddonMasterImplementations.AddonMaster.ContextMenu(contextMenuPtr);
                                break;
                            }
                            catch (Exception ex)
                            {
                                LogError?.Invoke($"[AutoMarket] Error creating ContextMenu wrapper: {ex.Message}", null);
                            }
                        }
                    }
                    catch { }
                }
                
                if (contextMenu == null || contextMenuPtr == nint.Zero)
                {
                    LogError?.Invoke("[AutoMarket] Context menu not found", null);
                    return false;
                }
                
                // Get "Put Up for Sale" text from Addon sheet row 99
                string putUpForSaleText = "Put Up for Sale";
                try
                {
                    if (Plugin?.DataManager != null)
                    {
                        var addonSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Addon>();
                        var row99Text = addonSheet?.GetRow(99).Text.ToString();
                        if (!string.IsNullOrEmpty(row99Text))
                        {
                            putUpForSaleText = row99Text;
                        }
                    }
                }
                catch { }
                
                // Access entries with defensive checks
                ECommons.UIHelpers.AddonMasterImplementations.AddonMaster.ContextMenu.Entry[] entries = null;
                try
                {
                    entries = contextMenu.Entries;
                    if (entries == null)
                    {
                        LogError?.Invoke("[AutoMarket] Context menu has no entries", null);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    LogError?.Invoke($"[AutoMarket] Error accessing context menu entries: {ex.Message}", null);
                    return false;
                }
                
                // Find "Put Up for Sale" entry
                int foundIndex = -1;
                for (int i = 0; i < entries.Length; i++)
                        {
                            try
                            {
                        var entry = entries[i];
                        if (!entry.Enabled) continue;
                        
                        var entryText = entry.Text;
                        if (entryText != null && 
                            (entryText.Equals(putUpForSaleText, StringComparison.OrdinalIgnoreCase) ||
                             entryText.Contains("Put Up for Sale", StringComparison.OrdinalIgnoreCase) ||
                             entryText.Contains("Sell items", StringComparison.OrdinalIgnoreCase)))
                        {
                            foundIndex = entry.Index;
                            break;
                        }
                    }
                    catch { continue; }
                }
                
                if (foundIndex < 0)
                {
                    LogError?.Invoke("[AutoMarket] Could not find 'Put Up for Sale' option in context menu", null);
                    return false;
                }
                
                // Click the entry using FireCallback
                                unsafe
                                {
                    var atkUnitBase = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)contextMenuPtr;
                    if (atkUnitBase == null || !ECommons.GenericHelpers.IsAddonReady(atkUnitBase))
                    {
                        LogError?.Invoke("[AutoMarket] ContextMenu addon is not ready", null);
                        return false;
                    }
                    
                    // Based on ECommons ContextMenu.Entry.Select(): values [0, Index, 0]
                    var values = stackalloc FFXIVClientStructs.FFXIV.Component.GUI.AtkValue[3]
                    {
                        new() { Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int, Int = 0 },
                        new() { Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int, Int = foundIndex },
                        new() { Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int, Int = 0 }
                    };
                    atkUnitBase->FireCallback(3, values, true);
                }
                await Task.Delay(180, token);
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"[AutoMarket] Error clicking 'Put Up for Sale': {ex.Message}", null);
                return false;
            }
        }
        
        public async Task<bool> VendorItem(ScannedItem item, CancellationToken token)
        {
            try
            {
                Log?.Invoke($"[AutoMarket] Attempting to vendor item: {item.ItemName} (ID: {item.ItemId})");
                
                // Step 1: Find item in inventory and open context menu
                if (!await OpenItemContextMenu(item, token))
                {
                    LogError?.Invoke($"[AutoMarket] Failed to open context menu for {item.ItemName}", null);
                    return false;
                }
                
                // Step 2: Click "Have Retainer Sell Items" from context menu
                if (!await ClickHaveRetainerSellItems(item, token))
                {
                    LogError?.Invoke($"[AutoMarket] Failed to click 'Have Retainer Sell Items' for {item.ItemName}", null);
                    return false;
                }
                
                // Step 3: Wait for and confirm any confirmation dialog
                await Task.Delay(300, token); // Give time for confirmation dialog to appear
                
                // Check for confirmation dialog (SelectYesno)
                bool confirmationClicked = false;
                unsafe
                {
                    // Try to find and confirm SelectYesno dialog if present
                    var raptureMgr = FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkUnitManager.Instance();
                    if (raptureMgr != null)
                    {
                        var yesnoName = System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi("SelectYesno");
                        var yesnoBytes = (byte*)yesnoName.ToPointer();
                        
                        for (int i = 1; i < 20; i++)
                        {
                            var yesnoAddon = raptureMgr->GetAddonByName(yesnoBytes, i);
                            if (yesnoAddon != null && yesnoAddon->IsVisible && ECommons.GenericHelpers.IsAddonReady(yesnoAddon))
                            {
                                Log?.Invoke("[AutoMarket] Found confirmation dialog, clicking Yes...");
                                // Click Yes (button index 0)
                                ECommons.Automation.Callback.Fire(yesnoAddon, true, 0);
                                confirmationClicked = true;
                                break;
                            }
                        }
                        
                        System.Runtime.InteropServices.Marshal.FreeHGlobal(yesnoName);
                    }
                }
                
                if (confirmationClicked)
                {
                    await Task.Delay(180, token);
                }
                
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"[AutoMarket] Error vendoring item {item.ItemName}: {ex.Message}", ex);
                return false;
            }
        }
        
        public async Task<bool> ClickHaveRetainerSellItems(ScannedItem item, CancellationToken token)
        {
            try
            {
                // Wait for context menu to appear - use RaptureAtkUnitManager like SelectString
                ECommons.UIHelpers.AddonMasterImplementations.AddonMaster.ContextMenu contextMenu = null;
                nint contextMenuPtr = nint.Zero;
                for (int attempts = 0; attempts < 30; attempts++)
                {
                    await Task.Delay(60, token);
                    try
                    {
                        unsafe
                        {
                            if (ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Client.UI.AddonContextMenu>("ContextMenu", out var contextMenuAddon))
                            {
                                if (ECommons.GenericHelpers.IsAddonReady(&contextMenuAddon->AtkUnitBase))
                                {
                                    contextMenuPtr = (nint)contextMenuAddon;
                                }
                            }
                        }
                        
                        if (contextMenuPtr != nint.Zero)
                        {
                            try
                            {
                                contextMenu = new ECommons.UIHelpers.AddonMasterImplementations.AddonMaster.ContextMenu(contextMenuPtr);
                                break;
                            }
                            catch (Exception ex)
                            {
                                LogError?.Invoke($"[AutoMarket] Error creating ContextMenu wrapper: {ex.Message}", null);
                            }
                        }
                    }
                    catch { }
                }
                
                if (contextMenu == null || contextMenuPtr == nint.Zero)
                {
                    LogError?.Invoke("[AutoMarket] Context menu not found", null);
                    return false;
                }
                
                // Get "Have Retainer Sell Items" text from Addon sheet row 5480
                string retainerSellText = "Have Retainer Sell Items";
                try
                {
                    if (Plugin?.DataManager != null)
                    {
                        var addonSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Addon>();
                        var row5480Text = addonSheet?.GetRow(5480).Text.ToString();
                        if (!string.IsNullOrEmpty(row5480Text))
                        {
                            retainerSellText = row5480Text;
                        }
                    }
                }
                catch { }
                
                // Access entries with defensive checks
                ECommons.UIHelpers.AddonMasterImplementations.AddonMaster.ContextMenu.Entry[] entries = null;
                try
                {
                    entries = contextMenu.Entries;
                    if (entries == null)
                    {
                        LogError?.Invoke("[AutoMarket] Context menu has no entries", null);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    LogError?.Invoke($"[AutoMarket] Error accessing context menu entries: {ex.Message}", null);
                    return false;
                }
                
                // Find "Have Retainer Sell Items" entry
                int foundIndex = -1;
                for (int i = 0; i < entries.Length; i++)
                {
                    try
                    {
                        var entry = entries[i];
                        if (!entry.Enabled) continue;
                        
                        var entryText = entry.Text;
                        if (entryText != null && 
                            (entryText.Equals(retainerSellText, StringComparison.OrdinalIgnoreCase) ||
                             entryText.Contains("Have Retainer Sell Items", StringComparison.OrdinalIgnoreCase) ||
                             entryText.Contains("Sell Items", StringComparison.OrdinalIgnoreCase)))
                        {
                            foundIndex = entry.Index;
                            break;
                        }
                    }
                    catch { continue; }
                }
                
                if (foundIndex < 0)
                {
                    LogError?.Invoke("[AutoMarket] Could not find 'Have Retainer Sell Items' option in context menu", null);
                    return false;
                }
                
                // Click the entry using FireCallback
                unsafe
                {
                    var atkUnitBase = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)contextMenuPtr;
                    if (atkUnitBase == null || !ECommons.GenericHelpers.IsAddonReady(atkUnitBase))
                    {
                        LogError?.Invoke("[AutoMarket] ContextMenu addon is not ready", null);
                        return false;
                    }
                    
                    // Based on ECommons ContextMenu.Entry.Select(): values [0, Index, 0]
                    var values = stackalloc FFXIVClientStructs.FFXIV.Component.GUI.AtkValue[3]
                    {
                        new() { Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int, Int = 0 },
                        new() { Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int, Int = foundIndex },
                        new() { Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int, Int = 0 }
                    };
                    atkUnitBase->FireCallback(3, values, true);
                }
                await Task.Delay(180, token);
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"[AutoMarket] Error clicking 'Have Retainer Sell Items': {ex.Message}", null);
                return false;
            }
        }
        
        public async Task<uint> GetLowestPriceFromComparePrices(ScannedItem item, CancellationToken token)
        {
            Log?.Invoke("[AutoMarket] Waiting for ItemSearchResult window to appear...");
            nint itemSearchPtr = nint.Zero;
            
            for (int attempts = 0; attempts < 40; attempts++)
            {
                await Task.Delay(60, token);
                
                if (token.IsCancellationRequested) break;
                
                try
                {
                    unsafe
                    {
                        if (ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>("ItemSearchResult", out var itemSearchAddon))
                        {
                            if (ECommons.GenericHelpers.IsAddonReady(itemSearchAddon))
                            {
                                itemSearchPtr = (nint)itemSearchAddon;
                                Log?.Invoke("[AutoMarket] Found ItemSearchResult window");
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogError?.Invoke($"[AutoMarket] Error checking ItemSearchResult (attempt {attempts}): {ex.Message}", ex);
                    await Task.Delay(30, token);
                    continue;
                }
            }
            
            if (itemSearchPtr == nint.Zero)
            {
                LogError?.Invoke("[AutoMarket] ItemSearchResult window did not appear", null);
                return 0;
            }
            
            if (itemSearchPtr == nint.Zero)
            {
                LogError?.Invoke("[AutoMarket] ItemSearchResult window did not appear", null);
                return 0;
            }
            
            await Task.Delay(120, token);
            
            ECommons.UIHelpers.AddonMasterImplementations.AddonMaster.ItemSearchResult itemSearch = null;
            for (int attempts = 0; attempts < 40; attempts++)
            {
                await Task.Delay(60, token);
                
                if (token.IsCancellationRequested) break;
                
                try
                {
                    unsafe
                    {
                        var addon = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)itemSearchPtr;
                        if (addon == null)
                        {
                            continue;
                        }
                        
                        if (!ECommons.GenericHelpers.IsAddonReady(addon))
                        {
                            continue;
                        }
                    }
                    
                    try
                    {
                        itemSearch = new ECommons.UIHelpers.AddonMasterImplementations.AddonMaster.ItemSearchResult(itemSearchPtr);
                        
                        if (itemSearch?.Entries != null && itemSearch.Entries.Length > 0)
                        {
                            Log?.Invoke($"[AutoMarket] ItemSearchResult has {itemSearch.Entries.Length} entries");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError?.Invoke($"[AutoMarket] Error creating ItemSearchResult wrapper (attempt {attempts}): {ex.Message}", ex);
                        await Task.Delay(30, token);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    LogError?.Invoke($"[AutoMarket] Error checking ItemSearchResult (attempt {attempts}): {ex.Message}", ex);
                    await Task.Delay(30, token);
                    continue;
                }
            }
            
            if (itemSearch == null)
            {
                LogError?.Invoke("[AutoMarket] Could not create ItemSearchResult wrapper", null);
                return 0;
            }
            
            if (itemSearch.Entries == null || itemSearch.Entries.Length == 0)
            {
                LogError?.Invoke("[AutoMarket] ItemSearchResult window has no entries after waiting", null);
                return 0;
            }
            
            Log?.Invoke($"[AutoMarket] ItemSearchResult window has {itemSearch.Entries.Length} entries, parsing prices...");
            
            // Find lowest price (considering HQ/NQ)
            uint lowestPrice = uint.MaxValue;
            int parsedCount = 0;
            
            // Parse entries with defensive checks
            var entries = itemSearch.Entries;
            if (entries == null || entries.Length == 0)
            {
                LogError?.Invoke("[AutoMarket] ItemSearchResult.Entries is null or empty", null);
                return 0;
            }
            
            for (int i = 0; i < entries.Length; i++)
            {
                try
                {
                    var entry = entries[i];
                    
                    unsafe
                    {
                        AtkTextNode* priceTextNode = null;
                        try
                        {
                            priceTextNode = entry.PriceTextNode;
                        }
                        catch
                        {
                            continue;
                        }
                        
                        if (priceTextNode == null) continue;
                        
                        var nodeTextPtr = &priceTextNode->NodeText;
                        if (nodeTextPtr == null) continue;
                        
                        try
                        {
                            var seString = ECommons.GenericHelpers.ReadSeString(nodeTextPtr);
                            var priceText = seString.TextValue;
                            
                            if (!string.IsNullOrEmpty(priceText))
                            {
                                // Remove commas and parse price (e.g., "1,234,567" -> 1234567)
                                var cleanPrice = priceText.Replace(",", "").Replace(" ", "").Trim();
                                
                                // Try to extract just the number part (in case there's "gil" or other text)
                                var match = System.Text.RegularExpressions.Regex.Match(cleanPrice, @"(\d+)");
                                if (match.Success && uint.TryParse(match.Groups[1].Value, out uint price))
                                {
                                    // Check if this is HQ (entry.HQImageNode would be non-null if HQ)
                                    bool isHQ = false;
                                    try
                                    {
                                        unsafe
                                        {
                                            var hqNode = entry.HQImageNode;
                                            isHQ = hqNode != null;
                                        }
                                    }
                                    catch { }
                                    
                                    Log?.Invoke($"[AutoMarket]   Entry {i} price: {price:N0} gil ({(isHQ ? "HQ" : "NQ")})");
                                    
                                    if (price < lowestPrice)
                                    {
                                        lowestPrice = price;
                                    }
                                    parsedCount++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log?.Invoke($"[AutoMarket] Error parsing price from entry {i}: {ex.Message}");
                            continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"[AutoMarket] Error processing entry {i}: {ex.Message}");
                    continue;
                }
            }
            
            if (parsedCount == 0)
            {
                Log?.Invoke("[AutoMarket] Could not parse any prices from ItemSearchResult entries");
                return 0;
            }
            
            if (lowestPrice == uint.MaxValue)
            {
                Log?.Invoke("[AutoMarket] No valid prices found");
                return 0;
            }
            
            Log?.Invoke($"[AutoMarket] Found lowest price: {lowestPrice:N0} gil (parsed {parsedCount} entries)");
            return lowestPrice;
        }
        
        public async Task<bool> CloseRetainerWindow(bool weVendored, CancellationToken token)
        {
            try
            {
                nint sellAddonPtr = nint.Zero;
                nint selectAddonPtr = nint.Zero;
                
                unsafe
                {
                    // Try to close RetainerSell window if open
                    FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase* tempSellPtr = null;
                    if (ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>("RetainerSell", out var tempSellAddon))
                    {
                        tempSellPtr = tempSellAddon;
                        sellAddonPtr = (nint)tempSellPtr;
                    }
                    
                    // Try to close SelectString if still open
                    FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase* tempSelectPtr = null;
                    if (ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>("SelectString", out var tempSelectAddon))
                    {
                        tempSelectPtr = tempSelectAddon;
                        selectAddonPtr = (nint)tempSelectPtr;
                    }
                }
                
                // Close windows outside unsafe block
                if (sellAddonPtr != nint.Zero)
                {
                    var retainerSell = new ECommons.UIHelpers.AddonMasterImplementations.AddonMaster.RetainerSell(sellAddonPtr);
                    retainerSell.Cancel();
                    await Task.Delay(500, token);
                }
                
                if (selectAddonPtr != nint.Zero)
                {
                    unsafe
                    {
                        ((FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)selectAddonPtr)->Close(true);
                    }
                    await Task.Delay(500, token);
                }
                
                // Handle confirmation dialog that appears when leaving retainer after vendoring
                // Only check for dialog if we actually vendored items
                if (weVendored)
                {
                    await HandleRetainerLeaveConfirmation(token);
                }
                
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"[AutoMarket] Error closing retainer window: {ex.Message}", ex);
                return false;
            }
        }
        
        /// <summary>
        /// Closes the retainer UI windows to return to RetainerList (retainer selection).
        /// Closes RetainerSellList and SelectString, but keeps RetainerList open for next retainer selection.
        /// </summary>
        public async Task<bool> CloseRetainerList(bool weVendored, CancellationToken token)
        {
            try
            {
                // Close RetainerSellList (the inventory/market board view)
                bool closedRetainerSellList = false;
                unsafe
                {
                    if (ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>("RetainerSellList", out var retainerSellListAddon))
                    {
                        retainerSellListAddon->Close(true);
                        closedRetainerSellList = true;
                    }
                }
                
                if (closedRetainerSellList)
                {
                    await Task.Delay(500, token);
                }
                
                // Close SelectString if it's still open
                bool closedSelectString = false;
                unsafe
                {
                    if (ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>("SelectString", out var selectStringAddon))
                    {
                        selectStringAddon->Close(true);
                        closedSelectString = true;
                    }
                }
                
                if (closedSelectString)
                {
                    await Task.Delay(500, token);
                }
                
                // Handle confirmation dialog that appears when leaving retainer after vendoring
                // Only check for dialog if we actually vendored items
                if (weVendored)
                {
                    await HandleRetainerLeaveConfirmation(token);
                }
                
                // Verify RetainerList is open and ready (we need it to select the next retainer)
                bool retainerListReady = false;
                unsafe
                {
                    if (ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>("RetainerList", out var retainerListAddon)
                        && ECommons.GenericHelpers.IsAddonReady(retainerListAddon))
                    {
                        retainerListReady = true;
                    }
                }
                
                // Wait for RetainerList to appear if it's not ready yet
                if (!retainerListReady)
                {
                    for (int attempts = 0; attempts < 30; attempts++)
                    {
                        await Task.Delay(60, token);
                        
                        unsafe
                        {
                            if (ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>("RetainerList", out var retainerListAddon)
                                && ECommons.GenericHelpers.IsAddonReady(retainerListAddon))
                            {
                                retainerListReady = true;
                                break;
                            }
                        }
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"[AutoMarket] Error closing retainer windows: {ex.Message}", ex);
                return false;
            }
        }
        
        /// <summary>
        /// Handles the confirmation dialog that appears when leaving a retainer after vendoring items.
        /// The dialog says "Your retainer will be unable to process item buyback..." and requires clicking "Yes".
        /// </summary>
        private async Task<bool> HandleRetainerLeaveConfirmation(CancellationToken token)
        {
            try
            {
                // Wait a bit for the dialog to appear after closing the retainer window
                await Task.Delay(300, token);
                
                bool confirmationClicked = false;
                nint yesnoName = nint.Zero;
                
                try
                {
                    // Wait for the dialog to appear (up to 2 seconds)
                    for (int attempts = 0; attempts < 20; attempts++)
                    {
                        await Task.Delay(60, token);
                        
                        unsafe
                        {
                            var raptureMgr = FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkUnitManager.Instance();
                            if (raptureMgr != null)
                            {
                                if (yesnoName == nint.Zero)
                                {
                                    yesnoName = System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi("SelectYesno");
                                }
                                var yesnoBytes = (byte*)yesnoName.ToPointer();
                                
                                for (int i = 1; i < 20; i++)
                                {
                                    var yesnoAddon = raptureMgr->GetAddonByName(yesnoBytes, i);
                                    if (yesnoAddon != null && yesnoAddon->IsVisible && ECommons.GenericHelpers.IsAddonReady(yesnoAddon))
                                    {
                                        Log?.Invoke("[AutoMarket] Found retainer leave confirmation dialog, clicking Yes...");
                                        // Click Yes (button index 0)
                                        ECommons.Automation.Callback.Fire(yesnoAddon, true, 0);
                                        confirmationClicked = true;
                                        break;
                                    }
                                }
                            }
                        }
                        
                        if (confirmationClicked)
                        {
                            break;
                        }
                    }
                }
                finally
                {
                    if (yesnoName != nint.Zero)
                    {
                        System.Runtime.InteropServices.Marshal.FreeHGlobal(yesnoName);
                    }
                }
                
                if (confirmationClicked)
                {
                    await Task.Delay(300, token); // Wait for dialog to close
                    Log?.Invoke("[AutoMarket] Confirmed leaving retainer");
                }
                
                return confirmationClicked;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"[AutoMarket] Error handling retainer leave confirmation: {ex.Message}", ex);
                return false;
            }
        }
    }
}
