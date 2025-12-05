using System;
using System.Collections.Generic;

namespace CosmeticStore.MVC.Models
{
    public partial class User
    {
        public User()
        {
            BlogPosts = new HashSet<BlogPost>();
            Orders = new HashSet<Order>();
            Reviews = new HashSet<Review>();
        }

        public int UserId { get; set; }
        public string Email { get; set; } = null!;
        public string PasswordHash { get; set; } = null!;
        public string? FullName { get; set; }
        public string? Address { get; set; }
        public string? PhoneNumber { get; set; }
        public string Role { get; set; } = null!;
        public string? OtpCode { get; set; } // Mã OTP
        public DateTime? OtpExpiryTime { get; set; }
        public bool IsLocked { get; set; } = false;
        public virtual ICollection<BlogPost> BlogPosts { get; set; }
        public virtual ICollection<Order> Orders { get; set; }
        public virtual ICollection<Review> Reviews { get; set; }
    }
}
