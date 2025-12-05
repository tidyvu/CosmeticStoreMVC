using CosmeticStore.MVC.Models;
using CosmeticStore.MVC.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<CosmeticStoreContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("CosmeticContext")));
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied"; // <--- THÊM DÒNG NÀY
        options.ExpireTimeSpan = TimeSpan.FromDays(1);
    });
builder.Services.AddHostedService<OrderCleanupService>();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
//var supportedCultures = new[] { "vi-VN" };
//var localizationOptions = new RequestLocalizationOptions()
    //.SetDefaultCulture("vi-VN")
    //.AddSupportedCultures(supportedCultures)
    //.AddSupportedUICultures(supportedCultures);

//app.UseRequestLocalization(localizationOptions);
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
