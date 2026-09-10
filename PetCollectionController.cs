using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskApi.Authentication;
using TaskApi.DTOs;
using TaskApi.Services;

namespace TaskApi.Controllers;

[ApiController]
[Route("api/pet")]
public sealed class PetCollectionController(PetCollectionService service) : ControllerBase
{
    [HttpGet("collection")] public IActionResult Get() => Execute(() => service.Get(UserId()));
    [HttpPost("rewards")] public IActionResult Claim(ClaimPetRewardRequest request) => Execute(() => service.Claim(UserId(), request));
    [HttpPut("appearance")] public IActionResult Equip(PetAppearanceRequest request) => Execute(() => service.Equip(UserId(), request));
    [HttpPost("interactions")] public IActionResult Interact(PetInteractionRequest request) => Execute(() => service.Interact(UserId(), request));
    private int UserId() => RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext).Id;
    private IActionResult Execute(Func<PetCollectionResponse> action)
    {
        try { return Ok(action()); }
        catch (TeamOperationException ex) { return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message }); }
        catch (DbUpdateException) { return Conflict(new { code = "collection_conflict", message = "別の操作で更新されました。再読み込みして確認してください。" }); }
    }
}
