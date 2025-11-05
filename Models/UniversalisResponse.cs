using System;

namespace AutomarketPro.Models
{
    public class UniversalisResponse
    {
        public Listing[] listings { get; set; } = Array.Empty<Listing>();
        public RecentHistory[] recentHistory { get; set; } = Array.Empty<RecentHistory>();
    }
    
    public class Listing
    {
        public int pricePerUnit { get; set; }
        public int quantity { get; set; }
        public bool hq { get; set; }
    }
    
    public class RecentHistory
    {
        public int pricePerUnit { get; set; }
        public int quantity { get; set; }
        public bool hq { get; set; }
        public long timestamp { get; set; }
    }
}

