using System.ComponentModel.DataAnnotations;

namespace McpManager.Web.Portal.Dtos.Account;

public class ChangeEmailDto
{
    [Required(ErrorMessage = "This field is required.")]
    [EmailAddress(ErrorMessage = "The email address is not valid.")]
    [Display(Name = "New Email")]
    public string NewEmail { get; set; }
}
