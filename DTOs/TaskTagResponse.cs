namespace TaskApi.DTOs;

public sealed class TaskTagCountResponse
{
    public string Name { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Todo { get; set; }
    public int Doing { get; set; }
    public int Done { get; set; }
}

public sealed record TaskTagCatalogResponse(List<TaskTagCountResponse> Items, int TotalTasks,
    int UntaggedTasks, int RegisteredTags, int MaxRegisteredTags);

public sealed record TaskTagCreatedResponse(string Name);
