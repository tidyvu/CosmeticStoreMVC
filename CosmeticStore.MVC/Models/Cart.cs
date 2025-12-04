using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CosmeticStore.MVC.Models
{
    public class Cart
    {
        [Key]
        public int CartId { get; set; }
        public int UserId { get; set; }
        public int ProductId { get; set; }
        public int VariantId { get; set; }
        public int Quantity { get; set; }
        public DateTime CreatedDate { get; set; }

        [ForeignKey("ProductId")]
        public virtual Product? Product { get; set; }
        [ForeignKey("UserId")]
        public virtual User? User { get; set; }
        [ForeignKey("VariantId")]
        public virtual ProductVariant? ProductVariant { get; set; }
    }
}