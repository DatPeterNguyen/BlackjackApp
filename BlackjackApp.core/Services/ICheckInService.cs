namespace BlackjackApp.core.Services;

// Daily $100 check-in (24hr reset) + 7-day streak bonus ($100-$1K range).
public interface ICheckInService
{
    bool CanCheckInToday();

    decimal CheckIn();
}
