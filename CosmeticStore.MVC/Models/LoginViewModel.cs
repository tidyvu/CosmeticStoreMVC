using System.ComponentModel.DataAnnotations;

namespace CosmeticStore.MVC.Models
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập Email.")]
        [EmailAddress]
        public string Email { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
        public string Password { get; set; }

        public string? ReturnUrl { get; set; }
    }
}