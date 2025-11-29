using CosmeticStore.MVC.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace CosmeticStore.MVC.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly CosmeticStoreContext _context;
        public HomeController(ILogger<HomeController> logger, CosmeticStoreContext context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // 3. Lấy 8 sản phẩm mới nhất (Sắp xếp theo ProductId giảm dần)
            // Include luôn biến thể để lấy giá
            var newProducts = await _context.Products
                .Include(p => p.ProductVariants)
                .OrderByDescending(p => p.ProductId)
                .Take(8)
                .ToListAsync();

            return View(newProducts);
        }

        public IActionResult Privacy()
        {
            return View();
        }
        public IActionResult StoreLocator()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
