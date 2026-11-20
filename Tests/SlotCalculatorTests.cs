using Backend.Domain;

namespace Tests;

public class SlotCalculatorTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");
    private static readonly ClinicOptions Options = new() { SlotStepMinutes = 15, MinLeadMinutes = 120, BookingHorizonDays = 60 };

    // Monday 2026-11-30. Israel is UTC+2 in winter, so 09:00 local = 07:00 UTC.
    private static readonly DateOnly Monday = new(2026, 11, 30);
    private static readonly DateTime Now = new(2026, 11, 24, 6, 0, 0, DateTimeKind.Utc);
    private static readonly SlotService Consult = new(DurationMinutes: 20, BufferMinutes: 5);

    private static WorkingWindow[] Hours(string from, string to, DayOfWeek day = DayOfWeek.Monday) =>
        new[] { new WorkingWindow(day, TimeOnly.Parse(from), TimeOnly.Parse(to)) };

    private static DateTime Utc(int hour, int minute = 0) => new(2026, 11, 30, hour, minute, 0, DateTimeKind.Utc);

    private static IReadOnlyList<DateTime> Slots(IEnumerable<WorkingWindow> hours, SlotService? service = null, IReadOnlyCollection<TimeRange>? busy = null, DateTime? now = null, DateOnly? day = null) =>
        SlotCalculator.FreeSlots(day ?? Monday, service ?? Consult, hours, busy ?? Array.Empty<TimeRange>(), now ?? Now, Options, Zone);

    [Fact]
    public void Slots_follow_the_grid_and_the_last_one_ends_by_closing_time()
    {
        var slots = Slots(Hours("09:00", "10:00"));

        // 20 minutes each: 09:00, 09:15, 09:30 fit; 09:45 would end at 10:05.
        Assert.Equal(new[] { Utc(7), Utc(7, 15), Utc(7, 30) }, slots);
    }

    [Fact]
    public void The_buffer_may_run_past_closing_but_the_visit_may_not()
    {
        var slots = Slots(Hours("09:00", "09:40"));

        Assert.Equal(Utc(7, 15), slots.Last()); // ends 09:35, buffer to 09:40
    }

    [Fact]
    public void A_booked_appointment_and_its_buffer_block_overlapping_starts()
    {
        // Booked 09:30-09:50 local, cleanup until 09:55.
        var busy = new[] { new TimeRange(Utc(7, 30), Utc(7, 55)) };

        var slots = Slots(Hours("09:00", "11:00"), busy: busy);

        Assert.Contains(Utc(7), slots);          // 09:00-09:20 + 5 buffer ends 09:25, clear
        Assert.DoesNotContain(Utc(7, 15), slots); // its buffer would run to 09:40
        Assert.DoesNotContain(Utc(7, 30), slots);
        Assert.DoesNotContain(Utc(7, 45), slots); // starts before the cleanup ends
        Assert.Contains(Utc(8), slots);          // 10:00 is free again
    }

    [Fact]
    public void Back_to_back_is_allowed_when_ranges_only_touch()
    {
        var busy = new[] { new TimeRange(Utc(7, 25), Utc(8)) };

        Assert.Contains(Utc(7), Slots(Hours("09:00", "11:00"), busy: busy)); // blocked until 09:25 exactly
    }

    [Fact]
    public void Time_off_removes_every_slot_it_touches()
    {
        var busy = new[] { new TimeRange(Utc(8), Utc(9)) }; // 10:00-11:00 local

        var slots = Slots(Hours("09:00", "12:00"), busy: busy);

        Assert.DoesNotContain(slots, s => s > Utc(7, 35) && s < Utc(9));
        Assert.Contains(Utc(9), slots);
    }

    [Fact]
    public void Nothing_inside_the_minimum_lead_time()
    {
        var now = Utc(6, 30); // 08:30 local, so the first bookable start is 10:30

        var slots = Slots(Hours("09:00", "12:00"), now: now);

        Assert.Equal(Utc(8, 30), slots.First());
    }

    [Fact]
    public void Nothing_beyond_the_booking_horizon()
    {
        var farDay = DateOnly.FromDateTime(Now).AddDays(61);

        Assert.Empty(Slots(Hours("09:00", "12:00", farDay.DayOfWeek), day: farDay));
    }

    [Fact]
    public void A_split_day_gives_slots_in_both_windows_and_none_in_the_gap()
    {
        var hours = new[]
        {
            new WorkingWindow(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(11, 0)),
            new WorkingWindow(DayOfWeek.Monday, new TimeOnly(15, 0), new TimeOnly(19, 0)),
        };

        var slots = Slots(hours);

        Assert.Contains(Utc(6), slots);
        Assert.Contains(Utc(13), slots);
        Assert.DoesNotContain(slots, s => s >= Utc(9) && s < Utc(13));
    }

    [Fact]
    public void Other_weekdays_have_no_slots()
    {
        Assert.Empty(Slots(Hours("09:00", "12:00", DayOfWeek.Tuesday)));
    }

    [Fact]
    public void The_hour_skipped_when_clocks_go_forward_has_no_slots()
    {
        // Israel moves to summer time on Friday 2027-03-26 at 02:00 (02:00-03:00 does not exist).
        var day = new DateOnly(2027, 3, 26);
        var hours = Hours("01:00", "04:00", DayOfWeek.Friday);
        var options = new ClinicOptions { SlotStepMinutes = 30, MinLeadMinutes = 0, BookingHorizonDays = 400 };

        var slots = SlotCalculator.FreeSlots(day, new SlotService(30, 0), hours, Array.Empty<TimeRange>(), Now, options, Zone);
        var local = slots.Select(s => ClinicClock.ToLocal(s, Zone).ToString("HH:mm")).ToList();

        Assert.Equal(new[] { "01:00", "01:30", "03:00", "03:30" }, local);
    }

    [Fact]
    public void Back_in_winter_the_repeated_hour_is_offered_once()
    {
        // Israel goes back to winter time on Sunday 2027-10-31 at 02:00 (01:00-02:00 happens twice).
        var day = new DateOnly(2027, 10, 31);
        var hours = Hours("00:30", "03:00", DayOfWeek.Sunday);
        var options = new ClinicOptions { SlotStepMinutes = 30, MinLeadMinutes = 0, BookingHorizonDays = 400 };

        var slots = SlotCalculator.FreeSlots(day, new SlotService(30, 0), hours, Array.Empty<TimeRange>(), Now, options, Zone);

        Assert.Equal(slots.Count, slots.Distinct().Count());
        Assert.Equal(5, slots.Count); // 00:30, 01:00, 01:30, 02:00, 02:30 on the wall clock
    }
}
