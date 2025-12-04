using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CosmeticStore.MVC.Models; // Đảm bảo đúng namespace model của bạn

namespace CosmeticStore.MVC.Services
{
    public class OrderCleanupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;

        public OrderCleanupService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Tạo scope mới để gọi DbContext (vì BackgroundService là Singleton)
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var context = scope.ServiceProvider.GetRequiredService<CosmeticStoreContext>();

                        // 1. Tìm các đơn hàng "Pending Payment" đã quá 1 tiếng
                        var timeout = DateTime.Now.AddMilliseconds(-5); // Lấy mốc thời gian cách đây 1 tiếng

                        var expiredOrders = await context.Orders
                            .Include(o => o.OrderDetails) // Load chi tiết để xóa cho sạch
                            .Where(o => o.Status == "Pending Payment" && o.OrderDate < timeout)
                            .ToListAsync(stoppingToken);

                        if (expiredOrders.Any())
                        {
                            foreach (var order in expiredOrders)
                            {
                                // A. Xóa chi tiết đơn hàng trước (Tránh lỗi khóa ngoại)
                                if (order.OrderDetails != null && order.OrderDetails.Any())
                                {
                                    context.OrderDetails.RemoveRange(order.OrderDetails);
                                }

                                // B. Xóa đơn hàng chính
                                context.Orders.Remove(order);
                            }

                            // Lưu thay đổi vào DB
                            await context.SaveChangesAsync(stoppingToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Ghi log lỗi nếu cần (Console.WriteLine(ex.Message));
                }

                // Chờ 1 phút rồi quét lại (60.000ms)
                await Task.Delay(60000, stoppingToken);
            }
        }
    }
}