using CosmeticStore.MVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.IO;

namespace CosmeticStore.MVC.Controllers
{
    [Authorize(Roles = "Admin")]
    public class BrandsController : Controller
    {
        private readonly CosmeticStoreContext _context;

        public BrandsController(CosmeticStoreContext context)
        {
            _context = context;
        }

        // GET: Brands
        public async Task<IActionResult> Index()
        {
            return _context.Brands != null ?
                        View(await _context.Brands.ToListAsync()) :
                        Problem("Entity set 'CosmeticStoreContext.Brands' is null.");
        }

        // GET: Brands/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || _context.Brands == null)
            {
                return NotFound();
            }

            var brand = await _context.Brands
                .FirstOrDefaultAsync(m => m.BrandId == id);
            if (brand == null)
            {
                return NotFound();
            }

            return View(brand);
        }

        // GET: Brands/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Brands/Create (ĐÃ CẬP NHẬT LOGIC TẢI FILE)
        [HttpPost]
        [ValidateAntiForgeryToken]
        // Bổ sung IFormFile? LogoFile để nhận file từ form
        public async Task<IActionResult> Create([Bind("BrandId,BrandName,LogoUrl")] Brand brand, IFormFile? LogoFile)
        {
            if (LogoFile != null && LogoFile.Length > 0)
            {
                // Tạo tên file duy nhất
                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(LogoFile.FileName);
                // Định nghĩa đường dẫn lưu trữ: wwwroot/images/brands
                var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "brands");

                if (!Directory.Exists(uploadPath)) Directory.CreateDirectory(uploadPath);

                var filePath = Path.Combine(uploadPath, fileName);

                // Lưu file vật lý
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await LogoFile.CopyToAsync(stream);
                }

                // Cập nhật URL vào Model
                brand.LogoUrl = "/images/brands/" + fileName;
            }
            ModelState.Remove("Products");

            if (ModelState.IsValid)
            {
                _context.Add(brand);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(brand);
        }

        // GET: Brands/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || _context.Brands == null)
            {
                return NotFound();
            }

            var brand = await _context.Brands.FindAsync(id);
            if (brand == null)
            {
                return NotFound();
            }
            return View(brand);
        }

        // POST: Brands/Edit/5 (ĐÃ CẬP NHẬT LOGIC TẢI FILE)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("BrandId,BrandName,LogoUrl")] Brand brand, IFormFile? LogoFile)
        {
            if (id != brand.BrandId)
            {
                return NotFound();
            }

            ModelState.Remove("Products");

            if (ModelState.IsValid)
            {
                if (LogoFile != null && LogoFile.Length > 0)
                {
                    // 1. Tạo tên file duy nhất
                    var fileName = Guid.NewGuid().ToString() + Path.GetExtension(LogoFile.FileName);
                    // Định nghĩa đường dẫn lưu trữ: wwwroot/images/brands
                    var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "brands");

                    if (!Directory.Exists(uploadPath)) Directory.CreateDirectory(uploadPath);

                    var filePath = Path.Combine(uploadPath, fileName);

                    // 2. Lưu file vật lý
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await LogoFile.CopyToAsync(stream);
                    }

                    // 3. Cập nhật URL mới vào Model
                    // (Nếu không có file mới, LogoUrl sẽ giữ giá trị cũ được truyền từ input hidden trong View)
                    brand.LogoUrl = "/images/brands/" + fileName;
                }

                try
                {
                    _context.Update(brand);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!BrandExists(brand.BrandId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            return View(brand);
        }

        // GET: Brands/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null || _context.Brands == null)
            {
                return NotFound();
            }

            var brand = await _context.Brands
                .FirstOrDefaultAsync(m => m.BrandId == id);
            if (brand == null)
            {
                return NotFound();
            }

            return View(brand);
        }

        // POST: Brands/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (_context.Brands == null)
            {
                return Problem("Entity set 'CosmeticStoreContext.Brands' is null.");
            }
            var brand = await _context.Brands.FindAsync(id);
            if (brand != null)
            {
                _context.Brands.Remove(brand);

                /* TÙY CHỌN: Xóa file Logo vật lý khi xóa Brand */
                /* if (!string.IsNullOrEmpty(brand.LogoUrl) && brand.LogoUrl.StartsWith("/images/brands/"))
                {
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", brand.LogoUrl.TrimStart('/'));
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }*/
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool BrandExists(int id)
        {
            return (_context.Brands?.Any(e => e.BrandId == id)).GetValueOrDefault();
        }
    }
}