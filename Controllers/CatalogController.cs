using Backend.Domain;
using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>What the clinic offers: the public part of the app, read only.</summary>
[ApiController]
[Route("api")]
public class CatalogController : ControllerBase
{
    private readonly CatalogStore _catalog;
    private readonly AvailabilityService _availability;
    private readonly ClinicOptions _options;

    public CatalogController(CatalogStore catalog, AvailabilityService availability, ClinicOptions options)
    {
        _catalog = catalog;
        _availability = availability;
        _options = options;
    }

    [HttpGet("clinic")]
    public IActionResult Clinic() => Ok(new
    {
        _options.Name,
        _options.TimeZoneId,
        _options.CancellationNoticeHours,
        _options.BookingHorizonDays,
        Today = _availability.Today(),
    });

    [HttpGet("services")]
    public async Task<IActionResult> Services(CancellationToken ct) => Ok(await _catalog.ServicesAsync(ct));

    [HttpGet("practitioners")]
    public async Task<IActionResult> Practitioners([FromQuery] int? serviceId, CancellationToken ct) =>
        Ok(await _catalog.PractitionersAsync(serviceId, ct));
}
