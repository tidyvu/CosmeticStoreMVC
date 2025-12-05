using Microsoft.AspNetCore.Mvc;
using CosmeticStore.MVC.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using CosmeticStore.MVC.Helpers; // Chứa VnPayLibrary
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace CosmeticStore.MVC.Controllers
{
    public class AccountController : Controller
    {
        private readonly CosmeticStoreContext _context;
        private readonly IConfiguration _configuration;

        public AccountController(CosmeticStoreContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        // ============================================================
        // 1. ĐĂNG KÝ
        // ============================================================
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
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(user.PasswordHash);
            user.Role = "Customer";
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Đăng ký thành công! Vui lòng đăng nhập.";
            return RedirectToAction("Login");
        }

        // ============================================================
        // 2. ĐĂNG NHẬP
        // ============================================================
        public IActionResult Login(string returnUrl = "/")
        {
            ViewData["ReturnUrl"] = returnUrl; // Lưu lại trang khách muốn vào để chuyển hướng sau khi login xong
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string email, string password, string returnUrl = "/")
        {
            // Tìm user trong DB
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                ViewBag.Error = "Email hoặc mật khẩu không đúng.";
                return View();
            }

            // 2. Tạo Claims để đăng nhập
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.FullName ?? user.Email),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("UserId", user.UserId.ToString()),
                new Claim(ClaimTypes.Role, user.Role)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, claimsPrincipal);
            var sessionCart = HttpContext.Session.Get<List<CartItem>>("Cart");
            if (sessionCart != null && sessionCart.Count > 0)
            {
                foreach (var item in sessionCart)
                {
                    // Tìm xem món này (đúng ProductID VÀ VariantID) đã có trong DB chưa
                    var dbItem = await _context.Carts
                        .FirstOrDefaultAsync(c => c.UserId == user.UserId
                                               && c.ProductId == item.ProductId
                                               && c.VariantId == item.VariantId); // <--- Quan trọng: Phải khớp VariantId

                    if (dbItem != null)
                    {
                        // Nếu có rồi: Cộng dồn số lượng
                        dbItem.Quantity += item.Quantity;
                    }
                    else
                    {
                        // Nếu chưa có: Tạo mới với đầy đủ thông tin
                        var newCart = new Cart
                        {
                            UserId = user.UserId,
                            ProductId = item.ProductId,
                            VariantId = item.VariantId, // <--- Đừng quên dòng này
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

            return Redirect(returnUrl);
        }

        // ============================================================
        // 3. ĐĂNG XUẤT
        // ============================================================
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Home");
        }

        // ============================================================
        // 4. XEM LỊCH SỬ ĐƠN HÀNG
        // ============================================================
        [Authorize]
        public async Task<IActionResult> MyOrders()
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null) return RedirectToAction("Login");
            int userId = int.Parse(userIdClaim.Value);

            var orders = await _context.Orders
                .Where(o => o.UserId == userId)
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            return View(orders);
        }

        // ============================================================
        // 5. CHI TIẾT ĐƠN HÀNG
        // ============================================================
        [Authorize]
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


        // ============================================================
        // 6. HỦY ĐƠN HÀNG (Xử lý cả COD và VNPay)
        // ============================================================
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> CancelOrder(int orderId)
        {
            var userIdClaim = User.FindFirst("UserId");
            int userId = int.Parse(userIdClaim.Value);

            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(od => od.Variant)
                .FirstOrDefaultAsync(o => o.OrderId == orderId && o.UserId == userId);

            if (order == null) return NotFound();

            // Trường hợp 1: Đơn COD đang xử lý -> Hủy và HOÀN KHO
            if (order.Status == "Pending")
            {
                order.Status = "Cancelled";

                // Hoàn lại số lượng tồn kho (Vì COD đã trừ lúc đặt)
                foreach (var item in order.OrderDetails)
                {
                    var variant = await _context.ProductVariants.FindAsync(item.VariantId);
                    if (variant != null) variant.StockQuantity += item.Quantity;
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã hủy đơn hàng COD thành công.";
            }
            // Trường hợp 2: Đơn VNPay chưa thanh toán -> Hủy và KHÔNG CẦN HOÀN KHO
            else if (order.Status == "Pending Payment")
            {
                order.Status = "Cancelled";
                // Không hoàn kho vì đơn VNPay lúc đặt chưa trừ kho (chỉ trừ khi thanh toán thành công)

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã hủy đơn hàng chờ thanh toán thành công.";
            }
            else
            {
                TempData["ErrorMessage"] = "Không thể hủy đơn hàng ở trạng thái này.";
            }

            return RedirectToAction("MyOrders");
        }

        // ============================================================
        // 7. THANH TOÁN LẠI (Cho đơn Pending Payment)
        // ============================================================
        [Authorize]
        public async Task<IActionResult> RepayOrder(int orderId)
        {
            var userIdClaim = User.FindFirst("UserId");
            int userId = int.Parse(userIdClaim.Value);

            var order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderId == orderId && o.UserId == userId);

            if (order == null || order.Status != "Pending Payment")
            {
                TempData["ErrorMessage"] = "Đơn hàng không hợp lệ để thanh toán.";
                return RedirectToAction("MyOrders");
            }

            // Tạo lại URL thanh toán VNPay
            string vnpayUrl = CreateVnPayUrl(order.OrderId, order.TotalAmount);
            return Redirect(vnpayUrl);
        }

        // ============================================================
        // 8. THÔNG TIN CÁ NHÂN & CẬP NHẬT
        // ============================================================
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

        // Helper: Tạo URL VNPay (Giống bên CartController)
        private string CreateVnPayUrl(int orderId, decimal amount)
        {
            string vnp_Returnurl = "https://localhost:50587/Cart/PaymentCallback"; // Cổng 50587
            string vnp_Url = _configuration["VnPay:BaseUrl"];
            string vnp_TmnCode = _configuration["VnPay:TmnCode"];
            string vnp_HashSecret = _configuration["VnPay:HashSecret"];

            VnPayLibrary vnpay = new VnPayLibrary();
            vnpay.AddRequestData("vnp_Version", "2.1.0");
            vnpay.AddRequestData("vnp_Command", "pay");
            vnpay.AddRequestData("vnp_TmnCode", vnp_TmnCode);
            vnpay.AddRequestData("vnp_Amount", ((long)amount * 100).ToString());
            vnpay.AddRequestData("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
            vnpay.AddRequestData("vnp_CurrCode", "VND");
            vnpay.AddRequestData("vnp_IpAddr", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1");
            vnpay.AddRequestData("vnp_Locale", "vn");
            vnpay.AddRequestData("vnp_OrderInfo", "Thanh toan don hang:" + orderId);
            vnpay.AddRequestData("vnp_OrderType", "other");
            vnpay.AddRequestData("vnp_ReturnUrl", vnp_Returnurl);
            vnpay.AddRequestData("vnp_TxnRef", orderId.ToString());

            return vnpay.CreateRequestUrl(vnp_Url, vnp_HashSecret);
        }
    }
}