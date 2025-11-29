using CosmeticStore.MVC.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

public class BlogController : Controller
{
    private readonly CosmeticStoreContext _context;
    public BlogController(CosmeticStoreContext context) { _context = context; }

    // Danh sách bài viết
    public async Task<IActionResult> Index()
    {
        return View(await _context.BlogPosts.OrderByDescending(p => p.PublishDate).ToListAsync());
    }

    // Chi tiết bài viết
    public async Task<IActionResult> Details(int id)
    {
        var post = await _context.BlogPosts.FindAsync(id);
        if (post == null) return NotFound();
        return View(post);
    }
}