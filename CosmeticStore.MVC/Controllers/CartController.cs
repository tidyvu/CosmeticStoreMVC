using Microsoft.AspNetCore.Mvc;
using CosmeticStore.MVC.Models;
using CosmeticStore.MVC.Helpers; // Chứa VnPayLibrary & SessionHelper
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace CosmeticStore.MVC.Controllers
{
    public class CartController : Controller
    {
        private readonly CosmeticStoreContext _context;
        private readonly IConfiguration _configuration;

        public CartController(CosmeticStoreContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        // ==========================================================
        // 1. HIỂN THỊ GIỎ HÀNG (INDEX)
        // ==========================================================
        public async Task<IActionResult> Index()
        {
            var cartItems = new List<CartItem>();

            if (User.Identity.IsAuthenticated)
            {
                int userId = GetUserId();
                var dbCarts = await _context.Carts
                    .Include(c => c.Product)
                    .Include(c => c.ProductVariant)
                    .Where(c => c.UserId == userId)
                    .ToListAsync();

                foreach (var c in dbCarts)
                {
                    if (c.ProductVariant != null)
                    {
                        cartItems.Add(new CartItem
                        {
                            ProductId = c.ProductId,
                            VariantId = c.VariantId,
                            ProductName = c.Product?.ProductName,
                            VariantName = c.ProductVariant.VariantName,
                            ImageUrl = c.Product?.MainImageUrl,
                            Price = GetEffectivePrice(c.ProductVariant),
                            Quantity = c.Quantity
                        });
                    }
                }
            }
            else
            {
                cartItems = HttpContext.Session.Get<List<CartItem>>("Cart") ?? new List<CartItem>();
            }

            return View(cartItems);
        }

        // ==========================================================
        // 2. THÊM VÀO GIỎ
        // ==========================================================
        public async Task<IActionResult> AddToCart(int productId, int variantId, int quantity = 1)
        {
            if (quantity <= 0) quantity = 1;

            var variant = await _context.ProductVariants.FindAsync(variantId);
            if (variant == null) return NotFound();

            // SỬA LỖI: Đã bỏ "?? 0" vì StockQuantity là int
            int currentStock = variant.StockQuantity;

            if (currentStock < quantity)
            {
                TempData["ErrorMessage"] = $"Sản phẩm này chỉ còn {currentStock} món.";
                string referer = Request.Headers["Referer"].ToString();
                return !string.IsNullOrEmpty(referer) ? Redirect(referer) : RedirectToAction("Index");
            }

            if (User.Identity.IsAuthenticated)
            {
                int userId = GetUserId();
                var dbItem = await _context.Carts
                    .FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == productId && c.VariantId == variantId);

                if (dbItem != null)
                {
                    if (currentStock < (dbItem.Quantity + quantity))
                    {
                        TempData["ErrorMessage"] = $"Kho không đủ. Bạn đã có {dbItem.Quantity} trong giỏ.";
                    }
                    else
                    {
                        dbItem.Quantity += quantity;
                    }
                }
                else
                {
                    var newCart = new Cart
                    {
                        UserId = userId,
                        ProductId = productId,
                        VariantId = variantId,
                        Quantity = quantity,
                        CreatedDate = DateTime.Now
                    };
                    _context.Carts.Add(newCart);
                }
                await _context.SaveChangesAsync();
            }
            else
            {
                var cart = HttpContext.Session.Get<List<CartItem>>("Cart") ?? new List<CartItem>();
                var existingItem = cart.FirstOrDefault(x => x.ProductId == productId && x.VariantId == variantId);

                if (existingItem != null)
                {
                    if (currentStock < (existingItem.Quantity + quantity))
                    {
                        TempData["ErrorMessage"] = $"Kho không đủ hàng.";
                    }
                    else
                    {
                        existingItem.Quantity += quantity;
                    }
                }
                else
                {
                    var product = await _context.Products.FindAsync(productId);
                    cart.Add(new CartItem
                    {
                        ProductId = variant.ProductId,
                        VariantId = variant.VariantId,
                        ProductName = product?.ProductName,
                        VariantName = variant.VariantName,
                        ImageUrl = product?.MainImageUrl,
                        Price = GetEffectivePrice(variant),
                        Quantity = quantity
                    });
                }
                HttpContext.Session.Set("Cart", cart);
            }

            if (TempData["ErrorMessage"] == null)
                TempData["SuccessMessage"] = "Đã thêm vào giỏ hàng!";

            string urlReferer = Request.Headers["Referer"].ToString();
            return !string.IsNullOrEmpty(urlReferer) ? Redirect(urlReferer) : RedirectToAction("Index");
        }

        // ==========================================================
        // 3. CẬP NHẬT SỐ LƯỢNG (AJAX)
        // ==========================================================
        [HttpPost]
        public async Task<IActionResult> UpdateQuantity(int productId, int variantId, int quantity)
        {
            if (quantity <= 0) return await Remove(productId, variantId);

            var variant = await _context.ProductVariants.FindAsync(variantId);
            if (variant == null) return Json(new { success = false, message = "Sản phẩm không tồn tại." });

            // SỬA LỖI: Đã bỏ "?? 0"
            int currentStock = variant.StockQuantity;

            if (currentStock < quantity)
            {
                return Json(new { success = false, message = $"Kho chỉ còn {currentStock} sản phẩm.", newQuantity = currentStock });
            }

            decimal grandTotal = 0;
            decimal itemTotalPrice = 0;

            if (User.Identity.IsAuthenticated)
            {
                int userId = GetUserId();
                var dbItem = await _context.Carts.FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == productId && c.VariantId == variantId);

                if (dbItem != null)
                {
                    dbItem.Quantity = quantity;
                    await _context.SaveChangesAsync();

                    var currentCart = await _context.Carts.Include(c => c.ProductVariant).Where(c => c.UserId == userId).ToListAsync();
                    grandTotal = currentCart.Sum(c => c.Quantity * GetEffectivePrice(c.ProductVariant));

                    var currentItem = currentCart.FirstOrDefault(c => c.ProductId == productId && c.VariantId == variantId);
                    if (currentItem != null)
                    {
                        itemTotalPrice = currentItem.Quantity * GetEffectivePrice(currentItem.ProductVariant);
                    }
                }
            }
            else
            {
                var cart = HttpContext.Session.Get<List<CartItem>>("Cart");
                var item = cart?.FirstOrDefault(x => x.ProductId == productId && x.VariantId == variantId);
                if (item != null)
                {
                    item.Quantity = quantity;
                    HttpContext.Session.Set("Cart", cart);
                    grandTotal = cart.Sum(x => x.TotalPrice);
                    itemTotalPrice = item.TotalPrice;
                }
            }

            return Json(new
            {
                success = true,
                grandTotal = grandTotal,
                newTotalPrice = itemTotalPrice,
                newQuantity = quantity
            });
        }

        // ==========================================================
        // 4. XÓA SẢN PHẨM (AJAX)
        // ==========================================================
        public async Task<IActionResult> Remove(int productId, int variantId)
        {
            try
            {
                decimal grandTotal = 0;
                int itemCount = 0;

                if (User.Identity.IsAuthenticated)
                {
                    int userId = GetUserId();
                    if (userId == 0) return RedirectToAction("Login", "Account");

                    var dbItem = await _context.Carts.FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == productId && c.VariantId == variantId);
                    if (dbItem != null)
                    {
                        _context.Carts.Remove(dbItem);
                        await _context.SaveChangesAsync();
                    }

                    var currentCart = await _context.Carts.Include(c => c.ProductVariant).Where(c => c.UserId == userId).ToListAsync();
                    grandTotal = currentCart.Sum(c => c.Quantity * GetEffectivePrice(c.ProductVariant));
                    itemCount = currentCart.Count;
                }
                else
                {
                    var cart = HttpContext.Session.Get<List<CartItem>>("Cart");
                    if (cart != null)
                    {
                        cart.RemoveAll(x => x.ProductId == productId && x.VariantId == variantId);
                        HttpContext.Session.Set("Cart", cart);
                        grandTotal = cart.Sum(x => x.TotalPrice);
                        itemCount = cart.Count;
                    }
                }

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = true, grandTotal = grandTotal, itemCount = itemCount });
                }

                return RedirectToAction("Index");
            }
            catch
            {
                return RedirectToAction("Index");
            }
        }

        // ==========================================================
        // 5. TRANG THANH TOÁN (GET)
        // ==========================================================
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Checkout()
        {
            int userId = GetUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            var dbCarts = await _context.Carts
                .Include(c => c.Product)
                .Include(c => c.ProductVariant)
                .Where(c => c.UserId == userId)
                .ToListAsync();

            if (dbCarts.Count == 0)
            {
                TempData["ErrorMessage"] = "Giỏ hàng trống.";
                return RedirectToAction("Index");
            }

            var cartItems = dbCarts.Select(c => new CartItem
            {
                ProductId = c.ProductId,
                VariantId = c.VariantId,
                ProductName = c.Product?.ProductName,
                VariantName = c.ProductVariant?.VariantName,
                ImageUrl = c.Product?.MainImageUrl,
                Price = GetEffectivePrice(c.ProductVariant),
                Quantity = c.Quantity
            }).ToList();

            var user = await _context.Users.FindAsync(userId);
            ViewBag.User = user;

            return View(cartItems);
        }

        // ==========================================================
        // 6. XỬ LÝ ĐẶT HÀNG (POST) - CHUẨN LOGIC VNPAY
        // ==========================================================
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Checkout(string customerName, string phone, string address, string paymentMethod)
        {
            if (string.IsNullOrEmpty(customerName) || string.IsNullOrEmpty(phone) || string.IsNullOrEmpty(address))
            {
                TempData["ErrorMessage"] = "Vui lòng nhập đầy đủ thông tin giao hàng.";
                return RedirectToAction("Checkout");
            }

            int userId = GetUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            using (var transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    var dbCarts = await _context.Carts
                        .Include(c => c.ProductVariant)
                        .Where(c => c.UserId == userId)
                        .ToListAsync();

                    if (dbCarts.Count == 0) return RedirectToAction("Index");

                    decimal totalAmount = dbCarts.Sum(c => c.Quantity * GetEffectivePrice(c.ProductVariant));

                    // 1. Tạo đơn hàng
                    var order = new Order
                    {
                        UserId = userId,
                        CustomerName = customerName,
                        CustomerPhone = phone,
                        ShippingAddress = address,
                        OrderDate = DateTime.Now,
                        TotalAmount = totalAmount,
                        Status = paymentMethod == "VNPay" ? "Pending Payment" : "Pending"
                    };

                    _context.Orders.Add(order);
                    await _context.SaveChangesAsync();

                    // 2. Tạo chi tiết đơn
                    foreach (var item in dbCarts)
                    {
                        var variant = await _context.ProductVariants.FindAsync(item.VariantId);

                        // Kiểm tra kho cơ bản
                        if (variant == null) throw new Exception("Sản phẩm không tồn tại.");

                        // SỬA LỖI: Bỏ "?? 0"
                        int currentStock = variant.StockQuantity;

                        if (currentStock < item.Quantity)
                        {
                            throw new Exception($"Sản phẩm '{variant.VariantName}' hiện không đủ hàng.");
                        }

                        // QUAN TRỌNG: Chỉ trừ kho nếu là COD
                        if (paymentMethod == "COD")
                        {
                            variant.StockQuantity = currentStock - item.Quantity;
                        }

                        var orderDetail = new OrderDetail
                        {
                            OrderId = order.OrderId,
                            VariantId = item.VariantId,
                            Quantity = item.Quantity,
                            UnitPrice = GetEffectivePrice(item.ProductVariant)
                        };
                        _context.OrderDetails.Add(orderDetail);
                    }

                    // QUAN TRỌNG: Chỉ xóa giỏ hàng nếu là COD
                    if (paymentMethod == "COD")
                    {
                        _context.Carts.RemoveRange(dbCarts);
                    }

                    await _context.SaveChangesAsync();
                    transaction.Commit();

                    // 4. Điều hướng
                    if (paymentMethod == "VNPay")
                    {
                        // Giỏ hàng vẫn còn, Kho chưa trừ -> Chuyển sang VNPay
                        var vnpayUrl = CreateVnPayUrl(order.OrderId, totalAmount);
                        return Redirect(vnpayUrl);
                    }
                    else
                    {
                        // COD: Đã trừ kho, đã xóa giỏ -> Xong
                        TempData["SuccessMessage"] = "Đặt hàng thành công!";
                        return RedirectToAction("OrderSuccess");
                    }
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    TempData["ErrorMessage"] = "Lỗi đặt hàng: " + ex.Message;
                    return RedirectToAction("Checkout");
                }
            }
        }

        // ==========================================================
        // 7. PAYMENT CALLBACK (VNPAY) - XỬ LÝ KẾT QUẢ
        // ==========================================================
        [HttpGet]
        public async Task<IActionResult> PaymentCallback()
        {
            var response = _configuration.GetSection("VnPay");
            if (Request.Query.Count == 0) return RedirectToAction("Index");

            var vnpay = new VnPayLibrary();
            foreach (var s in Request.Query)
            {
                if (!string.IsNullOrEmpty(s.Key) && s.Key.StartsWith("vnp_"))
                {
                    vnpay.AddResponseData(s.Key, s.Value);
                }
            }

            long orderId = Convert.ToInt64(vnpay.GetResponseData("vnp_TxnRef"));
            string vnp_ResponseCode = vnpay.GetResponseData("vnp_ResponseCode");
            string vnp_SecureHash = Request.Query["vnp_SecureHash"];

            bool checkSignature = vnpay.ValidateSignature(vnp_SecureHash, response["HashSecret"]);

            if (checkSignature)
            {
                // Load đơn hàng tạm
                var order = await _context.Orders
                    .Include(o => o.OrderDetails)
                    .FirstOrDefaultAsync(o => o.OrderId == orderId);

                if (order == null) return RedirectToAction("Index");

                // CHỈ KHI LÀ "00" MỚI COI LÀ THÀNH CÔNG
                if (vnp_ResponseCode == "00")
                {
                    // === THÀNH CÔNG: LƯU ĐƠN, TRỪ KHO, XÓA GIỎ ===
                    if (order.Status == "Pending Payment")
                    {
                        order.Status = "Paid"; // Đã thanh toán

                        // 1. Trừ kho (Lúc này mới trừ)
                        foreach (var detail in order.OrderDetails)
                        {
                            var variant = await _context.ProductVariants.FindAsync(detail.VariantId);
                            if (variant != null)
                            {
                                // SỬA LỖI: Bỏ "?? 0"
                                int stock = variant.StockQuantity;
                                if (stock >= detail.Quantity)
                                    variant.StockQuantity = stock - detail.Quantity;
                                else
                                    variant.StockQuantity = 0; // Hết hàng
                            }
                        }

                        // 2. Xóa giỏ hàng của User
                        int? userId = order.UserId;
                        if (userId != null)
                        {
                            var userCarts = _context.Carts.Where(c => c.UserId == userId);
                            _context.Carts.RemoveRange(userCarts);
                        }

                        await _context.SaveChangesAsync();
                    }
                    TempData["SuccessMessage"] = "Thanh toán VNPay thành công!";
                    return RedirectToAction("OrderSuccess");
                }
                else
                {
                    // === THẤT BẠI: XÓA SẠCH ĐƠN HÀNG TẠM ===
                    // Giúp Database sạch sẽ, coi như khách chưa từng đặt

                    // 1. Xóa chi tiết đơn hàng trước (Để tránh lỗi FK)
                    if (order.OrderDetails != null && order.OrderDetails.Any())
                    {
                        _context.OrderDetails.RemoveRange(order.OrderDetails);
                    }

                    // 2. Xóa đơn hàng chính
                    _context.Orders.Remove(order);

                    await _context.SaveChangesAsync();

                    // Giỏ hàng vẫn còn nguyên (vì chưa xóa ở bước Checkout)
                    TempData["ErrorMessage"] = "Giao dịch thanh toán đã bị hủy hoặc thất bại. Giỏ hàng của bạn vẫn còn nguyên.";
                    return RedirectToAction("Index"); // Quay về trang giỏ hàng
                }
            }
            else
            {
                TempData["ErrorMessage"] = "Lỗi bảo mật chữ ký số.";
                return RedirectToAction("Index");
            }
        }

        // ==========================================================
        // HELPER FUNCTIONS
        // ==========================================================

        private string CreateVnPayUrl(int orderId, decimal amount)
        {
            // URL chuẩn cho port 50587
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

        private int GetUserId()
        {
            var claim = User.FindFirst("UserId");
            return (claim != null && int.TryParse(claim.Value, out int id)) ? id : 0;
        }

        private decimal GetEffectivePrice(ProductVariant variant)
        {
            if (variant == null) return 0;
            return variant.SalePrice > 0 ? (decimal)variant.SalePrice : variant.Price;
        }

        public IActionResult OrderSuccess()
        {
            return View();
        }
    }
}