using System.ComponentModel.DataAnnotations;

namespace TaskApi.DTOs;

public class UpdatePetRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MinLength(1)]
    [RegularExpression("^(dog|cat|rabbit|fox|panda|dragon)$", ErrorMessage = "対応しているペットの種類を選んでください。")]
    public string? Species { get; set; }
}
