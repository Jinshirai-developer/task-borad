using System.ComponentModel.DataAnnotations;

namespace TaskApi.DTOs;

public sealed class CreateTaskTagRequest
{
    [Required, MaxLength(300)]
    public string Name { get; set; } = string.Empty;
}
