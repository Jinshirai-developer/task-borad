using System.ComponentModel.DataAnnotations;

namespace TaskApi.DTOs;

public sealed class UserPreferencesResponse
{
    public string Theme { get; set; } = "classic";
    public string Layout { get; set; } = "board";
}

public sealed class UpdateUserPreferencesRequest
{
    [Required, RegularExpression("^(classic|retro|light|dark|forest|sunset)$", ErrorMessage = "対応しているテーマを選んでください。")]
    public string Theme { get; set; } = string.Empty;

    [Required, RegularExpression("^(board|list|compact|gallery|focus)$", ErrorMessage = "対応しているレイアウトを選んでください。")]
    public string Layout { get; set; } = string.Empty;
}
