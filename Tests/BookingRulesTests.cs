using Backend.Domain;

namespace Tests;

public class BookingRulesTests
{
    private static readonly ClinicOptions Options = new() { CancellationNoticeHours = 24 };
    private static readonly DateTime Start = new(2026, 12, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_patient_can_change_until_the_notice_period()
    {
        Assert.True(BookingRules.PatientCanChange(AppointmentStatus.Booked, Start, Start.AddHours(-25), Options).Ok);
        Assert.True(BookingRules.PatientCanChange(AppointmentStatus.Booked, Start, Start.AddHours(-24).AddSeconds(-1), Options).Ok);
    }

    [Fact]
    public void Inside_the_notice_period_the_patient_is_told_to_call()
    {
        var result = BookingRules.PatientCanChange(AppointmentStatus.Booked, Start, Start.AddHours(-24), Options);

        Assert.False(result.Ok);
        Assert.Equal("too_late", result.Code);
        Assert.Contains("24 hours", result.Message);
    }

    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    public void Only_a_booked_appointment_can_be_changed(string status)
    {
        Assert.Equal("not_active", BookingRules.PatientCanChange(status, Start, Start.AddDays(-5), Options).Code);
    }

    [Fact]
    public void Staff_can_cancel_at_any_time_but_not_mark_a_future_visit_as_done()
    {
        var before = Start.AddMinutes(-30);

        Assert.True(BookingRules.StaffCanSet(AppointmentStatus.Booked, AppointmentStatus.Cancelled, Start, before).Ok);
        Assert.Equal("not_started", BookingRules.StaffCanSet(AppointmentStatus.Booked, AppointmentStatus.Completed, Start, before).Code);
        Assert.Equal("not_started", BookingRules.StaffCanSet(AppointmentStatus.Booked, AppointmentStatus.NoShow, Start, before).Code);
        Assert.True(BookingRules.StaffCanSet(AppointmentStatus.Booked, AppointmentStatus.NoShow, Start, Start.AddMinutes(10)).Ok);
    }

    [Fact]
    public void Staff_can_correct_completed_to_no_show_but_not_reopen_a_cancellation()
    {
        var after = Start.AddHours(1);

        Assert.True(BookingRules.StaffCanSet(AppointmentStatus.Completed, AppointmentStatus.NoShow, Start, after).Ok);
        Assert.Equal("not_active", BookingRules.StaffCanSet(AppointmentStatus.Cancelled, AppointmentStatus.Completed, Start, after).Code);
        Assert.Equal("bad_status", BookingRules.StaffCanSet(AppointmentStatus.Completed, AppointmentStatus.Booked, Start, after).Code);
    }
}
