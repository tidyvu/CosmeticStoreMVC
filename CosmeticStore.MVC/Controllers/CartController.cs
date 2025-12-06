using ClosedXML.Excel;      // Cần cho xuất Excel
using CosmeticStore.MVC.Helpers; // Chứa VnPayLibrary & SessionHelper
using CosmeticStore.MVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;            // Cần cho MemoryStream
using System.Linq;
using System.Threading.Tasks;

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
                    .Include(c => c.Product).Include(c => c.ProductVariant)
                    .Where(c => c.UserId == userId).ToListAsync();

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
                var dbItem = await _context.Carts.FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == productId && c.VariantId == variantId);

                if (dbItem != null)
                {
                    if (currentStock < (dbItem.Quantity + quantity)) TempData["ErrorMessage"] = $"Kho không đủ. Bạn đã có {dbItem.Quantity} trong giỏ.";
                    else dbItem.Quantity += quantity;
                }
                else
                {
                    _context.Carts.Add(new Cart { UserId = userId, ProductId = productId, VariantId = variantId, Quantity = quantity, CreatedDate = DateTime.Now });
                }
                await _context.SaveChangesAsync();
            }
            else
            {
                var cart = HttpContext.Session.Get<List<CartItem>>("Cart") ?? new List<CartItem>();
                var existingItem = cart.FirstOrDefault(x => x.ProductId == productId && x.VariantId == variantId);
                if (existingItem != null)
                {
                    if (currentStock < (existingItem.Quantity + quantity)) TempData["ErrorMessage"] = "Kho không đủ hàng.";
                    else existingItem.Quantity += quantity;
                }
                else
                {
                    var product = await _context.Products.FindAsync(productId);
                    cart.Add(new CartItem { ProductId = variant.ProductId, VariantId = variant.VariantId, ProductName = product?.ProductName, VariantName = variant.VariantName, ImageUrl = product?.MainImageUrl, Price = GetEffectivePrice(variant), Quantity = quantity });
                }
                HttpContext.Session.Set("Cart", cart);
            }

            if (TempData["ErrorMessage"] == null) TempData["SuccessMessage"] = "Đã thêm vào giỏ hàng!";
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
            if (variant == null) return Json(new { success = false, message = "SP không tồn tại." });
            int currentStock = variant.StockQuantity;
            if (currentStock < quantity) return Json(new { success = false, message = $"Kho chỉ còn {currentStock}.", newQuantity = currentStock });

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
                    itemTotalPrice = dbItem.Quantity * GetEffectivePrice(dbItem.ProductVariant);
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
            return Json(new { success = true, grandTotal, newTotalPrice = itemTotalPrice, newQuantity = quantity });
        }

        // ==========================================================
        // 4. XÓA SẢN PHẨM
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
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return Json(new { success = true, grandTotal, itemCount });
                return RedirectToAction("Index");
            }
            catch { return RedirectToAction("Index"); }
        }

        // ==========================================================
        // 5. TRANG THANH TOÁN (GET)
        // ==========================================================
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Checkout()
        {
            int userId = GetUserId();
            var dbCarts = await _context.Carts.Include(c => c.Product).Include(c => c.ProductVariant).Where(c => c.UserId == userId).ToListAsync();
            if (dbCarts.Count == 0)
            {
                TempData["ErrorMessage"] = "Giỏ hàng trống.";
                return RedirectToAction("Index");
            }
            var cartItems = dbCarts.Select(c => new CartItem { ProductId = c.ProductId, VariantId = c.VariantId, ProductName = c.Product?.ProductName, VariantName = c.ProductVariant?.VariantName, ImageUrl = c.Product?.MainImageUrl, Price = GetEffectivePrice(c.ProductVariant), Quantity = c.Quantity }).ToList();
            ViewBag.User = await _context.Users.FindAsync(userId);
            return View(cartItems);
        }

        // ==========================================================
        // 6. XỬ LÝ ĐẶT HÀNG (POST)
        // ==========================================================
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Checkout(string customerName, string phone, string address, string paymentMethod)
        {
            if (string.IsNullOrEmpty(customerName) || string.IsNullOrEmpty(phone) || string.IsNullOrEmpty(address))
            {
                TempData["ErrorMessage"] = "Thiếu thông tin giao hàng.";
                return RedirectToAction("Checkout");
            }

            int userId = GetUserId();
            using (var transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    var dbCarts = await _context.Carts.Include(c => c.ProductVariant).Where(c => c.UserId == userId).ToListAsync();
                    if (dbCarts.Count == 0) return RedirectToAction("Index");

                    decimal totalAmount = dbCarts.Sum(c => c.Quantity * GetEffectivePrice(c.ProductVariant));

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

                    foreach (var item in dbCarts)
                    {
                        var variant = await _context.ProductVariants.FindAsync(item.VariantId);
                        if (variant == null) throw new Exception("Lỗi sản phẩm.");
                        if (variant.StockQuantity < item.Quantity) throw new Exception($"'{variant.VariantName}' hết hàng.");

                        if (paymentMethod == "COD") variant.StockQuantity -= item.Quantity;

                        _context.OrderDetails.Add(new OrderDetail { OrderId = order.OrderId, VariantId = item.VariantId, Quantity = item.Quantity, UnitPrice = GetEffectivePrice(item.ProductVariant) });
                    }

                    if (paymentMethod == "COD") _context.Carts.RemoveRange(dbCarts);

                    await _context.SaveChangesAsync();
                    transaction.Commit();

                    if (paymentMethod == "VNPay") return Redirect(CreateVnPayUrl(order.OrderId, totalAmount));

                    TempData["SuccessMessage"] = "Đặt hàng thành công!";
                    return RedirectToAction("OrderSuccess", new { id = order.OrderId });
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    TempData["ErrorMessage"] = "Lỗi: " + ex.Message;
                    return RedirectToAction("Checkout");
                }
            }
        }

        // ==========================================================
        // 7. PAYMENT CALLBACK (VNPAY)
        // ==========================================================
        [HttpGet]
        public async Task<IActionResult> PaymentCallback()
        {
            if (Request.Query.Count == 0) return RedirectToAction("Index");
            var response = _configuration.GetSection("VnPay");
            var vnpay = new VnPayLibrary();
            foreach (var s in Request.Query) if (s.Key.StartsWith("vnp_")) vnpay.AddResponseData(s.Key, s.Value);

            long orderId = Convert.ToInt64(vnpay.GetResponseData("vnp_TxnRef"));
            string vnp_ResponseCode = vnpay.GetResponseData("vnp_ResponseCode");
            string vnp_SecureHash = Request.Query["vnp_SecureHash"];

            if (vnpay.ValidateSignature(vnp_SecureHash, response["HashSecret"]))
            {
                var order = await _context.Orders.Include(o => o.OrderDetails).FirstOrDefaultAsync(o => o.OrderId == orderId);
                if (order == null) return RedirectToAction("Index");

                if (vnp_ResponseCode == "00")
                {
                    if (order.Status == "Pending Payment")
                    {
                        order.Status = "Paid";
                        foreach (var detail in order.OrderDetails)
                        {
                            var variant = await _context.ProductVariants.FindAsync(detail.VariantId);
                            if (variant != null) variant.StockQuantity = Math.Max(0, variant.StockQuantity - detail.Quantity);
                        }
                        var userCarts = _context.Carts.Where(c => c.UserId == order.UserId);
                        _context.Carts.RemoveRange(userCarts);
                        await _context.SaveChangesAsync();
                    }
                    TempData["SuccessMessage"] = "Thanh toán thành công!";
                    return RedirectToAction("OrderSuccess", new { id = order.OrderId });
                }
                else
                {
                    if (order.OrderDetails != null) _context.OrderDetails.RemoveRange(order.OrderDetails);
                    _context.Orders.Remove(order);
                    await _context.SaveChangesAsync();
                    TempData["ErrorMessage"] = "Thanh toán thất bại.";
                    return RedirectToAction("Index");
                }
            }
            return RedirectToAction("Index");
        }

        // ==========================================================
        // 8. TRANG SUCCESS (HIỂN THỊ CHI TIẾT)
        // ==========================================================
        [Authorize]
        public async Task<IActionResult> OrderSuccess(int id)
        {
            int userId = GetUserId();
            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(od => od.Variant).ThenInclude(pv => pv.Product)
                .FirstOrDefaultAsync(o => o.OrderId == id && o.UserId == userId);
            if (order == null) return RedirectToAction("Index", "Home");
            return View(order);
        }

        // ==========================================================
        // 9. XUẤT EXCEL
        // ==========================================================
        [Authorize]
        public async Task<IActionResult> ExportOrderToExcel(int id)
        {
            int userId = GetUserId();
            var order = await _context.Orders.Include(o => o.OrderDetails).ThenInclude(od => od.Variant).ThenInclude(pv => pv.Product).FirstOrDefaultAsync(o => o.OrderId == id && o.UserId == userId);
            if (order == null) return NotFound();

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("DonHang_" + order.OrderId);
                worksheet.Cell(1, 1).Value = "CHI TIẾT ĐƠN HÀNG #" + order.OrderId;
                worksheet.Range(1, 1, 1, 6).Merge().Style.Font.SetBold().Font.SetFontSize(16).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                worksheet.Cell(3, 1).Value = "Khách hàng:"; worksheet.Cell(3, 2).Value = order.CustomerName;
                worksheet.Cell(4, 1).Value = "SĐT:"; worksheet.Cell(4, 2).Value = "'" + order.CustomerPhone;
                worksheet.Cell(5, 1).Value = "Địa chỉ:"; worksheet.Cell(5, 2).Value = order.ShippingAddress;
                worksheet.Cell(6, 1).Value = "Ngày đặt:"; worksheet.Cell(6, 2).Value = order.OrderDate;

                int row = 8;
                string[] headers = { "STT", "Tên SP", "Phân loại", "SL", "Đơn giá", "Thành tiền" };
                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(row, i + 1).Value = headers[i];
                    worksheet.Cell(row, i + 1).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#f7e6e9")).Font.SetBold().Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                }

                row++; int stt = 1;
                foreach (var item in order.OrderDetails)
                {
                    worksheet.Cell(row, 1).Value = stt++;
                    worksheet.Cell(row, 2).Value = item.Variant?.Product?.ProductName;
                    worksheet.Cell(row, 3).Value = item.Variant?.VariantName;
                    worksheet.Cell(row, 4).Value = item.Quantity;
                    worksheet.Cell(row, 5).Value = item.UnitPrice;
                    worksheet.Cell(row, 6).Value = item.Quantity * item.UnitPrice;
                    worksheet.Cell(row, 5).Style.NumberFormat.Format = "#,##0";
                    worksheet.Cell(row, 6).Style.NumberFormat.Format = "#,##0";
                    row++;
                }

                worksheet.Cell(row, 5).Value = "TỔNG CỘNG:";
                worksheet.Cell(row, 5).Style.Font.SetBold();
                worksheet.Cell(row, 6).Value = order.TotalAmount;
                worksheet.Cell(row, 6).Style.Font.SetBold().Font.SetFontColor(XLColor.FromHtml("#d14d5a")).NumberFormat.Format = "#,##0";
                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"DonHang_{order.OrderId}.xlsx");
                }
            }
        }

        // HELPERS
        private string CreateVnPayUrl(int orderId, decimal amount)
        {
            // LƯU Ý: Đổi port 50587 thành port thực tế của bạn nếu cần
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
    }
}