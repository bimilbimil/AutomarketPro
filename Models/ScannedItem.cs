using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutomarketPro.Models
{
    public class ScannedItem
    {
        public uint ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public int Quantity { get; set; }
        public bool IsHQ { get; set; }
        public uint VendorPrice { get; set; }
        public uint MarketPrice { get; set; }
        public uint ListingPrice { get; set; }
        public uint RecentSalePrice { get; set; }
        public bool IsProfitable { get; set; }
        public int ProfitPerItem { get; set; }
        public long TotalProfit { get; set; }
        public uint StackSize { get; set; }
        public InventoryType InventoryType { get; set; }
        public int InventorySlot { get; set; }
    }
}

