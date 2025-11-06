using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FFXIVClientStructs.FFXIV.Client.Game;
using AutomarketPro.Core;
using AutomarketPro.Models;
using Newtonsoft.Json;
using ECommons.DalamudServices;
using InventoryManager = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager;

namespace AutomarketPro.Services
{
    public class MarketScanner : IDisposable
    {
        private readonly AutomarketProPlugin Plugin;
        private readonly HttpClient HttpClient;
        private List<ScannedItem> ScannedItems = new();
        private bool IsScanning = false;
        private CancellationTokenSource? CancelToken;
        private string? CachedWorldName = null;
        
        public IReadOnlyList<ScannedItem> Items => ScannedItems.AsReadOnly();
        public bool Scanning => IsScanning;
        public event Action? ScanComplete;
        
        /// <summary>
        /// Gets the current world name. Returns null if not available yet.
        /// </summary>
        public string? CurrentWorldName
        {
            get
            {
                if (!string.IsNullOrEmpty(CachedWorldName))
                    return CachedWorldName;
                    
                // Try to get it fresh if not cached
                var worldName = GetWorldNameOnMainThread();
                if (!string.IsNullOrEmpty(worldName))
                {
                    CachedWorldName = worldName;
                }
                return worldName;
            }
        }
        
        public MarketScanner(AutomarketProPlugin plugin)
        {
            Plugin = plugin;
            HttpClient = new HttpClient();
            HttpClient.DefaultRequestHeaders.Add("User-Agent", "AutoMarketPro/1.0");
        }
        
        private void Log(string message)
        {
            Plugin?.MainWindow?.Log(message);
        }
        
        private void LogError(string message, Exception? ex = null)
        {
            Plugin?.MainWindow?.LogError(message, ex);
        }
        
        private void LogWarning(string message)
        {
            Plugin?.MainWindow?.LogWarning(message);
        }
        
        public void CacheWorldName()
        {
            CachedWorldName = GetWorldNameOnMainThread();
        }
        
        public async Task<bool> StartScanning()
        {
            try
            {
                if (IsScanning)
                {
                    LogWarning("[AutoMarket] [SCAN] Already scanning! Please wait for current scan to finish.");
                    return false;
                }
                
                if (Plugin == null)
                {
                    LogError("[AutoMarket] [SCAN] Plugin reference is null");
                    return false;
                }
                
                if (Plugin.DataManager == null)
                {
                    LogError("[AutoMarket] [SCAN] DataManager is null");
                    return false;
                }
                
                if (string.IsNullOrEmpty(CachedWorldName))
                {
                    CacheWorldName();
                }
                
                IsScanning = true;
                ScannedItems.Clear();
                CancelToken = new CancellationTokenSource();
                
                // Send chat message when scanning starts
                Plugin?.PrintChat("[AutoMarket] Scanning inventory started...");
                
                ScanInventory();
                
                if (ScannedItems.Count == 0)
                {
                    // Send chat message when scanning completes with no items
                    Plugin?.PrintChat("[AutoMarket] Scanning complete! No items found.");
                    return true;
                }
                
                await FetchMarketPrices(CancelToken.Token);
                EvaluateProfitability();
                
                // Send chat message when scanning completes successfully
                if (ScannedItems != null)
                {
                    Plugin?.PrintChat($"[AutoMarket] Scanning complete! Found {ScannedItems.Count} items.");
                }
                else
                {
                    Plugin?.PrintChat("[AutoMarket] Scanning complete!");
                }
                
                ScanComplete?.Invoke();
                
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                LogError("[AutoMarket] Scan failed", ex);
                return false;
            }
            finally
            {
                IsScanning = false;
                CancelToken?.Dispose();
                CancelToken = null;
            }
        }
        
        public void StopScanning()
        {
            CancelToken?.Cancel();
            IsScanning = false;
        }
        
        private unsafe void ScanInventory()
        {
            var inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null || Plugin.DataManager == null)
            {
                LogError("[AutoMarket] [SCAN] InventoryManager or DataManager is null");
                return;
            }
            
            Lumina.Excel.ExcelSheet<Lumina.Excel.Sheets.Item>? itemSheet = null;
            try
            {
                itemSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            }
            catch (Exception ex)
            {
                LogError("[AutoMarket] [SCAN] Exception while getting Item sheet", ex);
                return;
            }
            
            if (itemSheet == null)
            {
                LogError("[AutoMarket] [SCAN] Item sheet is null");
                return;
            }
            var inventoryTypes = new[]
            {
                InventoryType.Inventory1,
                InventoryType.Inventory2,
                InventoryType.Inventory3,
                InventoryType.Inventory4
            };
            
            foreach (var type in inventoryTypes)
            {
                var container = inventoryManager->GetInventoryContainer(type);
                if (container == null) continue;
                
                for (int i = 0; i < container->Size; i++)
                {
                    var slot = container->GetInventorySlot(i);
                    if (slot == null || slot->ItemId == 0) continue;
                    
                    var itemData = itemSheet.GetRow(slot->ItemId);
                    if (itemData.RowId == 0) continue;
                    
                    if (itemData.ItemSearchCategory.RowId == 0 || itemData.IsUntradable) continue;
                    if (Plugin.Configuration.IgnoredItemIds.Contains(slot->ItemId)) continue;
                    
                    var itemName = itemData.Name.ToString();
                    if (string.IsNullOrWhiteSpace(itemName))
                    {
                        itemName = $"Item#{slot->ItemId}";
                    }
                    
                    var item = new ScannedItem
                    {
                        ItemId = slot->ItemId,
                        ItemName = itemName,
                        Quantity = slot->Quantity,
                        IsHQ = slot->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality),
                        VendorPrice = itemData.PriceLow,
                        StackSize = itemData.StackSize,
                        InventoryType = type,
                        InventorySlot = i
                    };
                    
                    var existing = ScannedItems.FirstOrDefault(x => x.ItemId == item.ItemId && x.IsHQ == item.IsHQ);
                    if (existing != null)
                    {
                        existing.Quantity += item.Quantity;
                    }
                    else
                    {
                        ScannedItems.Add(item);
                    }
                }
            }
        }
        
        private string? GetWorldNameOnMainThread()
        {
            try
            {
                if (Svc.ClientState?.LocalPlayer != null)
                {
                    var currentWorld = Svc.ClientState.LocalPlayer.CurrentWorld;
                    var worldObj = currentWorld.Value;
                    return worldObj.Name.ToString();
                }
            }
            catch (Exception ex)
            {
                LogError($"[AutoMarket] [SCAN] Error getting world: {ex.Message}", ex);
            }

            try
            {
                if (Plugin.ClientState?.LocalPlayer != null)
                {
                    var currentWorld = Plugin.ClientState.LocalPlayer.CurrentWorld;
                    var worldObj = currentWorld.Value;
                    return worldObj.Name.ToString();
                }
            }
            catch (Exception ex)
            {
                LogError($"[AutoMarket] [SCAN] Error getting world (fallback): {ex.Message}", ex);
            }

            return "Excalibur";
        }
        
        private async Task FetchMarketPrices(CancellationToken cancelToken)
        {
            string world = CachedWorldName ?? "Excalibur";
            
            foreach (var item in ScannedItems)
            {
                if (cancelToken.IsCancellationRequested) break;
                
                try
                {
                    
                    var hqParam = item.IsHQ ? "?hq=1" : "";
                    var url = $"https://universalis.app/api/v2/{world}/{item.ItemId}{hqParam}";
                    
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(10));
                    
                    var response = await HttpClient.GetStringAsync(url, cts.Token);
                    var data = JsonConvert.DeserializeObject<UniversalisResponse>(response);
                    
                    if (data?.listings?.Length > 0)
                    {
                        item.MarketPrice = (uint)data.listings
                            .OrderBy(l => l.pricePerUnit)
                            .First().pricePerUnit;
                        
                        if (data.recentHistory?.Length > 0)
                        {
                            item.RecentSalePrice = (uint)data.recentHistory
                                .OrderByDescending(h => h.timestamp)
                                .First().pricePerUnit;
                        }
                    }
                    else
                    {
                        item.MarketPrice = (uint)(item.VendorPrice * 1.5);
                    }
                    
                    await Task.Delay(120, cancelToken);
                }
                catch (OperationCanceledException)
                {
                    item.MarketPrice = item.VendorPrice;
                }
                catch (Exception ex)
                {
                    LogError($"Failed to fetch price for item {item.ItemId}", ex);
                    item.MarketPrice = item.VendorPrice;
                }
            }
        }
        
        private void EvaluateProfitability()
        {
            var config = Plugin.Configuration;
            
            foreach (var item in ScannedItems)
            {
                uint priceForProfitability = item.MarketPrice;
                bool usingRecentSale = false;
                
                if (item.RecentSalePrice > 0)
                {
                    bool condition1 = item.RecentSalePrice > item.MarketPrice * 2;
                    bool condition2 = item.MarketPrice < item.VendorPrice * 2 && item.RecentSalePrice > item.VendorPrice + config.MinProfitThreshold;
                    
                    if (condition1 || condition2)
                    {
                        priceForProfitability = item.RecentSalePrice;
                        usingRecentSale = true;
                    }
                }
                
                if (config.AutoUndercut && item.MarketPrice > 0)
                {
                    item.ListingPrice = (uint)Math.Max(1, item.MarketPrice - config.UndercutAmount);
                }
                else
                {
                    item.ListingPrice = item.MarketPrice;
                }
                
                var expectedSalePrice = usingRecentSale ? priceForProfitability : item.ListingPrice;
                var profitMargin = (int)expectedSalePrice - (int)item.VendorPrice;
                
                item.ProfitPerItem = profitMargin;
                item.TotalProfit = profitMargin * item.Quantity;
                item.IsProfitable = item.TotalProfit > config.MinProfitThreshold;
            }
            
            // Sort by total profit descending
            ScannedItems = ScannedItems.OrderByDescending(i => i.TotalProfit).ToList();
        }
        
        public List<ScannedItem> GetProfitableItems() => ScannedItems.Where(i => i.IsProfitable).ToList();
        public List<ScannedItem> GetUnprofitableItems() => ScannedItems.Where(i => !i.IsProfitable).ToList();
        
        public void Dispose()
        {
            CancelToken?.Cancel();
            HttpClient?.Dispose();
        }
    }
}

