using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CosmeticStore.MVC.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace CosmeticStore.MVC.Controllers
{
    // Dòng này sẽ tự động chặn tất cả người lạ và User thường.
    // Chỉ cho phép User có Claim Role là "Admin" truy cập.
    [Authorize(Roles = "Admin")]
    public class AdminReportController : Controller
    {
        private readonly CosmeticStoreContext _context;

        public AdminReportController(CosmeticStoreContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            // Không cần check IsAdmin() nữa
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetDailyRevenue()
        {
            var thirtyDaysAgo = DateTime.Now.AddDays(-30);

            var data = await _context.Orders
                .Where(o => o.OrderDate >= thirtyDaysAgo && o.Status != "Cancelled")
                .GroupBy(o => o.OrderDate.Value.Date)
                .Select(g => new
                {
                    Date = g.Key.ToString("dd/MM/yyyy"),
                    Revenue = g.Sum(o => o.TotalAmount),
                    Count = g.Count()
                })
                .ToListAsync();

            // Sắp xếp lại bằng C#
            var sortedData = data.OrderBy(x => DateTime.ParseExact(x.Date, "dd/MM/yyyy", null)).ToList();

            return Json(sortedData);
        }
        [HttpGet]
        public async Task<IActionResult> GetMonthlyRevenue()
        {
            var currentYear = DateTime.Now.Year;

            var data = await _context.Orders
                .Where(o => o.OrderDate.Value.Year == currentYear && o.Status != "Cancelled")
                .GroupBy(o => o.OrderDate.Value.Month)
                .Select(g => new
                {
                    Label = "Tháng " + g.Key,
                    Value = g.Key,
                    Revenue = g.Sum(o => o.TotalAmount),
                    Count = g.Count()
                })
                .OrderBy(x => x.Value)
                .ToListAsync();

            return Json(data);
        }

        [HttpGet]
        public async Task<IActionResult> GetYearlyRevenue()
        {
            var data = await _context.Orders
                .Where(o => o.Status != "Cancelled")
                .GroupBy(o => o.OrderDate.Value.Year)
                .Select(g => new
                {
                    Label = "Năm " + g.Key,
                    Revenue = g.Sum(o => o.TotalAmount),
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Label)
                .Take(5)
                .ToListAsync();

            return Json(data.OrderBy(x => x.Label));
        }

        [HttpGet]
        public async Task<IActionResult> GetReportDetails(string type, string timeValue)
        {
            var query = _context.Orders
                .Include(o => o.User)
                .Where(o => o.Status != "Cancelled")
                .AsQueryable();

            if (type == "daily")
            {
                if (DateTime.TryParseExact(timeValue, "dd/MM/yyyy", null, System.Globalization.DateTimeStyles.None, out DateTime date))
                {
                    query = query.Where(o => o.OrderDate.Value.Date == date.Date);
                }
            }
            else if (type == "monthly")
            {
                if (int.TryParse(timeValue.Replace("Tháng ", ""), out int month))
                {
                    int year = DateTime.Now.Year;
                    query = query.Where(o => o.OrderDate.Value.Month == month && o.OrderDate.Value.Year == year);
                }
            }
            else if (type == "yearly")
            {
                if (int.TryParse(timeValue.Replace("Năm ", ""), out int year))
                {
                    query = query.Where(o => o.OrderDate.Value.Year == year);
                }
            }

            var result = await query.Select(o => new {
                OrderId = o.OrderId,
                CustomerName = o.CustomerName ?? o.User.FullName ?? "Khách vãng lai",
                OrderDate = o.OrderDate.Value.ToString("dd/MM/yyyy HH:mm"),
                TotalAmount = o.TotalAmount,
                Status = o.Status
            }).OrderByDescending(o => o.OrderId).ToListAsync();

            return Json(result);
        }
        public async Task<IActionResult> PeriodDetails(string type, string timeValue)
        {
            

            // 1. Khởi tạo Query
            var query = _context.Orders
                .Include(o => o.User) // Load thông tin khách
                .Where(o => o.Status != "Cancelled")
                .AsQueryable();

            // 2. Xử lý lọc theo thời gian (Logic giống hệt API cũ)
            string title = "";

            if (type == "daily")
            {
                if (DateTime.TryParseExact(timeValue, "dd/MM/yyyy", null, System.Globalization.DateTimeStyles.None, out DateTime date))
                {
                    query = query.Where(o => o.OrderDate.Value.Date == date.Date);
                    title = $"Chi tiết doanh thu Ngày {timeValue}";
                }
            }
            else if (type == "monthly")
            {
                if (int.TryParse(timeValue.Replace("Tháng ", ""), out int month))
                {
                    int year = DateTime.Now.Year;
                    query = query.Where(o => o.OrderDate.Value.Month == month && o.OrderDate.Value.Year == year);
                    title = $"Chi tiết doanh thu Tháng {month}/{year}";
                }
            }
            else if (type == "yearly")
            {
                if (int.TryParse(timeValue.Replace("Năm ", ""), out int year))
                {
                    query = query.Where(o => o.OrderDate.Value.Year == year);
                    title = $"Chi tiết doanh thu Năm {year}";
                }
            }

            // 3. Lấy dữ liệu
            var orders = await query.OrderByDescending(o => o.OrderId).ToListAsync();

            // 4. Truyền dữ liệu sang View
            ViewBag.Title = title;
            ViewBag.Type = type; // Để biết đang xem loại nào (nếu cần quay lại)

            // Tính tổng phụ
            ViewBag.TotalRevenue = orders.Sum(o => o.TotalAmount);
            ViewBag.TotalCount = orders.Count;

            return View(orders);
        }
    }
}