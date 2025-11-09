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
        
        // Callback to check retainer listing count (set by RetainerAutomation)
        public Func<int, int>? GetRetainerListingCount { get; set; }
        
        public ItemListing(AutomarketPro.AutomarketProPlugin plugin)
        {
            Plugin = plugin;
        }

        /// <summary>
        /// Safely gets InventoryManager with retry logic (up to 5 attempts).
        /// Returns null if all attempts fail.
        /// </summary>
        private unsafe InventoryManager* GetInventoryManagerSafe()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    var manager = InventoryManager.Instance();
                    if (manager != null)
                    {
                        return manager;
                    }
                }
                catch
                {
                    // Continue to next attempt
                }
                
                if (attempt < 4)
                {
                    System.Threading.Thread.Sleep(10); // Small delay between attempts
                }
            }
            return null;
        }

        /// <summary>
        /// Safely gets AgentInventoryContext with retry logic (up to 5 attempts).
        /// Returns null if all attempts fail.
        /// </summary>
        private unsafe AgentInventoryContext* GetAgentInventoryContextSafe()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    var agent = AgentInventoryContext.Instance();
                    if (agent != null)
                    {
                        return agent;
                    }
                }
                catch
                {
                    // Continue to next attempt
                }
                
                if (attempt < 4)
                {
                    System.Threading.Thread.Sleep(10); // Small delay between attempts
                }
            }
            return null;
        }

        /// <summary>
        /// Safely gets RaptureAtkUnitManager with retry logic (up to 5 attempts).
        /// Returns null if all attempts fail.
        /// </summary>
        private unsafe FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkUnitManager* GetRaptureAtkUnitManagerSafe()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    var manager = FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkUnitManager.Instance();
                    if (manager != null)
                    {
                        return manager;
                    }
                }
                catch
                {
                    // Continue to next attempt
                }
                
                if (attempt < 4)
                {
                    System.Threading.Thread.Sleep(10); // Small delay between attempts
                }
            }
            return null;
        }

        /// <summary>
        /// Safely gets an inventory container with re-validation. Re-validates InventoryManager before each attempt.
        /// Use this when the operation happens after a delay or in a loop where the pointer might become stale.
        /// </summary>
        private unsafe FFXIVClientStructs.FFXIV.Client.Game.InventoryContainer* GetInventoryContainerSafe(InventoryType inventoryType, int maxAttempts = 5)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    var inventoryManager = GetInventoryManagerSafe();
                    if (inventoryManager != null)
                    {
                        var container = inventoryManager->GetInventoryContainer(inventoryType);
                        if (container != null)
                        {
                            return container;
                        }
                    }
                }
                catch
                {
                    // Continue to next attempt
                }
                
                if (attempt < maxAttempts - 1)
                {
                    System.Threading.Thread.Sleep(10);
                }
            }
            return null;
        }

        /// <summary>
        /// Safely gets an inventory slot with re-validation. Re-validates InventoryManager and Container before each attempt.
        /// Use this when the operation happens after a delay or in a loop where the pointer might become stale.
        /// </summary>
        private unsafe FFXIVClientStructs.FFXIV.Client.Game.InventoryItem* GetInventorySlotSafe(InventoryType inventoryType, int slot, int maxAttempts = 5)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    var container = GetInventoryContainerSafe(inventoryType, 1); // Get container with single attempt (we're already retrying)
                    if (container != null && slot >= 0 && slot < container->Size)
                    {
                        var slotItem = container->GetInventorySlot(slot);
                        if (slotItem != null)
                        {
                            return slotItem;
                        }
                    }
                }
                catch
                {
                    // Continue to next attempt
                }
                
                if (attempt < maxAttempts - 1)
                {
                    System.Threading.Thread.Sleep(10);
                }
            }
            return null;
        }


        public async Task<bool> ListItemOnMarket(ScannedItem item, CancellationToken token, int? retainerIndex = null, int maxListings = 20)
        {
            try
            {
                uint lowestPrice = item.ListingPrice;
                bool skipComparePrices = Plugin.Configuration.DataCenterScan;
                
                // Handle items with quantity > 99 by listing in batches
                int startingQuantity = item.Quantity; // Track starting quantity for debugging
                int remainingQuantity = item.Quantity;
                int totalListed = 0; // Track total actually listed
                bool firstBatch = true;
                bool anyBatchSucceeded = false;
                
                // Track which inventory slot we're currently working with
                // Start with the slot from the scanned item, but may need to find new stacks as we deplete them
                InventoryType currentInventoryType = item.InventoryType;
                int currentInventorySlot = item.InventorySlot;
                
                while (remainingQuantity > 0 && !token.IsCancellationRequested)
                {
                    // Check retainer listing limit before each batch (if retainerIndex is provided)
                    if (retainerIndex.HasValue && GetRetainerListingCount != null)
                    {
                        int currentListings = GetRetainerListingCount(retainerIndex.Value);
                        if (currentListings >= maxListings)
                        {
                            Log?.Invoke($"[AutoMarket] Retainer {retainerIndex.Value} reached max listings ({currentListings}/{maxListings}). Cannot list more batches of {item.ItemName}. Remaining quantity: {remainingQuantity}");
                            // Update item quantity to reflect what's remaining
                            item.Quantity = remainingQuantity;
                            // Update inventory location in case we moved to a different stack
                            item.InventoryType = currentInventoryType;
                            item.InventorySlot = currentInventorySlot;
                            // Return true (partially successful) so item stays in queue for next retainer
                            return anyBatchSucceeded;
                        }
                    }
                    
                    // Check actual quantity in current slot before calculating batch quantity
                    // Use safe wrapper to re-validate pointer (may have changed since last check)
                    int actualSlotQuantity = 0;
                    unsafe
                    {
                        var slotItem = GetInventorySlotSafe(currentInventoryType, currentInventorySlot);
                        if (slotItem != null && slotItem->ItemId == item.ItemId)
                        {
                            actualSlotQuantity = slotItem->Quantity;
                        }
                    }
                    
                    // If current slot is empty, find the next stack
                    if (actualSlotQuantity <= 0)
                    {
                        Log?.Invoke($"[AutoMarket] Current slot ({currentInventoryType} slot {currentInventorySlot}) is empty. Finding next stack of {item.ItemName}...");
                        var (foundType, foundSlot) = FindNextStackOfItem(item, currentInventoryType, currentInventorySlot);
                        if (foundSlot >= 0)
                        {
                            currentInventoryType = foundType;
                            currentInventorySlot = foundSlot;
                            Log?.Invoke($"[AutoMarket] Found next stack at {currentInventoryType} slot {currentInventorySlot}");
                            
                            // Re-check quantity in the new slot - use safe wrapper to re-validate
                            unsafe
                            {
                                var slotItem = GetInventorySlotSafe(currentInventoryType, currentInventorySlot);
                                if (slotItem != null && slotItem->ItemId == item.ItemId)
                                {
                                    actualSlotQuantity = slotItem->Quantity;
                                }
                            }
                        }
                        else
                        {
                            LogError?.Invoke($"[AutoMarket] No more stacks found for {item.ItemName}. Expected {remainingQuantity} remaining but no stacks available.", null);
                            break; // Exit loop - no more stacks
                        }
                    }
                    
                    // Calculate quantity for this batch: min of (99 max per listing, remaining quantity needed, actual quantity in slot)
                    int batchQuantity = Math.Min(99, Math.Min(remainingQuantity, actualSlotQuantity));
                    
                    if (batchQuantity <= 0)
                    {
                        LogError?.Invoke($"[AutoMarket] Cannot list batch: batchQuantity={batchQuantity}, remainingQuantity={remainingQuantity}, actualSlotQuantity={actualSlotQuantity}", null);
                        break;
                    }
                    
                    if (!firstBatch)
                    {
                        Log?.Invoke($"[AutoMarket] Listing remaining {remainingQuantity} of {item.ItemName} (batch: {batchQuantity} from slot with {actualSlotQuantity})");
                    }
                    
                    // Add delay before opening context menu to ensure UI is stable
                    await Task.Delay(100, token);
                    
                    // Open context menu for the item
                    bool menuOpened = await OpenItemContextMenuForSlot(item, currentInventoryType, currentInventorySlot, token);
                    
                    if (!menuOpened)
                    {
                        LogError?.Invoke($"[AutoMarket] Failed to open context menu for {item.ItemName} at {currentInventoryType} slot {currentInventorySlot}", null);
                        break; // Exit loop if we can't open the menu
                    }
                    
                    // Add delay after opening context menu to ensure it's fully ready
                    await Task.Delay(100, token);
                    
                    if (!await ClickPutUpForSale(item, token))
                    {
                        LogError?.Invoke($"[AutoMarket] Failed to click 'Put Up for Sale' for {item.ItemName}", null);
                        break;
                    }
                    
                    await Task.Delay(60, token);
                    
                    // Only do price comparison on first batch (or if not using Data Center Scan)
                    bool priceFound = false;
                    if (firstBatch && !skipComparePrices)
                    {
                        // Only compare prices if Data Center Scan is not enabled and this is the first batch
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
                    else if (firstBatch && skipComparePrices)
                    {
                        // Data Center Scan is enabled - use the cached price from EvaluateProfitability
                        priceFound = true; // Mark as found since we're using the pre-calculated price
                        Log?.Invoke($"[AutoMarket] Using cached data center price for {item.ItemName}: {lowestPrice} (skipping compare prices)");
                    }
                    else
                    {
                        // Subsequent batches - use the same price as first batch
                        priceFound = true;
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
                        break;
                    }
                    
                    unsafe
                    {
                        try
                        {
                            // Re-validate RetainerSell addon before use (pointer may have become stale after delays)
                            if (!ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Client.UI.AddonRetainerSell>("RetainerSell", out var retainerSell)
                                || !ECommons.GenericHelpers.IsAddonReady(&retainerSell->AtkUnitBase))
                            {
                                LogError?.Invoke("[AutoMarket] RetainerSell addon not found or not ready when trying to set price", null);
                                break;
                            }
                            
                            if (ECommons.GenericHelpers.TryGetAddonByName<AtkUnitBase>("ItemSearchResult", out var itemSearchAddon))
                            {
                                itemSearchAddon->Close(true);
                            }
                            
                            var ui = &retainerSell->AtkUnitBase;
                            if (ui == null)
                            {
                                LogError?.Invoke("[AutoMarket] RetainerSell AtkUnitBase is null", null);
                                break;
                            }
                            
                            if (lowestPrice > 0)
                            {
                                // Null check for AskingPrice before SetValue
                                if (retainerSell->AskingPrice == null)
                                {
                                    LogError?.Invoke("[AutoMarket] RetainerSell AskingPrice is null", null);
                                    break;
                                }
                                
                                retainerSell->AskingPrice->SetValue((int)lowestPrice);
                                
                                // Set quantity for this batch (only if > 1)
                                if (batchQuantity > 1)
                                {
                                    // Null check for Quantity before SetValue
                                    if (retainerSell->Quantity == null)
                                    {
                                        LogError?.Invoke("[AutoMarket] RetainerSell Quantity is null", null);
                                        break;
                                    }
                                    
                                    retainerSell->Quantity->SetValue(batchQuantity);
                                }
                                
                                ECommons.Automation.Callback.Fire(ui, true, 0);
                                ui->Close(true);
                                
                                // Successfully listed this batch
                                // Subtract the actual quantity we listed (batchQuantity, which is already capped by slot quantity)
                                remainingQuantity -= batchQuantity;
                                totalListed += batchQuantity;
                                anyBatchSucceeded = true;
                                firstBatch = false;
                                
                                Log?.Invoke($"[AutoMarket] Listed {batchQuantity} of {item.ItemName} from slot {currentInventoryType}:{currentInventorySlot} (slot had {actualSlotQuantity}, remaining total: {remainingQuantity}, total listed so far: {totalListed})");
                            }
                            else
                            {
                                LogError?.Invoke("[AutoMarket] No price to set", null);
                                if (ui != null)
                                {
                                    ECommons.Automation.Callback.Fire(ui, true, 1); // cancel
                                    ui->Close(true);
                                }
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogError?.Invoke($"[AutoMarket] Exception setting price and confirming: {ex.Message}", ex);
                            break;
                        }
                    }
                    
                    // If there's more to list, check if current slot is depleted and find next stack if needed
                    if (remainingQuantity > 0)
                    {
                        await Task.Delay(300, token); // Delay between batches
                        
                        // Verify current slot still has items, if not find next stack
                        // Use safe wrapper to re-validate pointer after delay
                        bool slotDepleted = false;
                        unsafe
                        {
                            var slotItem = GetInventorySlotSafe(currentInventoryType, currentInventorySlot);
                            if (slotItem == null || slotItem->ItemId != item.ItemId || slotItem->Quantity <= 0)
                            {
                                slotDepleted = true;
                            }
                        }
                        
                        if (slotDepleted)
                        {
                            // Current slot is depleted, find next stack
                            Log?.Invoke($"[AutoMarket] Current slot ({currentInventoryType} slot {currentInventorySlot}) is depleted. Finding next stack...");
                            var (foundType, foundSlot) = FindNextStackOfItem(item, currentInventoryType, currentInventorySlot);
                            if (foundSlot >= 0)
                            {
                                currentInventoryType = foundType;
                                currentInventorySlot = foundSlot;
                                Log?.Invoke($"[AutoMarket] Moving to next stack at {currentInventoryType} slot {currentInventorySlot}");
                            }
                            else
                            {
                                LogError?.Invoke($"[AutoMarket] No more stacks found for {item.ItemName}. Expected {remainingQuantity} remaining but no stacks available.", null);
                                break; // Exit loop - no more stacks
                            }
                        }
                    }
                }
                
                // Update item quantity to reflect what's remaining (should be 0 if all listed successfully)
                item.Quantity = remainingQuantity;
                // Update inventory location in case we moved to a different stack
                item.InventoryType = currentInventoryType;
                item.InventorySlot = currentInventorySlot;
                
                // Log summary for debugging
                if (remainingQuantity > 0)
                {
                    Log?.Invoke($"[AutoMarket] Listing complete for {item.ItemName}: {remainingQuantity} items remaining (started with {startingQuantity}, listed {totalListed}, math check: {startingQuantity} - {totalListed} = {startingQuantity - totalListed})");
                }
                else
                {
                    Log?.Invoke($"[AutoMarket] Successfully listed all {item.ItemName} (started with {startingQuantity}, listed {totalListed})");
                }
                
                return anyBatchSucceeded;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"[AutoMarket] Error listing item {item.ItemName}: {ex.Message}", ex);
                return false;
            }
        }
        
        /// <summary>
        /// Closes any existing context menu to prevent conflicts when opening a new one.
        /// </summary>
        private unsafe bool CloseExistingContextMenu()
        {
            try
            {
                if (ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Client.UI.AddonContextMenu>("ContextMenu", out var contextMenuAddon))
                {
                    if (ECommons.GenericHelpers.IsAddonReady(&contextMenuAddon->AtkUnitBase))
                    {
                        // Close the existing context menu by pressing Escape or clicking outside
                        contextMenuAddon->AtkUnitBase.Close(true);
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore errors when trying to close context menu
            }
            return false;
        }

        /// <summary>
        /// Verifies that the retainer UI is in a valid state before opening context menus.
        /// </summary>
        private unsafe bool IsRetainerUIReady()
        {
            try
            {
                // Check if RetainerSellList is open and ready (the main retainer inventory window)
                if (ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>("RetainerSellList", out var retainerSellList))
                {
                    if (ECommons.GenericHelpers.IsAddonReady(retainerSellList) && retainerSellList->IsVisible)
                    {
                        return true;
                    }
                }
                
                // Also check if RetainerSell is open (the listing window)
                if (ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>("RetainerSell", out var retainerSell))
                {
                    if (ECommons.GenericHelpers.IsAddonReady(retainerSell) && retainerSell->IsVisible)
                    {
                        return true;
                    }
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Opens context menu for a specific inventory slot. Verifies the slot has the correct item and quantity.
        /// Includes safety checks to prevent crashes.
        /// </summary>
        private async Task<bool> OpenItemContextMenuForSlot(ScannedItem item, InventoryType inventoryType, int slot, CancellationToken token)
        {
            try
            {
                // Step 1: Verify retainer UI is ready
                bool uiReady = false;
                unsafe
                {
                    uiReady = IsRetainerUIReady();
                }
                if (!uiReady)
                {
                    LogError?.Invoke("[AutoMarket] Retainer UI is not ready - cannot open context menu", null);
                    return false;
                }
                
                // Step 2: Close any existing context menu to prevent conflicts
                bool closedMenu = false;
                unsafe
                {
                    closedMenu = CloseExistingContextMenu();
                }
                if (closedMenu)
                {
                    Log?.Invoke("[AutoMarket] Closed existing context menu before opening new one");
                    await Task.Delay(100, token); // Small delay after closing
                }
                
                // Step 3: Verify inventory state
                bool inventoryValid = false;
                unsafe
                {
                    var inventoryManager = GetInventoryManagerSafe();
                    if (inventoryManager == null)
                    {
                        LogError?.Invoke("[AutoMarket] InventoryManager is null after retries", null);
                        return false;
                    }
                    
                    // Verify the slot has the correct item
                    var container = inventoryManager->GetInventoryContainer(inventoryType);
                    if (container == null)
                    {
                        LogError?.Invoke($"[AutoMarket] Container {inventoryType} is null", null);
                        return false;
                    }
                    
                    if (slot < 0 || slot >= container->Size)
                    {
                        LogError?.Invoke($"[AutoMarket] Invalid slot {slot} for {inventoryType}", null);
                        return false;
                    }
                    
                    var slotItem = container->GetInventorySlot(slot);
                    if (slotItem == null || slotItem->ItemId != item.ItemId)
                    {
                        // Slot doesn't have the item (may have been depleted)
                        return false;
                    }
                    
                    // Check if slot has enough quantity (at least 1)
                    if (slotItem->Quantity <= 0)
                    {
                        return false;
                    }
                    
                    inventoryValid = true;
                }
                
                if (!inventoryValid)
                {
                    return false;
                }
                
                // Step 4: Verify AgentInventoryContext is in a valid state and open context menu
                bool agentValid = false;
                unsafe
                {
                    var agent = GetAgentInventoryContextSafe();
                    if (agent == null)
                    {
                        LogError?.Invoke("[AutoMarket] AgentInventoryContext is null after retries", null);
                        return false;
                    }
                    
                    agentValid = true;
                    
                    // Step 5: Add a small delay to ensure UI is stable (before opening)
                    // We'll do this outside unsafe block
                    
                    // Step 6: Double-check that no context menu is open (race condition check)
                    if (ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Client.UI.AddonContextMenu>("ContextMenu", out var existingMenu))
                    {
                        if (ECommons.GenericHelpers.IsAddonReady(&existingMenu->AtkUnitBase))
                        {
                            Log?.Invoke("[AutoMarket] Context menu still open, closing it...");
                            existingMenu->AtkUnitBase.Close(true);
                        }
                    }
                    
                    // Step 7: Open context menu using AgentInventoryContext
                    var inventoryManager = GetInventoryManagerSafe();
                    if (inventoryManager != null)
                    {
                        var container = inventoryManager->GetInventoryContainer(inventoryType);
                        if (container != null)
                        {
                            var slotItem = container->GetInventorySlot(slot);
                            if (slotItem != null)
                            {
                                Log?.Invoke($"[AutoMarket] Opening context menu for {item.ItemName} at {inventoryType} slot {slot} (quantity: {slotItem->Quantity})");
                            }
                        }
                    }
                    agent->OpenForItemSlot(inventoryType, slot, 0, 0);
                }
                
                if (!agentValid)
                {
                    return false;
                }
                
                // Step 5: Add a small delay to ensure UI is stable (after opening)
                await Task.Delay(50, token);
                
                // Step 6 delay: If we closed a menu, wait a bit
                // (Already handled in Step 6 above, but add delay here too)
                await Task.Delay(50, token);
                
                // Step 8: Wait for context menu to appear with validation
                for (int attempts = 0; attempts < 10; attempts++)
                {
                    await Task.Delay(30, token);
                    
                    unsafe
                    {
                        if (ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Client.UI.AddonContextMenu>("ContextMenu", out var contextMenuAddon))
                        {
                            if (ECommons.GenericHelpers.IsAddonReady(&contextMenuAddon->AtkUnitBase))
                            {
                                Log?.Invoke($"[AutoMarket] Context menu opened successfully (attempt {attempts + 1})");
                                return true;
                            }
                        }
                    }
                }
                
                LogError?.Invoke("[AutoMarket] Context menu did not appear after opening", null);
                return false;
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"[AutoMarket] Error opening context menu for slot: {ex.Message}", ex);
                return false;
            }
        }
        
        /// <summary>
        /// Finds the next stack of the same item after the given slot. Returns (InventoryType, slot) or (-1, -1) if not found.
        /// </summary>
        private (InventoryType type, int slot) FindNextStackOfItem(ScannedItem item, InventoryType afterType, int afterSlot)
        {
            try
            {
                unsafe
                {
                    InventoryType[] inventoryTypes = {
                        InventoryType.Inventory1,
                        InventoryType.Inventory2,
                        InventoryType.Inventory3,
                        InventoryType.Inventory4
                    };
                    
                    bool foundAfterSlot = false;
                    
                    foreach (var type in inventoryTypes)
                    {
                        // Use safe wrapper to re-validate container pointer in each loop iteration
                        var container = GetInventoryContainerSafe(type, 1); // Single attempt per iteration (we're already in a loop)
                        if (container == null) continue;
                        
                        int startSlot = 0;
                        // If we're in the same container, start searching after the current slot
                        if (type == afterType)
                        {
                            startSlot = afterSlot + 1;
                            foundAfterSlot = true;
                        }
                        // If we've already passed the afterType container, search from the beginning
                        else if (foundAfterSlot)
                        {
                            startSlot = 0;
                        }
                        // Otherwise, skip this container (we haven't reached afterType yet)
                        else
                        {
                            continue;
                        }
                        
                        for (int slot = startSlot; slot < container->Size; slot++)
                        {
                            var slotItem = container->GetInventorySlot(slot);
                            if (slotItem != null && slotItem->ItemId == item.ItemId && slotItem->Quantity > 0)
                            {
                                // Found a stack with the same item
                                return (type, slot);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"[AutoMarket] Error finding next stack: {ex.Message}", null);
            }
            
            return (InventoryType.Inventory1, -1);
        }
        
        public async Task<bool> OpenItemContextMenu(ScannedItem item, CancellationToken token)
        {
            try
            {
                Log?.Invoke($"[AutoMarket] Finding item {item.ItemName} (ID: {item.ItemId}) in inventory...");
                
                // Use the specific slot from the scanned item if available
                if (item.InventoryType != InventoryType.Inventory1 || item.InventorySlot >= 0)
                {
                    bool opened = await OpenItemContextMenuForSlot(item, item.InventoryType, item.InventorySlot, token);
                    if (opened)
                    {
                        return true;
                    }
                }
                
                // Fallback: Find the item in inventory (for backwards compatibility)
                InventoryType foundType = InventoryType.Inventory1;
                int foundSlot = -1;
                
                unsafe
                {
                    var inventoryManager = GetInventoryManagerSafe();
                    if (inventoryManager == null)
                    {
                        LogError?.Invoke("[AutoMarket] InventoryManager is null after retries", null);
                        return false;
                    }
                    
                    InventoryType[] inventoryTypes = {
                        InventoryType.Inventory1,
                        InventoryType.Inventory2,
                        InventoryType.Inventory3,
                        InventoryType.Inventory4
                    };
                    
                    foreach (var type in inventoryTypes)
                    {
                        // Use safe wrapper to re-validate container pointer in each loop iteration
                        var container = GetInventoryContainerSafe(type, 1); // Single attempt per iteration
                        if (container == null) continue;
                        
                        for (int slot = 0; slot < container->Size; slot++)
                        {
                            var slotItem = container->GetInventorySlot(slot);
                            if (slotItem != null && slotItem->ItemId == item.ItemId && slotItem->Quantity > 0)
                            {
                                foundType = type;
                                foundSlot = slot;
                                Log?.Invoke($"[AutoMarket] Found item at {type} slot {slot}");
                                break;
                            }
                        }
                        
                        if (foundSlot >= 0) break;
                    }
                }
                
                if (foundSlot < 0)
                {
                    LogError?.Invoke($"[AutoMarket] Item {item.ItemName} not found in inventory", null);
                    return false;
                }
                
                // Verify retainer UI is ready before opening context menu
                bool uiReady = false;
                unsafe
                {
                    uiReady = IsRetainerUIReady();
                }
                if (!uiReady)
                {
                    LogError?.Invoke("[AutoMarket] Retainer UI is not ready - cannot open context menu", null);
                    return false;
                }
                
                // Close any existing context menu
                bool closedMenu = false;
                unsafe
                {
                    closedMenu = CloseExistingContextMenu();
                }
                if (closedMenu)
                {
                    await Task.Delay(100, token);
                }
                
                // Open context menu using AgentInventoryContext
                bool agentValid = false;
                unsafe
                {
                    var agent = GetAgentInventoryContextSafe();
                    if (agent == null)
                    {
                        LogError?.Invoke("[AutoMarket] AgentInventoryContext is null after retries", null);
                        return false;
                    }
                    
                    agentValid = true;
                    
                    // Double-check no context menu is open
                    if (ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Client.UI.AddonContextMenu>("ContextMenu", out var existingMenu))
                    {
                        if (ECommons.GenericHelpers.IsAddonReady(&existingMenu->AtkUnitBase))
                        {
                            existingMenu->AtkUnitBase.Close(true);
                        }
                    }
                    
                    Log?.Invoke($"[AutoMarket] Opening context menu for item at {foundType} slot {foundSlot}");
                    agent->OpenForItemSlot(foundType, foundSlot, 0, 0);
                }
                
                if (!agentValid)
                {
                    return false;
                }
                
                // Add delay to ensure UI is stable (after opening)
                await Task.Delay(50, token);
                await Task.Delay(50, token);
                
                // Wait for context menu to appear with validation
                for (int attempts = 0; attempts < 10; attempts++)
                {
                    await Task.Delay(30, token);
                    
                    unsafe
                    {
                        if (ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Client.UI.AddonContextMenu>("ContextMenu", out var contextMenuAddon))
                        {
                            if (ECommons.GenericHelpers.IsAddonReady(&contextMenuAddon->AtkUnitBase))
                            {
                                return true;
                            }
                        }
                    }
                }
                
                LogError?.Invoke("[AutoMarket] Context menu did not appear after opening", null);
                return false;
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
                
                // First, verify retainer UI is still ready
                unsafe
                {
                    if (!IsRetainerUIReady())
                    {
                        LogError?.Invoke("[AutoMarket] Retainer UI is not ready when trying to click Put Up for Sale", null);
                        return false;
                    }
                }
                
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
                    // Re-validate context menu is still ready before clicking
                    if (!ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Client.UI.AddonContextMenu>("ContextMenu", out var contextMenuAddon))
                    {
                        LogError?.Invoke("[AutoMarket] ContextMenu addon not found when trying to click", null);
                        return false;
                    }
                    
                    var atkUnitBase = &contextMenuAddon->AtkUnitBase;
                    if (atkUnitBase == null || !ECommons.GenericHelpers.IsAddonReady(atkUnitBase))
                    {
                        LogError?.Invoke("[AutoMarket] ContextMenu addon is not ready", null);
                        return false;
                    }
                    
                    // Verify retainer UI is still ready
                    if (!IsRetainerUIReady())
                    {
                        LogError?.Invoke("[AutoMarket] Retainer UI is not ready when clicking context menu", null);
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
                nint yesnoName = nint.Zero;
                unsafe
                {
                    try
                    {
                        // Try to find and confirm SelectYesno dialog if present
                        var raptureMgr = GetRaptureAtkUnitManagerSafe();
                        if (raptureMgr != null)
                        {
                            yesnoName = System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi("SelectYesno");
                            if (yesnoName != nint.Zero)
                            {
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
                
                // First, verify retainer UI is still ready
                unsafe
                {
                    if (!IsRetainerUIReady())
                    {
                        LogError?.Invoke("[AutoMarket] Retainer UI is not ready when trying to click Have Retainer Sell Items", null);
                        return false;
                    }
                }
                
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
                    // Re-validate context menu is still ready before clicking
                    if (!ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Client.UI.AddonContextMenu>("ContextMenu", out var contextMenuAddon))
                    {
                        LogError?.Invoke("[AutoMarket] ContextMenu addon not found when trying to click", null);
                        return false;
                    }
                    
                    var atkUnitBase = &contextMenuAddon->AtkUnitBase;
                    if (atkUnitBase == null || !ECommons.GenericHelpers.IsAddonReady(atkUnitBase))
                    {
                        LogError?.Invoke("[AutoMarket] ContextMenu addon is not ready", null);
                        return false;
                    }
                    
                    // Verify retainer UI is still ready
                    if (!IsRetainerUIReady())
                    {
                        LogError?.Invoke("[AutoMarket] Retainer UI is not ready when clicking context menu", null);
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
                        var selectAddon = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)selectAddonPtr;
                        if (selectAddon != null)
                        {
                            selectAddon->Close(true);
                        }
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
                            var raptureMgr = GetRaptureAtkUnitManagerSafe();
                            if (raptureMgr != null)
                            {
                                if (yesnoName == nint.Zero)
                                {
                                    yesnoName = System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi("SelectYesno");
                                }
                                if (yesnoName != nint.Zero)
                                {
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
