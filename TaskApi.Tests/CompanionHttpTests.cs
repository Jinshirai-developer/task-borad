using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TaskApi.Tests;

public sealed class CompanionHttpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Browser_style_text_body_is_rejected_but_explicit_json_is_saved(bool teamScope)
    {
        using var factory=new TaskApiFactory();using var client=factory.CreateSecureClient();
        await factory.RegisterConfirmedAsync(client);
        string tasks="/api/tasks",workRoot="/api/companion";
        if(teamScope)
        {
            var createdTeam=await client.SendWithCsrfAsync(HttpMethod.Post,"/api/teams",new{name="Transport regression"});
            var teamId=(await createdTeam.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("team").GetProperty("id").GetInt32();
            tasks=$"/api/teams/{teamId}/tasks";workRoot=$"/api/teams/{teamId}/companion";
        }
        var task=await (await client.SendWithCsrfAsync(HttpMethod.Post,tasks,new{title="Browser transport"})).Content.ReadFromJsonAsync<JsonElement>();
        var path=$"{workRoot}/tasks/{task.GetProperty("id").GetInt32()}";
        var work=await client.GetFromJsonAsync<JsonElement>(path);
        var body=new {action=teamScope?"help_open":"savepoint_save",kind="review",message="Please help",nextStep="Continue here",
            version=work.GetProperty("version").GetUInt32(),expectedUpdatedAt=work.GetProperty("updatedAt").GetString()};
        var token=(await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf")).GetProperty("token").GetString();
        using var textRequest=new HttpRequestMessage(HttpMethod.Post,path){Content=new StringContent(JsonSerializer.Serialize(body))};
        textRequest.Headers.Add("X-CSRF-TOKEN",token);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType,(await client.SendAsync(textRequest)).StatusCode);
        var unchanged=await client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(work.GetRawText(),unchanged.GetRawText());
        var saved=await client.SendWithCsrfAsync(HttpMethod.Post,path,body);
        Assert.Equal(HttpStatusCode.OK,saved.StatusCode);
        var result=await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(teamScope?"Please help":"Continue here",teamScope?result.GetProperty("help").GetProperty("message").GetString():result.GetProperty("savepoint").GetProperty("nextStep").GetString());
    }

    [Fact] public async Task Companion_requires_authentication_and_CSRF_and_roundtrips_private_bookmark()
    {
        using var factory = new TaskApiFactory(); using var client = factory.CreateSecureClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/companion")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/companion/notes")).StatusCode);
        await factory.RegisterConfirmedAsync(client);
        var created = await client.SendWithCsrfAsync(HttpMethod.Post, "/api/tasks", new { title = "HTTP companion" });
        var task = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = task.GetProperty("id").GetInt32();
        var path = $"/api/companion/tasks/{id}";
        var work = await client.GetFromJsonAsync<JsonElement>(path);
        var body = new { action = "savepoint_save", nextStep = "Next action", version = work.GetProperty("version").GetUInt32(), expectedUpdatedAt = work.GetProperty("updatedAt").GetString() };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, body)).StatusCode);
        var saved = await client.SendWithCsrfAsync(HttpMethod.Post, path, body);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Contains("no-store", saved.Headers.CacheControl!.ToString());
        var result = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Next action", result.GetProperty("savepoint").GetProperty("nextStep").GetString());
        Assert.False(result.TryGetProperty("savepoints", out _));
        Assert.DoesNotContain("Next action", await client.GetStringAsync($"/api/tasks/{id}"));
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendWithCsrfAsync(HttpMethod.Post, path, body)).StatusCode);
    }

    [Fact] public async Task Companion_model_validation_and_private_task_scope_cannot_be_bypassed()
    {
        using var factory = new TaskApiFactory(); using var first = factory.CreateSecureClient(); using var second = factory.CreateSecureClient();
        await factory.RegisterConfirmedAsync(first); await factory.RegisterConfirmedAsync(second);
        var created = await first.SendWithCsrfAsync(HttpMethod.Post, "/api/tasks", new { title = "Private" });
        var task = await created.Content.ReadFromJsonAsync<JsonElement>(); var path = $"/api/companion/tasks/{task.GetProperty("id").GetInt32()}";
        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.SendWithCsrfAsync(HttpMethod.Post, path, new { action = "savepoint_save", nextStep = new string('x',401) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.GetAsync("/api/companion/notes?q=" + new string('x',201))).StatusCode);
    }
}
