using System;
using System.Threading.Tasks;
using BlackjackApp.core.Services;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace BlackjackApp.Maui.Views;

/// <summary>
/// The daily check-in reward (item 14 on the polish list) - $100 a day for
/// days 1-6 of a streak, and a wheel spin worth $100 - $1,000 on day 7.
/// Opened from the start menu, either automatically when a reward is waiting
/// or from its Daily Reward button (see GameMenuPage).
///
/// All the streak arithmetic lives in BlackjackApp.core's DailyCheckIn,
/// which is pure and takes today's date as an argument - this page just
/// renders the result, animates the wheel, and persists the claim through
/// GameProgressStorage.
/// </summary>
public partial class CheckInPage : ContentPage
{
    /// <summary>Whole turns the wheel makes before settling - purely cosmetic.</summary>
    private const int WheelSpins = 5;

    private const uint SpinDurationMs = 3200;

    private static readonly Random Rng = new();

    private readonly DateOnly _today;
    private CheckInStatus _status;
    private bool _busy;

    /// <summary>Raised once a reward has actually been paid and saved, so the menu underneath can refresh its own display.</summary>
    public event Action? Claimed;

    public CheckInPage()
    {
        InitializeComponent();

        _today = DateOnly.FromDateTime(DateTime.Now);
        WheelView.Drawable = new WheelDrawable();

        ReloadStatus();
        BuildDayStrip();
        RefreshForStatus();

#if DEBUG
        DebugForceDayButton.IsVisible = true;
#endif
    }

    private void ReloadStatus() => _status = DailyCheckIn.GetStatus(
        GameProgressStorage.LoadLastCheckIn(),
        GameProgressStorage.LoadCheckInStreakDay(),
        _today);

    /// <summary>
    /// Draws the seven-day cycle: days already banked in this cycle are
    /// filled in, the day currently up for grabs gets the gold outline, and
    /// day 7 advertises the wheel instead of a flat amount.
    /// </summary>
    private void BuildDayStrip()
    {
        DayStripLayout.Children.Clear();

        var claimedDays = _status.CanClaim ? _status.StreakDay - 1 : _status.StreakDay;
        var pendingDay = _status.CanClaim ? _status.StreakDay : 0;

        for (var day = 1; day <= DailyCheckIn.StreakLength; day++)
        {
            var isClaimed = day <= claimedDays;
            var isPending = day == pendingDay;
            var isWheelDay = DailyCheckIn.KindFor(day) == CheckInRewardKind.WheelSpin;

            var dayLabel = new Label
            {
                Text = $"Day {day}",
                FontSize = 9,
                TextColor = isClaimed ? Color.FromArgb("#0B3D2E") : Color.FromArgb("#8FBFA9"),
                HorizontalOptions = LayoutOptions.Center,
            };

            var amountLabel = new Label
            {
                Text = isWheelDay ? "SPIN" : $"${DailyCheckIn.DailyReward:N0}",
                FontSize = isWheelDay ? 11 : 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = isClaimed
                    ? Color.FromArgb("#0B3D2E")
                    : isWheelDay ? Color.FromArgb("#FFD700") : Colors.White,
                HorizontalOptions = LayoutOptions.Center,
            };

            DayStripLayout.Children.Add(new Border
            {
                WidthRequest = 44,
                Padding = new Thickness(2, 6),
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                Stroke = isPending ? Color.FromArgb("#FFD700") : Color.FromArgb("#3A6B57"),
                StrokeThickness = isPending ? 3 : 1,
                BackgroundColor = isClaimed ? Color.FromArgb("#C89B2C") : Color.FromArgb("#0E4433"),
                Content = new VerticalStackLayout
                {
                    Spacing = 1,
                    Children = { dayLabel, amountLabel },
                },
            });
        }
    }

    private void RefreshForStatus()
    {
        // Stays visible after a day-7 claim too, so the wheel keeps showing
        // where it actually landed rather than vanishing on payout.
        WheelContainer.IsVisible = _status.Kind == CheckInRewardKind.WheelSpin;

        if (_status.CanClaim)
        {
            StreakSummaryLabel.Text = _status.StreakWasBroken
                ? "Your streak lapsed, so you're starting over at day 1 of 7."
                : $"Day {_status.StreakDay} of {DailyCheckIn.StreakLength}.";

            ClaimButton.IsVisible = true;
            ClaimButton.IsEnabled = true;
            ClaimButton.Text = _status.Kind == CheckInRewardKind.WheelSpin
                ? "Spin the Wheel"
                : $"Claim ${DailyCheckIn.DailyReward:N0}";

            return;
        }

        StreakSummaryLabel.Text = _status.StreakDay >= DailyCheckIn.StreakLength
            ? "Streak complete - a fresh one starts tomorrow."
            : $"Day {_status.StreakDay} claimed. Day {_status.StreakDay + 1} unlocks tomorrow.";

        ClaimButton.IsVisible = false;
    }

    private async void ClaimButton_OnClicked(object? sender, EventArgs e)
    {
        if (_busy || !_status.CanClaim)
        {
            return;
        }

        _busy = true;
        ClaimButton.IsEnabled = false;

        try
        {
            if (_status.Kind == CheckInRewardKind.WheelSpin)
            {
                await SpinAndPayAsync();
            }
            else
            {
                PayOut(DailyCheckIn.DailyReward, $"+${DailyCheckIn.DailyReward:N0} added to your balance.");
            }
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Picks the winning wedge first, then animates the wheel onto it - the
    /// result is decided by DailyCheckIn.SpinWheel, and the animation just
    /// shows it, so a slow or interrupted animation can never change what
    /// gets paid.
    /// </summary>
    private async Task SpinAndPayAsync()
    {
        var wedge = DailyCheckIn.SpinWheel(Rng);
        var sweep = 360.0 / DailyCheckIn.WheelPrizes.Length;

        // WheelDrawable lays wedge i out starting at -90 degrees (12 o'clock)
        // and sweeping clockwise, so its centre sits at -90 + i*sweep +
        // sweep/2. Rotating by the negative of that offset brings that centre
        // back under the fixed pointer at the top; the whole turns on front
        // are just for show.
        var landing = 360.0 * WheelSpins - (wedge * sweep + sweep / 2.0);

        WheelView.Rotation = 0;
        await WheelView.RotateTo(landing, SpinDurationMs, Easing.CubicOut);

        var prize = DailyCheckIn.PrizeAt(wedge);
        PayOut(prize, $"The wheel landed on ${prize:N0}!");
    }

    private void PayOut(decimal amount, string message)
    {
        GameProgressStorage.SaveBalance(GameProgressStorage.LoadBalance() + amount);
        GameProgressStorage.SaveCheckIn(_today, _status.StreakDay);

        ReloadStatus();
        BuildDayStrip();
        RefreshForStatus();

        OutcomeLabel.Text = message;
        OutcomeLabel.IsVisible = true;

        Claimed?.Invoke();
    }

    /// <summary>
    /// Debug builds only - jumps the saved streak straight to "day 6,
    /// claimed yesterday" so today's check-in lands on day 7 (the wheel)
    /// without waiting a real week for it to come around. The button that
    /// calls this only shows itself in DEBUG (see the constructor); this
    /// handler has no #if around it so a Release build still compiles
    /// against the always-present XAML button, it just never runs.
    /// </summary>
    private void DebugForceDayButton_OnClicked(object? sender, EventArgs e)
    {
        GameProgressStorage.SaveCheckIn(_today.AddDays(-1), 6);

        ReloadStatus();
        BuildDayStrip();
        RefreshForStatus();

        OutcomeLabel.IsVisible = false;
        OutcomeLabel.Text = "";
    }

    private async void CloseButton_OnClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }

    /// <summary>
    /// Draws the day-7 wheel: one wedge per DailyCheckIn.WheelPrizes entry,
    /// laid out from 12 o'clock clockwise so the spin maths in
    /// SpinAndPayAsync can work out where each wedge ends up. Wedges are
    /// stroked as many-sided polygons rather than arcs, which keeps the
    /// geometry explicit instead of depending on arc angle conventions.
    /// </summary>
    private sealed class WheelDrawable : IDrawable
    {
        private const int SegmentsPerWedge = 24;

        private static readonly Color[] WedgeColors =
        [
            Color.FromArgb("#1B7A4D"),
            Color.FromArgb("#11593A"),
            Color.FromArgb("#1B7A4D"),
            Color.FromArgb("#11593A"),
            Color.FromArgb("#C89B2C"),
        ];

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var prizes = DailyCheckIn.WheelPrizes;
            var sweep = 360f / prizes.Length;
            var cx = dirtyRect.X + dirtyRect.Width / 2f;
            var cy = dirtyRect.Y + dirtyRect.Height / 2f;
            var radius = Math.Min(dirtyRect.Width, dirtyRect.Height) / 2f - 6f;

            for (var i = 0; i < prizes.Length; i++)
            {
                var path = new PathF();
                path.MoveTo(cx, cy);

                for (var step = 0; step <= SegmentsPerWedge; step++)
                {
                    var degrees = -90f + i * sweep + sweep * step / SegmentsPerWedge;
                    var radians = degrees * MathF.PI / 180f;
                    path.LineTo(cx + radius * MathF.Cos(radians), cy + radius * MathF.Sin(radians));
                }

                path.Close();

                canvas.FillColor = WedgeColors[i % WedgeColors.Length];
                canvas.FillPath(path);

                canvas.StrokeColor = Color.FromArgb("#0B3D2E");
                canvas.StrokeSize = 2f;
                canvas.DrawPath(path);
            }

            canvas.FontColor = Colors.White;
            canvas.FontSize = 15f;

            for (var i = 0; i < prizes.Length; i++)
            {
                var degrees = -90f + i * sweep + sweep / 2f;
                var radians = degrees * MathF.PI / 180f;
                var labelX = cx + radius * 0.62f * MathF.Cos(radians);
                var labelY = cy + radius * 0.62f * MathF.Sin(radians);

                canvas.DrawString(
                    $"${prizes[i]:N0}",
                    labelX - 40f,
                    labelY - 11f,
                    80f,
                    22f,
                    Microsoft.Maui.Graphics.HorizontalAlignment.Center,
                    Microsoft.Maui.Graphics.VerticalAlignment.Center);
            }

            canvas.FillColor = Color.FromArgb("#0B3D2E");
            canvas.FillCircle(cx, cy, 17f);
            canvas.StrokeColor = Color.FromArgb("#FFD700");
            canvas.StrokeSize = 3f;
            canvas.DrawCircle(cx, cy, 17f);
        }
    }
}
