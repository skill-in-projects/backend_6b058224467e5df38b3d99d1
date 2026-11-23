using Backend.Models;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>One place that decides the HTTP status for each error code, so every controller answers the same way.</summary>
public static class ApiErrors
{
    public static IActionResult Error(this ControllerBase controller, ApiError error) => error.Code switch
    {
        "no_service" or "no_practitioner" or "no_appointment" => controller.NotFound(error),
        "slot_taken" or "time_off_clash" or "not_active" => controller.Conflict(error),
        "too_late" or "not_started" => controller.UnprocessableEntity(error),
        _ => controller.BadRequest(error),
    };

    /// <summary>Times from the browser must say which zone they are in; a bare local time is ambiguous, so it is refused.</summary>
    public static DateTime? Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => null,
    };
}
