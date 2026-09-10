using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TaskApi.Authentication;
using TaskApi.DTOs;
using TaskApi.Services;

namespace TaskApi.Controllers;

[ApiController]
[Route("api/teams")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeamsController(TeamService service) : ControllerBase
{
    [HttpGet]
    public IActionResult GetAll() => Execute(() => Ok(service.GetAll(UserId)));

    [HttpPost]
    public IActionResult Create(CreateTeamRequest request) => Execute(() =>
    {
        var result = service.Create(UserId, request);
        return Created($"/api/teams/{result.Team.Id}", result);
    });

    [HttpPost("join")]
    [EnableRateLimiting("auth")]
    public IActionResult Join(JoinTeamRequest request) => Execute(() => Ok(service.Join(UserId, request)));

    [HttpGet("{teamId:int}")]
    public IActionResult Get(int teamId) => Execute(() => Ok(service.Get(UserId, teamId)));

    [HttpGet("{teamId:int}/summary")]
    public IActionResult Summary(int teamId) => Execute(() => Ok(service.GetSummary(UserId, teamId)));

    [HttpPost("{teamId:int}/invites")]
    public IActionResult Invite(int teamId) => Execute(() => Ok(service.RotateInvite(UserId, teamId)));

    [HttpDelete("{teamId:int}/members/me")]
    public IActionResult Leave(int teamId) => Execute(() => { service.Leave(UserId, teamId); return NoContent(); });

    [HttpPut("{teamId:int}/owner")]
    public IActionResult TransferOwner(int teamId, TransferTeamOwnerRequest request) =>
        Execute(() => Ok(service.TransferOwner(UserId, teamId, request.UserProfileId)));

    [HttpDelete("{teamId:int}")]
    public IActionResult Delete(int teamId) => Execute(() => { service.Delete(UserId, teamId); return NoContent(); });

    private int UserId => RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext).Id;

    private IActionResult Execute(Func<IActionResult> action)
    {
        try { return action(); }
        catch (TeamOperationException error)
        { return StatusCode(error.StatusCode, new { code = error.Code, message = error.Message }); }
        catch (DbUpdateException)
        { return Conflict(new { code = "team_conflict", message = "チームが別の操作で更新されました。再読み込みしてやり直してください。" }); }
    }
}
