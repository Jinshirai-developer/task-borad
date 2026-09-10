
namespace TaskApi.DTOs
{
    public class PagedResponse<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount {get; set;}
        public int Page{get; set;}
        public int PageSize {get; set;}
        public int TotalPages {get; set;}
        // Counts after filters, before pagination. Never infer workspace totals from Items.
        public TaskStatusCounts? StatusCounts { get; set; }
    }
    public sealed record TaskStatusCounts(int Todo, int Doing, int Done);
}
