using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CosmeticStore.MVC.Models;
using System;
using System.Linq;

namespace CosmeticStore.MVC.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly CosmeticStoreContext _context;

        public AdminController(CosmeticStoreContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // 1. Tính tổng số đơn hàng
            ViewBag.TotalOrders = await _context.Orders.CountAsync();

            // Lọc các đơn hàng đã hoàn thành (hoặc chưa hủy) và đảm bảo OrderDate không NULL
            var completedOrdersQuery = _context.Orders
                .Where(o => o.Status != "Cancelled" && o.OrderDate.HasValue);

            // 2. Tính tổng doanh thu tích lũy
            ViewBag.TotalRevenue = await completedOrdersQuery.SumAsync(o => o.TotalAmount);

            // 3. Tổng số sản phẩm
            ViewBag.TotalProducts = await _context.Products.CountAsync();

            // 4. Tổng số khách hàng (giả sử có trường Role)
            ViewBag.TotalCustomers = await _context.Users.CountAsync(u => u.Role == "Customer");


            // Bước quan trọng: Kéo dữ liệu cần thiết (Date và Amount) của các đơn hàng hợp lệ vào bộ nhớ
            // (EF Core chỉ dịch được Where và Select ở đây)
            var chartData = await completedOrdersQuery
                .Select(o => new { Date = o.OrderDate.Value, Amount = o.TotalAmount })
                .ToListAsync(); // <-- Dữ liệu được kéo về và lưu trong bộ nhớ

            // a. Doanh thu theo Ngày trong Tuần
            var dailyRevenueQuery = chartData
                .GroupBy(o => o.Date.DayOfWeek) // <-- Grouping client-side
                .Select(g => new
                {
                    Day = g.Key,
                    Total = g.Sum(o => o.Amount)
                })
                .ToList();

            // Sắp xếp và điền 0 cho những ngày không có đơn (Thứ Hai -> Chủ Nhật)
            var dailyRevenueData = new decimal[7];
            foreach (var data in dailyRevenueQuery)
            {
                // DayOfWeek.Monday = 1, ..., Sunday = 0.
                // Chuyển đổi: Monday (1) -> index 0, ..., Sunday (0) -> index 6.
                int index = (int)data.Day == 0 ? 6 : (int)data.Day - 1;
                dailyRevenueData[index] = data.Total;
            }
            ViewBag.DailyRevenueData = dailyRevenueData;


            // b. Doanh thu theo Tháng trong Năm hiện tại
            var currentYear = DateTime.Now.Year;
            var monthlyRevenueQuery = chartData
                .Where(o => o.Date.Year == currentYear) // Filtering client-side
                .GroupBy(o => o.Date.Month) // Grouping client-side
                .Select(g => new
                {
                    Month = g.Key,
                    Total = g.Sum(o => o.Amount)
                })
                .ToList();

            // Đảm bảo có đủ 12 tháng, những tháng không có dữ liệu là 0
            var monthlyRevenueData = new decimal[12];
            foreach (var data in monthlyRevenueQuery)
            {
                monthlyRevenueData[data.Month - 1] = data.Total; // Month 1 -> index 0
            }
            ViewBag.MonthlyRevenueData = monthlyRevenueData;


            // c. Doanh thu theo Từng Năm (từ trước đến nay)
            var yearlyRevenueQuery = chartData
                .GroupBy(o => o.Date.Year) // Grouping client-side
                .Select(g => new
                {
                    Year = g.Key,
                    Total = g.Sum(o => o.Amount)
                })
                .OrderBy(x => x.Year)
                .ToList();

            ViewBag.YearlyRevenueData = yearlyRevenueQuery.Select(x => x.Total).ToList();
            ViewBag.YearlyLabels = yearlyRevenueQuery.Select(x => x.Year.ToString()).ToList();


            // 6. Lấy 5 đơn hàng mới nhất để hiển thị nhanh
            var recentOrders = await _context.Orders
                .OrderByDescending(o => o.OrderDate)
                .Take(5)
                .ToListAsync();

            return View(recentOrders);
        }
    }
}