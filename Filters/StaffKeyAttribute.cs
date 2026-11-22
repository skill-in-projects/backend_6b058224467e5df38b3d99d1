using System.Security.Cryptography;
using System.Text;
using Backend.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Backend.Filters;

/// <summary>
/// Staff endpoints need the X-Staff-Key header to match the CLINIC_STAFF_KEY environment variable. There is no default
/// key: without the variable the staff screens are closed (503), never open. Patients never need it.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class StaffKeyAttribute : Attribute, IAuthorizationFilter
{
    public const string Header = "X-Staff-Key";
    public const string Variable = "CLINIC_STAFF_KEY";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var expected = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(expected))
        {
            context.Result = new ObjectResult(new ApiError("staff_key_not_set", "The staff key is not set up on the server yet.")) { StatusCode = 503 };
            return;
        }
        var given = context.HttpContext.Request.Headers[Header].ToString();
        if (!Matches(given, expected))
            context.Result = new UnauthorizedObjectResult(new ApiError("bad_staff_key", "That staff key is not right."));
    }

    /// <summary>Constant-time, so the response time does not leak how much of the key was right.</summary>
    public static bool Matches(string given, string expected)
    {
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(given));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(a, b) && given.Length > 0;
    }
}
