using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskApi.Authentication;
using TaskApi.DTOs;
using TaskApi.Services;

namespace TaskApi.Controllers;

[ApiController]
[Route("api/pet")]
public class PetController : ControllerBase
{
    private readonly PetService _petService;

    public PetController(PetService petService)
    {
        _petService = petService;
    }

    [HttpGet]
    public IActionResult Get()
    {
        try { return Ok(_petService.GetProfile(GetUserId())); }
        catch (TeamOperationException exception)
        {
            return StatusCode(exception.StatusCode, new { code = exception.Code, message = exception.Message });
        }
    }

    [HttpPut]
    public IActionResult Update([FromBody] UpdatePetRequest request)
    {
        try
        {
            return Ok(_petService.UpdateProfile(GetUserId(), request));
        }
        catch (TeamOperationException exception)
        {
            return StatusCode(exception.StatusCode, new { code = exception.Code, message = exception.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                title: "Pet update conflict",
                detail: "別の操作で育成データが更新されました。再読み込みしてからもう一度お試しください。",
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private int GetUserId()
    {
        return RequireAuthenticationAttribute.GetAuthenticatedUser(HttpContext).Id;
    }
}
