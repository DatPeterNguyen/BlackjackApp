using System;
using System.Threading.Tasks;
using BlackjackApp.core.Services;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Dispatching;
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

    /// <summary>Ticks the countdown. Only runs while the page is on screen and a reward is actually pending - see StartOrStopCountdown.</summary>
    private IDispatcherTimer? _countdown;

    /// <summary>
    /// Whether the page is currently on screen. Needed because work can land
    /// after the page is gone: the day-7 spin awaits a 3+ second animation
    /// before paying out, and closing mid-spin means OnDisappearing has
    /// already stopped the timer by the time PayOut asks for one. Without this
    /// that request would start a fresh timer on a dead page, which nothing
    /// would ever stop - it would tick once a second forever, holding the
    /// whole visual tree alive and writing to labels nobody can see.
    /// </summary>
    private bool _isOnScreen;
    private CheckInStatus _status;
    private bool _busy;

    /// <summary>Raised once a reward has actually been paid and saved, so the menu underneath can refresh its own display.</summary>
    public event Action? Claimed;

    public CheckInPage()
    {
        InitializeComponent();

        WheelView.Drawable = new WheelDrawable();

        ReloadStatus();
        BuildDayStrip();
        RefreshForStatus();

#if DEBUG
        DebugForceDayButton.IsVisible = true;
#endif
    }

    private void ReloadStatus() => _status = DailyCheckIn.GetStatus(
        GameProgressStorage.LoadLastCheckInUtc(),
        GameProgressStorage.LoadCheckInStreakDay(),
        // Read fresh rather than from a value captured when the page
        // opened: TickCountdown calls this the moment the clock runs
        // out, and against a stale "now" the reward would still look
        // locked on the page that just finished counting down to it.
        DateTime.UtcNow);

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
                FontSize = 12,
                FontFamily = AppFonts.Display,
                TextColor = isClaimed ? Color.FromArgb("#2A4D00") : Color.FromArgb("#E6F3C8"),
                HorizontalOptions = LayoutOptions.Center,
            };

            var amountLabel = new Label
            {
                Text = isWheelDay ? "SPIN" : $"${DailyCheckIn.DailyReward:N0}",
                FontSize = isWheelDay ? 14 : 15,
                FontFamily = AppFonts.Display,
                TextColor = isClaimed
                    ? Color.FromArgb("#2A4D00")
                    : isWheelDay ? Color.FromArgb("#FFC400") : Colors.White,
                HorizontalOptions = LayoutOptions.Center,
            };

            DayStripLayout.Children.Add(new Border
            {
                WidthRequest = 44,
                Padding = new Thickness(2, 6),
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                Stroke = isPending ? Color.FromArgb("#FFC400") : Color.FromArgb("#6F7D55"),
                StrokeThickness = isPending ? 3 : 1,
                BackgroundColor = isClaimed ? Color.FromArgb("#D9A300") : Color.FromArgb("#59000000"),
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

        var isActionableWheelDay = _status.CanClaim && _status.Kind == CheckInRewardKind.WheelSpin;
        WheelHintLabel.IsVisible = isActionableWheelDay;

        if (_status.CanClaim)
        {
            StreakSummaryLabel.Text = _status.StreakWasBroken
                ? "Your streak lapsed, so you're starting over at day 1 of 7."
                : $"Day {_status.StreakDay} of {DailyCheckIn.StreakLength}.";

            // Day 7 spins by tapping the wheel itself (see
            // WheelContainer_OnTapped) rather than a button, so the Claim
            // button only ever shows for the flat days 1-6 reward.
            ClaimButton.IsVisible = !isActionableWheelDay;
            ClaimButton.IsEnabled = true;
            ClaimButton.Text = $"Claim ${DailyCheckIn.DailyReward:N0}";

            CountdownLabel.IsVisible = false;
            StopCountdown();

            return;
        }

        StreakSummaryLabel.Text = _status.StreakDay >= DailyCheckIn.StreakLength
            ? "Streak complete - a fresh one starts when the clock runs out."
            : $"Day {_status.StreakDay} claimed. Day {_status.StreakDay + 1} unlocks when the clock runs out.";

        ClaimButton.IsVisible = false;

        // Draw the remaining time immediately from the status we already
        // have, so the countdown is correct on the very first frame instead
        // of blank until the first tick a second later.
        ShowCountdown(_status.TimeUntilNextClaim);
        StartOrStopCountdown();
    }

    private void ClaimButton_OnClicked(object? sender, EventArgs e)
    {
        if (_busy || !_status.CanClaim)
        {
            return;
        }

        _busy = true;
        ClaimButton.IsEnabled = false;

        try
        {
            PayOut(DailyCheckIn.DailyReward, $"+${DailyCheckIn.DailyReward:N0} added to your balance.");
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Day 7 only - tapping anywhere on the wheel (see the Grid's
    /// TapGestureRecognizer in CheckInPage.xaml) spins it, replacing the old
    /// separate "Spin the Wheel" button.
    /// </summary>
    private async void WheelContainer_OnTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || !_status.CanClaim || _status.Kind != CheckInRewardKind.WheelSpin)
        {
            return;
        }

        _busy = true;

        try
        {
            await SpinAndPayAsync();
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
        await WheelView.RotateToAsync(landing, SpinDurationMs, Easing.CubicOut);

        var prize = DailyCheckIn.PrizeAt(wedge);
        PayOut(prize, $"The wheel landed on ${prize:N0}!");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _isOnScreen = true;
        StartOrStopCountdown();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isOnScreen = false;
        StopCountdown();
    }

    /// <summary>
    /// Runs a one-second tick only while there is something to count down to.
    /// A reward that is already claimable needs no timer, and neither does a
    /// page nobody is looking at - leaving a dispatcher timer running against
    /// a closed page is how you leak one.
    /// </summary>
    private void StartOrStopCountdown()
    {
        if (!_isOnScreen || _status.CanClaim)
        {
            StopCountdown();
            return;
        }

        if (_countdown is not null)
        {
            return;
        }

        _countdown = Dispatcher.CreateTimer();
        _countdown.Interval = TimeSpan.FromSeconds(1);
        _countdown.Tick += (_, _) => TickCountdown();
        _countdown.Start();
    }

    private void StopCountdown()
    {
        _countdown?.Stop();
        _countdown = null;
    }

    /// <summary>
    /// Re-reads the clock and redraws the remaining time. When it reaches
    /// zero the whole status is reloaded, so the page flips to a claimable
    /// reward on its own rather than making the player back out and return.
    /// </summary>
    private void TickCountdown()
    {
        var remaining = DailyCheckIn.TimeUntilNextClaim(
            GameProgressStorage.LoadLastCheckInUtc(),
            DateTime.UtcNow);

        if (remaining <= TimeSpan.Zero)
        {
            StopCountdown();
            ReloadStatus();
            BuildDayStrip();
            RefreshForStatus();
            return;
        }

        ShowCountdown(remaining);
    }

    /// <summary>Renders the remaining time as h:mm:ss, counting whole seconds down rather than showing a stale value for the first second.</summary>
    private void ShowCountdown(TimeSpan remaining)
    {
        // Round up: with 4.2s left the player should read "4", not "4" only
        // after it has already become 3.2s. Ceiling keeps the last visible
        // number 1 rather than flashing 0 before it unlocks.
        var whole = TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds));

        CountdownLabel.Text = $"Next bonus in {(int)whole.TotalHours}:{whole.Minutes:00}:{whole.Seconds:00}";
        CountdownLabel.IsVisible = true;
    }

    private void PayOut(decimal amount, string message)
    {
        GameProgressStorage.SaveBalance(GameProgressStorage.LoadBalance() + amount);
        GameProgressStorage.SaveCheckIn(DateTime.UtcNow, _status.StreakDay);

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
        // Backdates the last claim past the 24-hour gate but inside the
        // 48-hour streak window, so the next day is immediately claimable.
        GameProgressStorage.SaveCheckIn(DateTime.UtcNow - DailyCheckIn.ClaimInterval - TimeSpan.FromMinutes(1), 6);

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
            Color.FromArgb("#4F8400"),
            Color.FromArgb("#2F5500"),
            Color.FromArgb("#4F8400"),
            Color.FromArgb("#2F5500"),
            Color.FromArgb("#D9A300"),
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

                canvas.StrokeColor = Color.FromArgb("#2A4D00");
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

            canvas.FillColor = Color.FromArgb("#2A4D00");
            canvas.FillCircle(cx, cy, 17f);
            canvas.StrokeColor = Color.FromArgb("#FFC400");
            canvas.StrokeSize = 3f;
            canvas.DrawCircle(cx, cy, 17f);
        }
    }
}
