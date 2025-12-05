using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CosmeticStore.MVC.Models;
using System.Linq; // Cần thiết cho các thao tác LINQ như Where, Select, Distinct

namespace CosmeticStore.MVC.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminOrdersController : Controller
    {
        private readonly CosmeticStoreContext _context;

        public AdminOrdersController(CosmeticStoreContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Hiển thị danh sách đơn hàng có hỗ trợ tìm kiếm và lọc.
        /// Tìm kiếm theo Mã ĐH, Tên KH, SĐT. Lọc theo Trạng thái.
        /// </summary>
        /// <param name="searchQuery">Từ khóa tìm kiếm (Mã ĐH, Tên KH, SĐT).</param>
        /// <param name="statusFilter">Lọc theo Trạng thái.</param>
        public async Task<IActionResult> Index(string searchQuery, string statusFilter)
        {
            // 1. Khởi tạo truy vấn cơ bản
            var ordersQuery = _context.Orders
                .Include(o => o.User)
                .OrderByDescending(o => o.OrderDate)
                .AsQueryable();

            // 2. Xử lý Lọc theo Trạng thái
            if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "All")
            {
                ordersQuery = ordersQuery.Where(o => o.Status == statusFilter);
                ViewData["StatusFilter"] = statusFilter;
            }

            // 3. Xử lý Tìm kiếm chung
            if (!string.IsNullOrEmpty(searchQuery))
            {
                string searchLower = searchQuery.ToLower().Trim();

                ordersQuery = ordersQuery.Where(o =>
                    // Tìm theo Mã ĐH
                    o.OrderId.ToString().Contains(searchLower) ||
                    // Tìm theo Tên Khách hàng
                    (o.CustomerName != null && o.CustomerName.ToLower().Contains(searchLower)) ||
                    // Tìm theo SĐT Khách hàng
                    (o.CustomerPhone != null && o.CustomerPhone.Contains(searchLower)));

                ViewData["SearchQuery"] = searchQuery; // Giữ lại từ khóa tìm kiếm
            }

            // 4. Lấy danh sách các trạng thái duy nhất để làm bộ lọc trong View
            ViewBag.Statuses = await _context.Orders.Select(o => o.Status).Distinct().ToListAsync();

            // 5. Thực thi truy vấn và trả về View
            var orders = await ordersQuery.ToListAsync();
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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int id, string status)
        {
            // 1. Lấy đơn hàng cùng chi tiết sản phẩm và biến thể để xử lý kho
            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Variant) // Load biến thể để cộng/trừ kho
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy đơn hàng!";
                return RedirectToAction(nameof(Index));
            }

            string oldStatus = order.Status; // Trạng thái cũ
            string newStatus = status;       // Trạng thái mới (Admin vừa chọn)

            if (oldStatus == newStatus)
            {
                return RedirectToAction(nameof(Details), new { id = id });
            }

            // 2. XỬ LÝ LOGIC TỒN KHO (ADVANCED)
            try
            {
                // TRƯỜNG HỢP A: HỦY ĐƠN (Cancelled) hoặc TRẢ HÀNG (Returned)
                // -> Cần cộng lại hàng vào kho
                if ((newStatus == "Cancelled" || newStatus == "Returned") && oldStatus != "Cancelled" && oldStatus != "Returned")
                {
                    foreach (var item in order.OrderDetails)
                    {
                        var variant = await _context.ProductVariants.FindAsync(item.VariantId);
                        if (variant != null)
                        {
                            variant.StockQuantity += item.Quantity; // Cộng lại kho
                        }
                    }
                }

                // TRƯỜNG HỢP B: KHÔI PHỤC ĐƠN HÀNG (Từ Cancelled/Returned -> Pending/Processing)
                // -> Cần trừ lại hàng trong kho (Nếu đủ hàng)
                else if ((oldStatus == "Cancelled" || oldStatus == "Returned") && newStatus != "Cancelled" && newStatus != "Returned")
                {
                    foreach (var item in order.OrderDetails)
                    {
                        var variant = await _context.ProductVariants.FindAsync(item.VariantId);
                        if (variant != null)
                        {
                            // Kiểm tra xem kho có đủ để khôi phục không
                            if (variant.StockQuantity < item.Quantity)
                            {
                                TempData["ErrorMessage"] = $"Không thể khôi phục đơn hàng! Sản phẩm '{variant.VariantName}' không đủ tồn kho (Còn: {variant.StockQuantity}, Cần: {item.Quantity}).";
                                // Lưu ý: Nếu không khôi phục được, không thay đổi trạng thái và thoát.
                                return RedirectToAction(nameof(Details), new { id = id });
                            }
                            variant.StockQuantity -= item.Quantity; // Trừ kho lại
                        }
                    }
                }

                // 3. Cập nhật trạng thái
                order.Status = newStatus;
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Cập nhật trạng thái đơn #{id}: {oldStatus} -> {newStatus} thành công!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Có lỗi xảy ra: " + ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id = id });
        }
    }
}