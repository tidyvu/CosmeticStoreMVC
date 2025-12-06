using Microsoft.AspNetCore.Mvc;
using CosmeticStore.MVC.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using CosmeticStore.MVC.Helpers;
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

namespace CosmeticStore.MVC.Controllers
{
    public class AccountController : Controller
    {
        private readonly CosmeticStoreContext _context;
        private readonly IConfiguration _configuration;
        private readonly EmailService _emailService;

        public AccountController(CosmeticStoreContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
            _emailService = new EmailService(configuration);
        }

        public IActionResult Register() => View();

        public IActionResult AccessDenied() => View();

        [HttpPost]
        public async Task<IActionResult> Register(User user)
        {
            if (await _context.Users.AnyAsync(u => u.Email == user.Email))
            {
                ModelState.AddModelError("Email", "Email này đã được sử dụng.");
                return View(user);
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(user.PasswordHash);
            user.Role = "Customer";
            user.IsLocked = false; // Mặc định không khóa

            // Tạo OTP
            string otp = new Random().Next(100000, 999999).ToString();

            // Lưu User và OTP vào Session
            HttpContext.Session.SetString("PendingUser", JsonConvert.SerializeObject(user));
            HttpContext.Session.SetString("RegisterOTP", otp);

            // Gửi Email
            string subject = "[CosmeticStore] Mã xác thực đăng ký";
            string body = $"Chào bạn,<br/>Mã xác thực (OTP) của bạn là: <b style='color:red; font-size:20px'>{otp}</b>.<br/>Mã này có hiệu lực trong phiên làm việc hiện tại.";

            try
            {
                await _emailService.SendEmailAsync(user.Email, subject, body);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Lỗi gửi mail: " + ex.Message);
                return View(user);
            }

            TempData["SuccessMessage"] = "Mã xác thực đã được gửi đến Email của bạn.";
            return RedirectToAction("VerifyRegister");
        }

        [HttpGet]
        public IActionResult VerifyRegister() => View();

        [HttpPost]
        public async Task<IActionResult> VerifyRegister(string otp)
        {
            var sessionOtp = HttpContext.Session.GetString("RegisterOTP");
            var pendingUserJson = HttpContext.Session.GetString("PendingUser");

            if (string.IsNullOrEmpty(sessionOtp) || string.IsNullOrEmpty(pendingUserJson))
            {
                TempData["ErrorMessage"] = "Phiên giao dịch hết hạn. Vui lòng đăng ký lại.";
                return RedirectToAction("Register");
            }

            if (otp == sessionOtp)
            {
                var user = JsonConvert.DeserializeObject<User>(pendingUserJson);
                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                HttpContext.Session.Remove("RegisterOTP");
                HttpContext.Session.Remove("PendingUser");

                TempData["SuccessMessage"] = "Đăng ký thành công! Vui lòng đăng nhập.";
                return RedirectToAction("Login");
            }
            else
            {
                ViewBag.Error = "Mã OTP không chính xác.";
                return View();
            }
        }

        public IActionResult Login(string returnUrl = "/")
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string email, string password, string returnUrl = "/")
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

            // 1. Kiểm tra Email và Pass
            if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                ViewBag.Error = "Email hoặc mật khẩu không đúng.";
                return View();
            }

            // 2. Kiểm tra tài khoản bị khóa
            if (user.IsLocked)
            {
                ViewBag.Error = "Tài khoản của bạn đã bị khóa vi phạm chính sách. Vui lòng liên hệ Admin.";
                return View();
            }

            // 3. Tạo Claims cho Cookie Auth (Dùng cho [Authorize])
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

            // 4. [MỚI] Lưu Session (Dùng cho AdminUsersController kiểm tra quyền)
            HttpContext.Session.SetString("UserID", user.UserId.ToString());
            HttpContext.Session.SetString("UserEmail", user.Email);
            HttpContext.Session.SetString("UserRole", user.Role ?? "Customer"); // Bắt buộc để Admin vào được trang quản lý
            HttpContext.Session.SetString("UserName", user.FullName ?? user.Email);

            // 5. Xử lý giỏ hàng từ Session vào DB
            var sessionCart = HttpContext.Session.Get<List<CartItem>>("Cart");
            if (sessionCart != null && sessionCart.Count > 0)
            {
                foreach (var item in sessionCart)
                {
                    var dbItem = await _context.Carts
                        .FirstOrDefaultAsync(c => c.UserId == user.UserId
                                               && c.ProductId == item.ProductId
                                               && c.VariantId == item.VariantId);

                    if (dbItem != null)
                    {
                        dbItem.Quantity += item.Quantity;
                    }
                    else
                    {
                        var newCart = new Cart
                        {
                            UserId = user.UserId,
                            ProductId = item.ProductId,
                            VariantId = item.VariantId,
                            Quantity = item.Quantity,
                            CreatedDate = DateTime.Now
                        };
                        _context.Carts.Add(newCart);
                    }
                }
                await _context.SaveChangesAsync();
                HttpContext.Session.Remove("Cart");
            }

            // 6. Điều hướng thông minh (Nếu là Admin thì vào trang Admin luôn)
            if (user.Role == "Admin" && returnUrl == "/")
            {
                return RedirectToAction("Index", "AdminUsers");
            }

            return Redirect(returnUrl);
        }

        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear(); // Xóa sạch Session
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult ForgotPassword() => View();

        [HttpPost]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                ViewBag.Error = "Email không tồn tại trong hệ thống.";
                return View();
            }

            if (user.IsLocked)
            {
                ViewBag.Error = "Tài khoản này đang bị khóa, không thể đặt lại mật khẩu.";
                return View();
            }

            string otp = new Random().Next(100000, 999999).ToString();
            HttpContext.Session.SetString("ResetEmail", email);
            HttpContext.Session.SetString("ResetOTP", otp);

            string body = $"Chào {user.FullName},<br/>Mã OTP đặt lại mật khẩu của bạn là: <b style='color:blue'>{otp}</b>";
            await _emailService.SendEmailAsync(email, "Yêu cầu đặt lại mật khẩu", body);

            TempData["SuccessMessage"] = "Đã gửi mã OTP đến email của bạn.";
            return RedirectToAction("VerifyForgotPassword");
        }

        [HttpGet]
        public IActionResult VerifyForgotPassword() => View();

        [HttpPost]
        public IActionResult VerifyForgotPassword(string otp)
        {
            var sessionOtp = HttpContext.Session.GetString("ResetOTP");
            if (sessionOtp != null && sessionOtp == otp)
            {
                HttpContext.Session.SetString("IsOtpVerified", "true");
                return RedirectToAction("ResetPassword");
            }

            ViewBag.Error = "Mã OTP không đúng hoặc đã hết hạn.";
            return View();
        }

        [HttpGet]
        public IActionResult ResetPassword()
        {
            if (HttpContext.Session.GetString("IsOtpVerified") != "true") return RedirectToAction("ForgotPassword");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(string newPassword, string confirmPassword)
        {
            if (HttpContext.Session.GetString("IsOtpVerified") != "true") return RedirectToAction("ForgotPassword");

            if (newPassword != confirmPassword)
            {
                ViewBag.Error = "Mật khẩu xác nhận không khớp.";
                return View();
            }

            string email = HttpContext.Session.GetString("ResetEmail");
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user != null)
            {
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
                await _context.SaveChangesAsync();

                HttpContext.Session.Remove("ResetEmail");
                HttpContext.Session.Remove("ResetOTP");
                HttpContext.Session.Remove("IsOtpVerified");

                TempData["SuccessMessage"] = "Đặt lại mật khẩu thành công. Vui lòng đăng nhập.";
                return RedirectToAction("Login");
            }
            return View();
        }

        [Authorize]
        [HttpGet]
        public IActionResult ChangePassword() => View();

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null) return RedirectToAction("Login");

            int userId = int.Parse(userIdClaim.Value);
            var user = await _context.Users.FindAsync(userId);

            if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
            {
                ViewBag.Error = "Mật khẩu hiện tại không đúng.";
                return View();
            }

            if (newPassword != confirmPassword)
            {
                ViewBag.Error = "Mật khẩu mới không khớp.";
                return View();
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Đổi mật khẩu thành công!";
            return RedirectToAction("Profile");
        }

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
            int userId = int.Parse(userIdClaim.Value);
            var user = await _context.Users.FindAsync(userId);

            if (user == null) return NotFound();

            user.FullName = model.FullName;
            user.PhoneNumber = model.PhoneNumber;
            user.Address = model.Address;

            _context.Update(user);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Cập nhật thông tin thành công!";
            return RedirectToAction("Profile");
        }


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

        [Authorize]
        public async Task<IActionResult> OrderDetails(int id)
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null) return RedirectToAction("Login");
            int userId = int.Parse(userIdClaim.Value);

            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(od => od.Variant).ThenInclude(v => v.Product)
                .FirstOrDefaultAsync(m => m.OrderId == id && m.UserId == userId);

            if (order == null) return NotFound();
            return View(order);
        }

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

            if (order.Status == "Pending") // COD -> Hoàn kho
            {
                order.Status = "Cancelled";
                foreach (var item in order.OrderDetails)
                {
                    var variant = await _context.ProductVariants.FindAsync(item.VariantId);
                    if (variant != null) variant.StockQuantity += item.Quantity;
                }
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã hủy đơn hàng COD thành công.";
            }
            else if (order.Status == "Pending Payment") // VNPay chưa trả -> Hủy, ko hoàn kho
            {
                order.Status = "Cancelled";
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã hủy đơn hàng chờ thanh toán thành công.";
            }
            else
            {
                TempData["ErrorMessage"] = "Không thể hủy đơn hàng ở trạng thái này.";
            }
            return RedirectToAction("MyOrders");
        }

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
            string vnpayUrl = CreateVnPayUrl(order.OrderId, order.TotalAmount);
            return Redirect(vnpayUrl);
        }

        private string CreateVnPayUrl(int orderId, decimal amount)
        {
            string vnp_Returnurl = "https://localhost:50587/Cart/PaymentCallback";
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