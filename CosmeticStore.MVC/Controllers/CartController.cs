using Microsoft.AspNetCore.Mvc;
using CosmeticStore.MVC.Models;
using CosmeticStore.MVC.Helpers; // Cần thiết cho Session Extensions
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace CosmeticStore.MVC.Controllers
{
    public class CartController : Controller
    {
        private readonly CosmeticStoreContext _context;

        public CartController(CosmeticStoreContext context)
        {
            _context = context;
        }

        // GET: /Cart/Index
        public IActionResult Index()
        {
            var cart = HttpContext.Session.Get<List<CartItem>>("Cart") ?? new List<CartItem>();
            return View(cart);
        }

        // POST/GET: /Cart/AddToCart?productId=1&variantId=5&quantity=1
        public async Task<IActionResult> AddToCart(int productId, int variantId, int quantity = 1)
        {
            if (quantity <= 0) quantity = 1;

            var cart = HttpContext.Session.Get<List<CartItem>>("Cart") ?? new List<CartItem>();

            // 1. Tìm kiếm item trong giỏ hàng (phải khớp cả ProductId và VariantId)
            var existingItem = cart.FirstOrDefault(x => x.ProductId == productId && x.VariantId == variantId);

            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
            }
            else
            {
                // 2. Lấy thông tin Variant và Product từ DB
                var variant = await _context.ProductVariants
                    .Include(v => v.Product) // PHẢI include Product để lấy tên, ảnh
                    .FirstOrDefaultAsync(v => v.VariantId == variantId && v.ProductId == productId);

                if (variant == null || variant.Product == null)
                {
                    TempData["ErrorMessage"] = "Không tìm thấy sản phẩm hoặc phân loại.";
                    return RedirectToAction("Details", "Products", new { id = productId });
                }

                var item = new CartItem
                {
                    ProductId = variant.ProductId,
                    VariantId = variant.VariantId,          // Lưu VariantId
                    VariantName = variant.VariantName,      // Lưu VariantName
                    ProductName = variant.Product.ProductName,
                    ImageUrl = variant.Product.MainImageUrl, // Lấy ảnh chính từ Product
                    Price = variant.Price,
                    Quantity = quantity
                };
                cart.Add(item);
            }

            HttpContext.Session.Set("Cart", cart);
            TempData["SuccessMessage"] = $"Đã thêm {quantity} sản phẩm vào giỏ hàng.";

            return RedirectToAction("Index");
        }

        // POST: /Cart/UpdateQuantity?productId=1&variantId=5&quantity=2
        // Xử lý AJAX để cập nhật số lượng
        [HttpPost]
        public IActionResult UpdateQuantity(int productId, int variantId, int quantity)
        {
            var cart = HttpContext.Session.Get<List<CartItem>>("Cart") ?? new List<CartItem>();
            var item = cart.FirstOrDefault(x => x.ProductId == productId && x.VariantId == variantId);

            if (item == null)
            {
                return Json(new { success = false, message = "Không tìm thấy sản phẩm trong giỏ." });
            }

            if (quantity <= 0)
            {
                // Nếu số lượng <= 0, ta chuyển sang xóa sản phẩm
                return RemoveConfirmed(productId, variantId);
            }

            // Cập nhật số lượng
            item.Quantity = quantity;

            // Tính toán giá trị mới
            var newTotalPrice = item.TotalPrice;
            var grandTotal = cart.Sum(x => x.TotalPrice);

            HttpContext.Session.Set("Cart", cart);

            // Trả về JSON để View cập nhật
            return Json(new
            {
                success = true,
                newQuantity = item.Quantity,
                newTotalPrice = newTotalPrice,
                grandTotal = grandTotal,
                message = "Cập nhật thành công."
            });
        }


        // POST: /Cart/Remove?productId=1&variantId=5
        // Xử lý AJAX để xóa sản phẩm
        [HttpPost]
        public IActionResult Remove(int productId, int variantId)
        {
            return RemoveConfirmed(productId, variantId);
        }

        // Logic xóa sản phẩm (dùng chung cho cả AJAX và GET cũ)
        private IActionResult RemoveConfirmed(int productId, int variantId)
        {
            var cart = HttpContext.Session.Get<List<CartItem>>("Cart") ?? new List<CartItem>();

            // Tìm item dựa trên cả ProductId và VariantId
            var itemToRemove = cart.FirstOrDefault(x => x.ProductId == productId && x.VariantId == variantId);

            if (itemToRemove != null)
            {
                cart.Remove(itemToRemove);
                HttpContext.Session.Set("Cart", cart);

                // Tính toán tổng tiền và số lượng item còn lại
                var grandTotal = cart.Sum(x => x.TotalPrice);
                var itemCount = cart.Count;

                // Trả về JSON cho AJAX
                return Json(new
                {
                    success = true,
                    grandTotal = grandTotal,
                    itemCount = itemCount,
                    message = "Xóa sản phẩm thành công."
                });
            }

            // Nếu không tìm thấy, trả về JSON lỗi
            return Json(new { success = false, message = "Không tìm thấy sản phẩm cần xóa." });
        }


        // 1. Hiển thị form điền thông tin (Bắt buộc đăng nhập)
        [Authorize]
        [HttpGet]
        public IActionResult Checkout()
        {
            var cart = HttpContext.Session.Get<List<CartItem>>("Cart") ?? new List<CartItem>();

            if (cart.Count == 0) return RedirectToAction("Index");

            // Lấy thông tin người dùng để điền sẵn vào form
            // (Bạn cần tùy chỉnh phần này dựa trên User Model của bạn)
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
            {
                var user = _context.Users.FirstOrDefault(u => u.UserId == userId);
                ViewBag.User = user;
            }

            return View(cart);
        }

        // 2. Xử lý lưu đơn hàng khi bấm nút "Xác nhận"
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Checkout(string customerName, string phone, string address)
        {
            var cart = HttpContext.Session.Get<List<CartItem>>("Cart") ?? new List<CartItem>();
            if (cart.Count == 0) return RedirectToAction("Index");

            var userIdClaim = User.FindFirst("UserId");
            int? userId = userIdClaim != null ? int.Parse(userIdClaim.Value) : null;

            // A. TẠO ĐƠN HÀNG (ORDER)
            var order = new Order
            {
                UserId = userId,
                CustomerName = customerName,
                CustomerPhone = phone,
                ShippingAddress = address,
                OrderDate = DateTime.Now,
                TotalAmount = cart.Sum(x => x.TotalPrice),
                Status = "Pending"
            };

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // B. TẠO CHI TIẾT ĐƠN (ORDER DETAILS) VÀ CẬP NHẬT KHO
            foreach (var item in cart)
            {
                var orderDetail = new OrderDetail
                {
                    OrderId = order.OrderId,
                    VariantId = item.VariantId, // Sử dụng VariantId đã lưu
                    Quantity = item.Quantity,
                    UnitPrice = item.Price
                };
                _context.OrderDetails.Add(orderDetail);

                // CẬP NHẬT KHO
                var variant = await _context.ProductVariants.FirstOrDefaultAsync(v => v.VariantId == item.VariantId);
                if (variant != null)
                {
                    // Đảm bảo số lượng tồn kho không âm
                    variant.StockQuantity = Math.Max(0, variant.StockQuantity - item.Quantity);
                }
            }

            await _context.SaveChangesAsync();

            // C. XÓA GIỎ HÀNG & CHUYỂN HƯỚNG
            HttpContext.Session.Remove("Cart");
            TempData["SuccessMessage"] = "Đơn hàng của bạn đã được đặt thành công!";

            return RedirectToAction("OrderSuccess");
        }

        // 3. Trang thông báo thành công
        public IActionResult OrderSuccess()
        {
            return View();
        }
    }
}