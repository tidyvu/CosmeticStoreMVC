using CosmeticStore.MVC.Helpers;
using CosmeticStore.MVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Microsoft.AspNetCore.Hosting; // Cần thiết

namespace CosmeticStore.MVC.Controllers
{
    public class ProductsController : Controller
    {
        private readonly CosmeticStoreContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public ProductsController(CosmeticStoreContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        // GET: Products (Danh sách sản phẩm - đã có lọc và phân trang)
        public async Task<IActionResult> Index(
            string sortOrder,
            string currentFilter,
            string searchString,
            decimal? minPrice,
            decimal? maxPrice,
            int? pageNumber)
        {
            ViewData["CurrentSort"] = sortOrder;
            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["PriceSortParm"] = sortOrder == "Price" ? "price_desc" : "Price";

            if (searchString != null)
            {
                pageNumber = 1;
            }
            else
            {
                searchString = currentFilter;
            }

            ViewData["CurrentFilter"] = searchString;
            ViewData["MinPrice"] = minPrice; 
            ViewData["MaxPrice"] = maxPrice;

            var products = _context.Products
                .Include(p => p.Brand)
                .Include(p => p.Category)
                .Include(p => p.ProductVariants) 
                .AsQueryable();

            // 1. Tìm kiếm theo tên
            if (!String.IsNullOrEmpty(searchString))
            {
                products = products.Where(s => s.ProductName.Contains(searchString));
            }

            // 2. Lọc theo giá (Dựa trên giá của biến thể đầu tiên hoặc bất kỳ biến thể nào)
            if (minPrice.HasValue)
            {
                products = products.Where(p => p.ProductVariants.Any(v => (v.SalePrice ?? v.Price) >= minPrice.Value));
            }

            if (maxPrice.HasValue)
            {
                products = products.Where(p => p.ProductVariants.Any(v => (v.SalePrice ?? v.Price) <= maxPrice.Value));
            }

            // 3. Sắp xếp
            switch (sortOrder)
            {
                case "name_desc":
                    products = products.OrderByDescending(s => s.ProductName);
                    break;
                case "Price":
                    products = products.OrderBy(s => s.ProductVariants.Min(v => v.SalePrice ?? v.Price));
                    break;
                case "price_desc":
                    products = products.OrderByDescending(s => s.ProductVariants.Min(v => v.SalePrice ?? v.Price));
                    break;
                default:
                    products = products.OrderBy(s => s.ProductName);
                    break;
            }

            int pageSize = 9; // Hiển thị 9 sản phẩm mỗi trang
            return View(await PaginatedList<Product>.CreateAsync(products.AsNoTracking(), pageNumber ?? 1, pageSize));
        }

        // GET: Products/Details/5 (Chi tiết sản phẩm)
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var product = await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Brand)
                .Include(p => p.ProductVariants)
                .Include(p => p.Reviews).ThenInclude(r => r.User)
                .FirstOrDefaultAsync(m => m.ProductId == id);

            if (product == null) return NotFound();

            return View(product);
        }

        // --- ADMIN CRUD ACTIONS ---

        // GET: Products/Create
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            ViewData["BrandId"] = new SelectList(_context.Brands, "BrandId", "BrandName");
            ViewData["CategoryId"] = new SelectList(_context.Categories, "CategoryId", "CategoryName");
            return View();
        }

        // POST: Products/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(Product product)
        {
            if (ModelState.IsValid)
            {
                if (product.ImageFile != null)
                {
                    var fileName = Guid.NewGuid().ToString() + Path.GetExtension(product.ImageFile.FileName);
                    var uploadPath = Path.Combine(_webHostEnvironment.WebRootPath, "images", "products");

                    if (!Directory.Exists(uploadPath)) Directory.CreateDirectory(uploadPath);
                    var filePath = Path.Combine(uploadPath, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await product.ImageFile.CopyToAsync(stream);
                    }
                    product.MainImageUrl = "/images/products/" + fileName;
                }

                _context.Add(product);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            ViewData["BrandId"] = new SelectList(_context.Brands, "BrandId", "BrandName", product.BrandId);
            ViewData["CategoryId"] = new SelectList(_context.Categories, "CategoryId", "CategoryName", product.CategoryId);
            return View(product);
        }

        // GET: Products/Edit/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || _context.Products == null) return NotFound();

            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            ViewData["BrandId"] = new SelectList(_context.Brands, "BrandId", "BrandName", product.BrandId);
            ViewData["CategoryId"] = new SelectList(_context.Categories, "CategoryId", "CategoryName", product.CategoryId);
            return View(product);
        }

        // POST: Products/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, Product product)
        {
            if (id != product.ProductId) return NotFound();

            // Lấy sản phẩm hiện tại từ DB để giữ lại MainImageUrl cũ nếu không upload ảnh mới
            var existingProduct = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.ProductId == id);

            if (ModelState.IsValid)
            {
                try
                {
                    if (product.ImageFile != null)
                    {
                        var fileName = Guid.NewGuid().ToString() + Path.GetExtension(product.ImageFile.FileName);
                        var uploadPath = Path.Combine(_webHostEnvironment.WebRootPath, "images", "products");
                        if (!Directory.Exists(uploadPath)) Directory.CreateDirectory(uploadPath);
                        var filePath = Path.Combine(uploadPath, fileName);

                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await product.ImageFile.CopyToAsync(stream);
                        }

                        product.MainImageUrl = "/images/products/" + fileName;

                        // Xóa ảnh cũ
                        if (!string.IsNullOrEmpty(existingProduct.MainImageUrl))
                        {
                            var oldFilePath = Path.Combine(_webHostEnvironment.WebRootPath, existingProduct.MainImageUrl.TrimStart('/'));
                            if (System.IO.File.Exists(oldFilePath))
                            {
                                System.IO.File.Delete(oldFilePath);
                            }
                        }
                    }
                    else
                    {
                        // Giữ lại MainImageUrl cũ
                        product.MainImageUrl = existingProduct.MainImageUrl;
                    }

                    _context.Update(product);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ProductExists(product.ProductId)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }

            ViewData["BrandId"] = new SelectList(_context.Brands, "BrandId", "BrandName", product.BrandId);
            ViewData["CategoryId"] = new SelectList(_context.Categories, "CategoryId", "CategoryName", product.CategoryId);
            return View(product);
        }

        // GET: Products/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null || _context.Products == null) return NotFound();

            var product = await _context.Products
                .Include(p => p.Brand)
                .Include(p => p.Category)
                .FirstOrDefaultAsync(m => m.ProductId == id);
            if (product == null) return NotFound();

            return View(product);
        }

        // POST: Products/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (_context.Products == null) return Problem("Entity set 'CosmeticStoreContext.Products' is null.");

            var product = await _context.Products.FindAsync(id);

            if (product != null)
            {
                // Xóa file ảnh liên quan
                if (!string.IsNullOrEmpty(product.MainImageUrl))
                {
                    var filePath = Path.Combine(_webHostEnvironment.WebRootPath, product.MainImageUrl.TrimStart('/'));
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }
                _context.Products.Remove(product);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool ProductExists(int id)
        {
            return (_context.Products?.Any(e => e.ProductId == id)).GetValueOrDefault();
        }

        // --- REVIEW ACTION ---

        // POST: Products/AddReview
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> AddReview(int productId, int rating, string comment)
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                TempData["ErrorMessage"] = "Bạn cần đăng nhập để đánh giá.";
                return RedirectToAction(nameof(Details), new { id = productId });
            }

            if (rating < 1 || rating > 5)
            {
                TempData["ErrorMessage"] = "Đánh giá sao không hợp lệ.";
                return RedirectToAction(nameof(Details), new { id = productId });
            }

            var review = new Review
            {
                ProductId = productId,
                UserId = userId,
                Rating = rating,
                Comment = comment,
                ReviewDate = DateTime.Now
            };

            _context.Reviews.Add(review);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Đánh giá của bạn đã được gửi thành công!";
            return RedirectToAction(nameof(Details), new { id = productId });
        }
    }
}