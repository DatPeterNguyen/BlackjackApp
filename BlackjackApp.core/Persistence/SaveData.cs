namespace BlackjackApp.core.Persistence;

// Plain data shape that gets serialized to a local JSON file.
// Fields will grow (P&L history, streak state, settings) as those
// features get built.
public class SaveData
{
    public decimal ChipBalance { get; set; }
    public DateTime? LastCheckIn { get; set; }
    public int CheckInStreak { get; set; }
}
