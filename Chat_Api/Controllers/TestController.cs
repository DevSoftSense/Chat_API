using Chat.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chat_Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class TestController : ControllerBase
{
    private readonly DatabaseHelper _databaseHelper;

    public TestController(DatabaseHelper databaseHelper)
    {
        _databaseHelper = databaseHelper;
    }

    /// <summary>
    /// Verifies tenant Transaction DB connectivity using SoftOnCloud Product Connection
    /// (resolvedConnectionString) with the caller's JWT.
    /// </summary>
    [Authorize]
    [HttpGet("connection")]
    public async Task<IActionResult> CheckConnection(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _databaseHelper.GetDefaultConnectionAsync(cancellationToken);
            await connection.OpenAsync(cancellationToken);

            return Ok(new
            {
                message = "PostgreSQL database connected successfully"
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = "Database connection failed",
                error = ex.Message
            });
        }
    }
}
