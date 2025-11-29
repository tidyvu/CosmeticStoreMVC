using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CosmeticStore.MVC.Models;

namespace CosmeticStore.MVC.Controllers
{
    // CHỈ ADMIN MỚI ĐƯỢC VÀO
    [Authorize(Roles = "Admin")]
    public class AdminOrdersController : Controller
    {
        private readonly CosmeticStoreContext _context;

        public AdminOrdersController(CosmeticStoreContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var orders = await _context.Orders
                .OrderByDescending(o => o.OrderDate) 
                .ToListAsync();
            return View(orders);
        }

        // GET: AdminOrders/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var order = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Variant)
                .ThenInclude(v => v.Product)  
                .FirstOrDefaultAsync(m => m.OrderId == id);

            if (order == null) return NotFound();

            return View(order);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStatus(int orderId, string status)
        {
            var order = await _context.Orders.FindAsync(orderId);
            if (order != null)
            {
                order.Status = status;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Details), new { id = orderId });
        }
    }
}