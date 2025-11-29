using System;
using System.Collections.Generic;

namespace CosmeticStore.MVC.Models
{
    public partial class ProductVariant
    {
        public ProductVariant()
        {
            OrderDetails = new HashSet<OrderDetail>();
        }

        public int VariantId { get; set; }
        public int ProductId { get; set; }
        public string? VariantName { get; set; }
        public string? Sku { get; set; }
        public decimal Price { get; set; }
        public decimal? SalePrice { get; set; }
        public int StockQuantity { get; set; }

        public virtual Product Product { get; set; } = null!;
        public virtual ICollection<OrderDetail> OrderDetails { get; set; }
    }
}
