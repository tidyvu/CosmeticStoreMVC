namespace CosmeticStore.MVC.Models
{
    public class CartItem
    {
        public int ProductId { get; set; }

        // BỔ SUNG để hỗ trợ phân loại sản phẩm
        public int VariantId { get; set; }
        public string VariantName { get; set; }
        // ------------------------------------

        public string ProductName { get; set; }
        public string ImageUrl { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }

        // Tính toán thành tiền tự động
        public decimal TotalPrice => Price * Quantity;
    }
}