using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CosmeticStore.MVC.Models;

namespace CosmeticStore.MVC.Controllers
{
    public class ProductVariantsController : Controller
    {
        private readonly CosmeticStoreContext _context;

        public ProductVariantsController(CosmeticStoreContext context)
        {
            _context = context;
        }

        // GET: ProductVariants?productId=5
        public async Task<IActionResult> Index(int? productId)
        {
            if (productId == null) return NotFound();

            var product = await _context.Products.FindAsync(productId);
            if (product == null) return NotFound();

            ViewBag.ProductId = productId;
            ViewBag.ProductName = product.ProductName;

            var variants = await _context.ProductVariants
                .Where(v => v.ProductId == productId)
                .ToListAsync();

            return View(variants);
        }

        // GET: ProductVariants/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var productVariant = await _context.ProductVariants
                .Include(p => p.Product)
                .FirstOrDefaultAsync(m => m.VariantId == id);

            if (productVariant == null) return NotFound();

            return View(productVariant);
        }

        // GET: ProductVariants/Create?productId=5
        public async Task<IActionResult> Create(int? productId)
        {
            if (productId == null) return NotFound();

            // Tìm sản phẩm để lấy tên, hỗ trợ hiển thị trong View
            var product = await _context.Products.FindAsync(productId);
            if (product == null) return NotFound();

            ViewBag.ProductName = product.ProductName;

            var model = new ProductVariant { ProductId = productId.Value };
            return View(model);
        }

        // POST: ProductVariants/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProductVariant productVariant)
        {
            // Kiểm tra SKU trùng lặp (chỉ trong phạm vi sản phẩm này)
            if (await _context.ProductVariants.AnyAsync(p =>
                p.Sku == productVariant.Sku &&
                p.ProductId == productVariant.ProductId))
            {
                ModelState.AddModelError("Sku", "SKU đã tồn tại cho sản phẩm này.");
            }

            // Xóa validation cho navigation properties nếu có
            ModelState.Remove("Product");
            ModelState.Remove("OrderDetails");

            if (ModelState.IsValid)
            {
                _context.Add(productVariant);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index), new { productId = productVariant.ProductId });
            }

            // QUAN TRỌNG: Lấy lại ProductName để hiển thị trên View khi có lỗi
            var product = await _context.Products.FindAsync(productVariant.ProductId);
            if (product != null)
            {
                ViewBag.ProductName = product.ProductName;
            }

            return View(productVariant);
        }


        // GET: ProductVariants/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var productVariant = await _context.ProductVariants.FindAsync(id);
            if (productVariant == null) return NotFound();

            return View(productVariant);
        }

        // POST: ProductVariants/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("VariantId,ProductId,VariantName,Sku,Price,SalePrice,StockQuantity")] ProductVariant productVariant)
        {
            if (id != productVariant.VariantId) return NotFound();

            // Kiểm tra validation: SKU không được trống (Nếu chưa dùng Data Annotations)
            if (string.IsNullOrWhiteSpace(productVariant.Sku))
                ModelState.AddModelError("Sku", "SKU không được để trống.");
            // Kiểm tra trùng SKU (bỏ qua bản ghi hiện tại)
            else if (await _context.ProductVariants.AnyAsync(p =>
                p.Sku == productVariant.Sku &&
                p.VariantId != productVariant.VariantId &&
                p.ProductId == productVariant.ProductId))
            {
                ModelState.AddModelError("Sku", "SKU đã tồn tại cho sản phẩm này.");
            }

            // Xóa validation lỗi liên quan đến navigation property
            ModelState.Remove("Product");
            ModelState.Remove("OrderDetails");

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(productVariant);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ProductVariantExists(productVariant.VariantId))
                        return NotFound();
                    else
                        throw;
                }

                return RedirectToAction(nameof(Index), new { productId = productVariant.ProductId });
            }

            return View(productVariant);
        }

        // GET: ProductVariants/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var productVariant = await _context.ProductVariants
                .Include(p => p.Product)
                .FirstOrDefaultAsync(m => m.VariantId == id);

            if (productVariant == null) return NotFound();

            return View(productVariant);
        }

        // POST: ProductVariants/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var productVariant = await _context.ProductVariants.FindAsync(id);

            if (productVariant != null)
            {
                _context.ProductVariants.Remove(productVariant);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index), new { productId = productVariant.ProductId });
            }

            return RedirectToAction(nameof(Index));
        }

        private bool ProductVariantExists(int id)
        {
            return _context.ProductVariants.Any(e => e.VariantId == id);
        }
    }
}