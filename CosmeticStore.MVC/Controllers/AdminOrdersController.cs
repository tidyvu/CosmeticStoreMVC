using ClosedXML.Excel;
using CosmeticStore.MVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        // ... (Các hàm khác)

        /// <summary>
        /// Trang in hóa đơn đóng gói (Không có Layout Admin)
        /// </summary>
        public async Task<IActionResult> PrintInvoice(int id)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                    .ThenInclude(od => od.Variant)
                        .ThenInclude(v => v.Product)
                .FirstOrDefaultAsync(m => m.OrderId == id);

            if (order == null) return NotFound();

            return View(order);
        }
        /// <summary>
        /// Xuất chi tiết đơn hàng ra file Excel (.xlsx)
        /// </summary>
        /// <param name="id">Mã đơn hàng</param>
        public async Task<IActionResult> ExportOrderToExcel(int id)
        {
            // 1. Lấy dữ liệu đơn hàng (Giống hệt hàm Details)
            var order = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Variant)
                .ThenInclude(v => v.Product)
                .FirstOrDefaultAsync(m => m.OrderId == id);

            if (order == null) return NotFound();

            // 2. Khởi tạo Workbook (File Excel)
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("ChiTietDonHang");

                // --- PHẦN 1: THÔNG TIN CHUNG ---
                // Tiêu đề
                worksheet.Cell(1, 1).Value = "CHI TIẾT ĐƠN HÀNG";
                worksheet.Range(1, 1, 1, 6).Merge().Style.Font.SetBold().Font.SetFontSize(16).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                // Thông tin khách hàng & Đơn hàng
                worksheet.Cell(3, 1).Value = "Mã đơn hàng:";
                worksheet.Cell(3, 2).Value = "#" + order.OrderId;

                worksheet.Cell(4, 1).Value = "Ngày đặt:";
                worksheet.Cell(4, 2).Value = order.OrderDate; // Excel tự format ngày
                worksheet.Cell(5, 1).Value = "Khách hàng:";
                worksheet.Cell(5, 2).Value = order.CustomerName ?? "Khách vãng lai";

                worksheet.Cell(6, 1).Value = "Số điện thoại:";
                worksheet.Cell(6, 2).Value = "'" + order.CustomerPhone; // Thêm dấu ' để giữ số 0 đầu

                worksheet.Cell(7, 1).Value = "Địa chỉ:";
                worksheet.Cell(7, 2).Value = order.ShippingAddress;

                worksheet.Cell(8, 1).Value = "Trạng thái:";
                worksheet.Cell(8, 2).Value = order.Status;

                // --- PHẦN 2: BẢNG SẢN PHẨM ---
                int row = 10; // Bắt đầu từ dòng 10

                // Header bảng
                worksheet.Cell(row, 1).Value = "STT";
                worksheet.Cell(row, 2).Value = "Tên sản phẩm";
                worksheet.Cell(row, 3).Value = "Phân loại";
                worksheet.Cell(row, 4).Value = "Số lượng";
                worksheet.Cell(row, 5).Value = "Đơn giá";
                worksheet.Cell(row, 6).Value = "Thành tiền";

                // Style Header bảng
                var headerRange = worksheet.Range(row, 1, row, 6);
                headerRange.Style.Font.SetBold();
                headerRange.Style.Fill.SetBackgroundColor(XLColor.LightGray);
                headerRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);

                // Dữ liệu bảng
                row++;
                int stt = 1;
                decimal totalAmount = 0;

                foreach (var item in order.OrderDetails)
                {
                    worksheet.Cell(row, 1).Value = stt++;
                    worksheet.Cell(row, 2).Value = item.Variant?.Product?.ProductName ?? "Sản phẩm đã xóa";
                    worksheet.Cell(row, 3).Value = item.Variant?.VariantName ?? "-";
                    worksheet.Cell(row, 4).Value = item.Quantity;
                    worksheet.Cell(row, 5).Value = item.UnitPrice;
                    worksheet.Cell(row, 6).Value = item.Quantity * item.UnitPrice;

                    // Định dạng tiền tệ (Ví dụ: 100,000)
                    worksheet.Cell(row, 5).Style.NumberFormat.Format = "#,##0";
                    worksheet.Cell(row, 6).Style.NumberFormat.Format = "#,##0";

                    totalAmount += (item.Quantity * item.UnitPrice);
                    row++;
                }

                // --- PHẦN 3: TỔNG KẾT ---
                worksheet.Cell(row, 5).Value = "TỔNG CỘNG:";
                worksheet.Cell(row, 5).Style.Font.SetBold();

                worksheet.Cell(row, 6).Value = totalAmount; // Hoặc order.TotalAmount nếu bạn lưu sẵn
                worksheet.Cell(row, 6).Style.Font.SetBold().Font.SetFontColor(XLColor.Red);
                worksheet.Cell(row, 6).Style.NumberFormat.Format = "#,##0";

                // Tự động căn chỉnh độ rộng cột
                worksheet.Columns().AdjustToContents();

                // 3. Xuất file ra stream
                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    string fileName = $"DonHang_{order.OrderId}_{DateTime.Now:yyyyMMdd}.xlsx";

                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
            }
        }
    }
}