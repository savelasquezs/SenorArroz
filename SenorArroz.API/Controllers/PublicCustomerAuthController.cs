using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SenorArroz.API.Security;
using SenorArroz.API.Services;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = StorefrontApiKeyOptions.Scheme)]
[Route("api/public/customer-auth")]
public sealed class PublicCustomerAuthController(StorefrontCustomerAuthService auth) : ControllerBase
{
    [HttpPost("request-code")]
    [RequestSizeLimit(4 * 1024)]
    [EnableRateLimiting("storefront-auth")]
    public async Task<ActionResult<ApiResponse<StorefrontOtpRequestResult>>> RequestCode(
        [FromBody] StorefrontOtpRequest request,
        CancellationToken ct)
    {
        try
        {
            var clientIp = Request.Headers["X-Storefront-Client-IP"].FirstOrDefault()
                ?? HttpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown";
            var result = await auth.RequestCodeAsync(request.Phone, clientIp, ct);
            return Ok(ApiResponse<StorefrontOtpRequestResult>.SuccessResponse(result));
        }
        catch (StorefrontAuthInvalidPhoneException)
        {
            return BadRequest(ApiResponse<StorefrontOtpRequestResult>.ErrorResponse("Ingresa un celular colombiano válido de 10 dígitos."));
        }
        catch (StorefrontAuthRateLimitException ex)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, ApiResponse<StorefrontOtpRequestResult>.ErrorResponse(ex.Message));
        }
        catch (StorefrontAuthUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse<StorefrontOtpRequestResult>.ErrorResponse(ex.Message));
        }
    }

    [HttpPost("verify-code")]
    [RequestSizeLimit(4 * 1024)]
    [EnableRateLimiting("storefront-auth")]
    public async Task<ActionResult<ApiResponse<StorefrontOtpVerificationResult>>> VerifyCode(
        [FromBody] StorefrontOtpVerifyRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await auth.VerifyCodeAsync(request.ChallengeId, request.Code, ct);
            return Ok(ApiResponse<StorefrontOtpVerificationResult>.SuccessResponse(result));
        }
        catch (StorefrontAuthInvalidCodeException)
        {
            return BadRequest(ApiResponse<StorefrontOtpVerificationResult>.ErrorResponse("El código no es válido o ya venció."));
        }
        catch (StorefrontAuthUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse<StorefrontOtpVerificationResult>.ErrorResponse(ex.Message));
        }
    }

    [HttpGet("session")]
    [EnableRateLimiting("storefront-auth")]
    public async Task<ActionResult<ApiResponse<StorefrontCustomerSessionResult>>> Session(CancellationToken ct)
    {
        try
        {
            var result = await auth.GetSessionAsync(Request.Headers["X-Storefront-Customer-Session"].FirstOrDefault(), ct);
            return Ok(ApiResponse<StorefrontCustomerSessionResult>.SuccessResponse(result));
        }
        catch (StorefrontAuthInvalidSessionException)
        {
            return Unauthorized(ApiResponse<StorefrontCustomerSessionResult>.ErrorResponse("La sesión del cliente no es válida o venció."));
        }
        catch (StorefrontAuthUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse<StorefrontCustomerSessionResult>.ErrorResponse(ex.Message));
        }
    }
}

public sealed class StorefrontOtpRequest
{
    [Required, StringLength(20)]
    public string Phone { get; set; } = string.Empty;
}

public sealed class StorefrontOtpVerifyRequest
{
    public Guid ChallengeId { get; set; }

    [Required, StringLength(6, MinimumLength = 6), RegularExpression(@"^\d{6}$")]
    public string Code { get; set; } = string.Empty;
}
