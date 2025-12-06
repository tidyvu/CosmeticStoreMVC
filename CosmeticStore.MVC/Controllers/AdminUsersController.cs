using CosmeticStore.MVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CosmeticStore.MVC.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminUsersController : Controller
    {
        private readonly CosmeticStoreContext _context;

        public AdminUsersController(CosmeticStoreContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string searchString, string statusFilter)
        {
            // Lưu lại giá trị bộ lọc để hiển thị trên View
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentStatus"] = statusFilter;

            // 1. Khởi tạo Query (Load kèm Orders để tính tiền)
            var query = _context.Users
                .Include(u => u.Orders)
                //.Where(u => u.Role != "Admin") // Ẩn Admin khỏi danh sách quản lý
                .AsQueryable();

            // 2. Xử lý TÌM KIẾM TỪ KHÓA (Tên, Email, SĐT)
            if (!string.IsNullOrEmpty(searchString))
            {
                searchString = searchString.Trim().ToLower();
                query = query.Where(u =>
                    u.FullName.ToLower().Contains(searchString) ||
                    u.Email.ToLower().Contains(searchString) ||
                    u.PhoneNumber.Contains(searchString)
                );
            }

            // 3. Xử lý LỌC TRẠNG THÁI (Active / Locked)
            if (!string.IsNullOrEmpty(statusFilter))
            {
                if (statusFilter == "active")
                {
                    query = query.Where(u => u.IsLocked == false); // Lấy user Hoạt động
                }
                else if (statusFilter == "locked")
                {
                    query = query.Where(u => u.IsLocked == true); // Lấy user Đã khóa
                }
            }

            // 4. Sắp xếp: Tổng tiền chi tiêu nhiều nhất lên đầu (VIP)
            // Nếu muốn sắp xếp theo ngày tạo mới nhất thì đổi thành: .OrderByDescending(u => u.UserId)
            query = query.OrderByDescending(u => u.Orders.Sum(o => o.TotalAmount));

            var users = await query.ToListAsync();
            return View(users);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleLock(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
                user.IsLocked = !user.IsLocked;
                await _context.SaveChangesAsync();
                TempData["Success"] = user.IsLocked ? $"Đã khóa tài khoản {user.Email}." : $"Đã mở khóa tài khoản {user.Email}.";
            }
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var user = await _context.Users
                .Include(u => u.Orders) // Load lịch sử mua hàng
                .FirstOrDefaultAsync(m => m.UserId == id);

            if (user == null) return NotFound();

            return View(user);
        }

        public IActionResult Create() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("UserId,Email,PasswordHash,FullName,Address,PhoneNumber,Role,IsLocked")] User user)
        {
            if (ModelState.IsValid)
            {
                // 1. Kiểm tra Email trùng lặp
                if (await _context.Users.AnyAsync(u => u.Email == user.Email))
                {
                    ModelState.AddModelError("Email", "Email này đã tồn tại.");
                    return View(user);
                }

                // 2. Mặc định role nếu chưa chọn
                if (string.IsNullOrEmpty(user.Role)) user.Role = "Customer";

                // 3. --- MÃ HÓA MẬT KHẨU --- (QUAN TRỌNG)
                if (!string.IsNullOrEmpty(user.PasswordHash))
                {
                    // Dùng BCrypt để mã hóa chuỗi mật khẩu Admin nhập vào
                    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(user.PasswordHash);
                }
                else
                {
                    // Nếu không nhập pass -> Gán mật khẩu mặc định (Ví dụ: 123456)
                    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456");
                }

                _context.Add(user);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Tạo tài khoản mới thành công.";
                return RedirectToAction(nameof(Index));
            }
            return View(user);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();
            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("UserId,Email,PasswordHash,FullName,Address,PhoneNumber,Role,OtpCode,OtpExpiryTime,IsLocked")] User user)
        {
            if (id != user.UserId) return NotFound();

            // Loại bỏ validate password vì admin thường chỉ sửa thông tin, không sửa pass
            ModelState.Remove("PasswordHash");

            if (ModelState.IsValid)
            {
                try
                {
                    var existingUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == id);
                    if (existingUser != null)
                    {
                        // Giữ lại dữ liệu nhạy cảm cũ mà form không gửi lên
                        user.PasswordHash = existingUser.PasswordHash;
                        user.OtpCode = existingUser.OtpCode;
                        user.OtpExpiryTime = existingUser.OtpExpiryTime;

                        _context.Update(user);
                        await _context.SaveChangesAsync();
                        TempData["Success"] = "Cập nhật thông tin thành công.";
                    }
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!UserExists(user.UserId)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(user);
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var user = await _context.Users.FirstOrDefaultAsync(m => m.UserId == id);
            if (user == null) return NotFound();
            return View(user);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
                // Kiểm tra: Nếu User đã có đơn hàng thì KHÔNG cho xóa (sẽ lỗi khóa ngoại)
                var hasOrders = await _context.Orders.AnyAsync(o => o.UserId == id);
                if (hasOrders)
                {
                    TempData["Error"] = "User này đã có lịch sử mua hàng, không thể xóa vĩnh viễn! Hãy dùng chức năng KHÓA.";
                    return RedirectToAction(nameof(Index));
                }

                _context.Users.Remove(user);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Đã xóa tài khoản thành công.";
            }
            return RedirectToAction(nameof(Index));
        }

        private bool UserExists(int id)
        {
            return (_context.Users?.Any(e => e.UserId == id)).GetValueOrDefault();
        }
    }
}