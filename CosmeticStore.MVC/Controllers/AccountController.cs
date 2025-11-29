using CosmeticStore.MVC.Helpers;
using CosmeticStore.MVC.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CosmeticStore.MVC.Controllers
{
    public class AccountController : Controller
    {
        private readonly CosmeticStoreContext _context;

        public AccountController(CosmeticStoreContext context)
        {
            _context = context;
        }

        // 1. ĐĂNG KÝ
        public IActionResult Register() => View();
        public IActionResult AccessDenied()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(User user)
        {
            // Kiểm tra Email đã tồn tại chưa
            if (await _context.Users.AnyAsync(u => u.Email == user.Email))
            {
                ModelState.AddModelError("Email", "Email này đã được sử dụng.");
                return View(user);
            }

            // Lưu người dùng mới (Lưu ý: Mật khẩu nên mã hóa, ở đây demo lưu thô)
            user.Role = "Customer"; // Mặc định là khách hàng
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return RedirectToAction("Login");
        }

        // 2. ĐĂNG NHẬP
        public IActionResult Login(string returnUrl = "/")
        {
            ViewData["ReturnUrl"] = returnUrl; // Lưu lại trang khách muốn vào để chuyển hướng sau khi login xong
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string email, string password, string returnUrl = "/")
        {
            // Tìm user trong DB
            var user = await _context.Users
        .FirstOrDefaultAsync(u => u.Email == email && u.PasswordHash == password);

            if (user == null)
            {
                ViewBag.Error = "Email hoặc mật khẩu không đúng.";
                return View();
            }

            // 2. Tạo Claims để đăng nhập
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.FullName ?? user.Email),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("UserId", user.UserId.ToString()), // Lưu UserID để dùng sau này
                new Claim(ClaimTypes.Role, user.Role)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, claimsPrincipal);

            var sessionCart = HttpContext.Session.Get<List<CartItem>>("Cart");

            if (sessionCart != null && sessionCart.Count > 0)
            {
                // Duyệt qua từng món trong Session để chuyển vào Database của User này
                foreach (var item in sessionCart)
                {
                    // Kiểm tra xem món này đã có trong DB của user chưa
                    var dbItem = await _context.Carts
                        .FirstOrDefaultAsync(c => c.UserId == user.UserId && c.ProductId == item.ProductId);

                    if (dbItem != null)
                    {
                        // Nếu có rồi: Cộng dồn số lượng
                        dbItem.Quantity += item.Quantity;
                    }
                    else
                    {
                        // Nếu chưa có: Tạo mới
                        var newCart = new Cart
                        {
                            UserId = user.UserId,
                            ProductId = item.ProductId,
                            Quantity = item.Quantity,
                            CreatedDate = DateTime.Now
                        };
                        _context.Carts.Add(newCart);
                    }
                }

                // Lưu thay đổi vào SQL Server
                await _context.SaveChangesAsync();

                // Xóa giỏ hàng trong Session đi (vì đã chuyển hết vào DB rồi)
                HttpContext.Session.Remove("Cart");
            }
            // ============================================================

            return Redirect(returnUrl);
        }

        // 3. ĐĂNG XUẤT
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            HttpContext.Session.Clear();
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }
        // 4. XEM LỊCH SỬ ĐƠN HÀNG
        [Authorize] // Bắt buộc phải đăng nhập
        public async Task<IActionResult> MyOrders()
        {
            // Lấy UserID của người đang đăng nhập từ Cookie
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null) return RedirectToAction("Login");

            int userId = int.Parse(userIdClaim.Value);

            // Lấy danh sách đơn hàng của User đó
            var orders = await _context.Orders
                .Where(o => o.UserId == userId)
                .OrderByDescending(o => o.OrderDate) // Mới nhất lên đầu
                .ToListAsync();

            return View(orders);
        }

        // 5. XEM CHI TIẾT ĐƠN HÀNG (Của khách)
        [Authorize]
        public async Task<IActionResult> OrderDetails(int id)
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null) return RedirectToAction("Login");
            int userId = int.Parse(userIdClaim.Value);

            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Variant) // Lấy thông tin biến thể
                .ThenInclude(v => v.Product)   // Lấy thông tin sản phẩm gốc
                .FirstOrDefaultAsync(m => m.OrderId == id && m.UserId == userId); // Quan trọng: Phải khớp UserID để không xem trộm đơn người khác

            if (order == null) return NotFound();

            return View(order);
        }
        // 1. HIỂN THỊ THÔNG TIN CÁ NHÂN
        [Authorize]
        public async Task<IActionResult> Profile()
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null) return RedirectToAction("Login");
            int userId = int.Parse(userIdClaim.Value);

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            return View(user);
        }

        // 2. CẬP NHẬT THÔNG TIN (POST)
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> UpdateProfile(User model)
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null) return RedirectToAction("Login");
            int userId = int.Parse(userIdClaim.Value);

            // Tìm user gốc trong DB
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            // Chỉ cập nhật các trường cho phép
            user.FullName = model.FullName;
            user.PhoneNumber = model.PhoneNumber;
            user.Address = model.Address;

            // Nếu muốn đổi mật khẩu thì cần logic phức tạp hơn (check pass cũ...), tạm thời bỏ qua ở đây.

            _context.Update(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Cập nhật thông tin thành công!";
            return RedirectToAction("Profile");
        }
    }
}