using System;
using System.Collections.Generic;

namespace CosmeticStore.MVC.Models
{
    public partial class BlogPost
    {
        public int PostId { get; set; }
        public string Title { get; set; } = null!;
        public string Content { get; set; } = null!;
        public string? ThumbnailUrl { get; set; }
        public int? AuthorId { get; set; }
        public DateTime? PublishDate { get; set; }

        public virtual User? Author { get; set; }
    }
}
