using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Route("api/availability")]
public class AvailabilityController : ControllerBase
{
    private const int MaxDays = 31;

    private readonly CatalogStore _catalog;
    private readonly AvailabilityService _availability;

    public AvailabilityController(CatalogStore catalog, AvailabilityService availability)
    {
        _catalog = catalog;
        _availability = availability;
    }

    /// <summary>GET api/availability?serviceId=1&amp;practitionerId=2&amp;date=2026-12-01: the free start times that day.</summary>
    [HttpGet]
    public async Task<IActionResult> Day([FromQuery] int serviceId, [FromQuery] int practitionerId, [FromQuery] DateOnly date, CancellationToken ct)
    {
        var (service, _, error) = await _catalog.OfferAsync(serviceId, practitionerId, ct);
        if (error != null) return this.Error(error);
        var hours = await _catalog.WorkingHoursAsync(practitionerId, ct);
        return Ok(await _availability.DayAsync(service!, practitionerId, hours, date, ct));
    }

    /// <summary>GET api/availability/days?serviceId=1&amp;practitionerId=2&amp;from=2026-12-01&amp;days=14: free slots per day.</summary>
    [HttpGet("days")]
    public async Task<IActionResult> Days([FromQuery] int serviceId, [FromQuery] int practitionerId, [FromQuery] DateOnly? from,
        [FromQuery] int days = 14, CancellationToken ct = default)
    {
        if (days is < 1 or > MaxDays)
            return BadRequest(new ApiError("bad_range", $"Ask for 1 to {MaxDays} days."));
        var (service, _, error) = await _catalog.OfferAsync(serviceId, practitionerId, ct);
        if (error != null) return this.Error(error);
        var hours = await _catalog.WorkingHoursAsync(practitionerId, ct);
        return Ok(await _availability.DaysAsync(service!, practitionerId, hours, from ?? _availability.Today(), days, ct));
    }
}
