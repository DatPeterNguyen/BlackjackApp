using System;
using System.Globalization;
using BlackjackApp.core.Services;
using Microsoft.Maui.Controls.Shapes;

namespace BlackjackApp.Maui.Views;

/// <summary>
/// The chip shop - see the comment at the top of ShopPage.xaml.
///
/// A shelf with nothing behind the counter: it renders
/// <see cref="ChipShop.Bundles"/> and disables every buy button, because
/// <see cref="IChipPurchaseService"/> has no implementation and so nothing
/// in this app can take a payment. Built now so the screen exists and can be
/// judged before the genuinely hard parts of selling anything are started.
/// </summary>
public partial class ShopPage : ContentPage
{
    public ShopPage()
    {
        InitializeComponent();
        BuildBundles();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Read fresh each time - a round may have been played since this page
        // was last opened.
        BalanceLabel.Text = $"You have ${GameProgressStorage.LoadBalance():N0} in chips";
    }

    /// <summary>One card per bundle, cheapest first, each with its buy button turned off.</summary>
    private void BuildBundles()
    {
        BundlesLayout.Children.Clear();

        foreach (var bundle in ChipShop.Bundles)
        {
            BundlesLayout.Children.Add(CreateBundleCard(bundle));
        }
    }

    /// <summary>
    /// One bundle: the chip art, what it grants, what it would cost, and a
    /// button that does nothing on purpose.
    /// </summary>
    private static Border CreateBundleCard(ChipBundle bundle)
    {
        var chipImage = new Image
        {
            // Reuses the chip denomination art rather than importing new
            // shop pictures: the sizes are only representative, so the
            // nearest denomination that reads as "more chips" is enough.
            Source = ImageSource.FromFile(ChipArtFor(bundle.Chips)),
            HeightRequest = 52,
            Aspect = Aspect.AspectFit,
            VerticalOptions = LayoutOptions.Center,
        };

        var nameLabel = new Label
        {
            Text = bundle.Name,
            TextColor = Colors.White,
            FontSize = 20,
            FontFamily = AppFonts.Display,
        };

        var chipsLabel = new Label
        {
            Text = $"{bundle.Chips:N0} chips",
            TextColor = Color.FromArgb("#E6F3C8"),
            FontSize = 15,
            FontFamily = AppFonts.Display,
        };

        var details = new VerticalStackLayout
        {
            Spacing = 1,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            Children = { nameLabel, chipsLabel },
        };

        if (bundle.IsBestValue)
        {
            details.Children.Add(new Label
            {
                Text = "BEST VALUE",
                TextColor = Color.FromArgb("#FFC400"),
                FontSize = 12,
                FontFamily = AppFonts.Display,
            });
        }

        // Disabled, and labelled with the price rather than an action, so it
        // reads as a price tag instead of a button someone might expect to
        // open a payment sheet. IsEnabled is the part that matters; the
        // wording is what stops it being a tease.
        var buyButton = new Button
        {
            Text = bundle.PriceUsd.ToString("C", CultureInfo.GetCultureInfo("en-US")),
            Style = (Style)Application.Current!.Resources["GoldButton"],
            WidthRequest = 104,
            HeightRequest = 42,
            FontSize = 18,
            IsEnabled = false,
            VerticalOptions = LayoutOptions.Center,
        };

        var content = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            ColumnSpacing = 12,
            Children = { chipImage, details, buyButton },
        };
        Grid.SetColumn(chipImage, 0);
        Grid.SetColumn(details, 1);
        Grid.SetColumn(buyButton, 2);

        return new Border
        {
            Stroke = bundle.IsBestValue ? Color.FromArgb("#FFC400") : Color.FromArgb("#6F7D55"),
            StrokeThickness = bundle.IsBestValue ? 2 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            BackgroundColor = Color.FromArgb("#59000000"),
            Padding = 12,
            Content = content,
        };
    }

    /// <summary>
    /// The chip picture that best suggests a bundle's size. Purely
    /// decorative - a bundle is an amount, not a pile of one denomination.
    /// </summary>
    private static string ChipArtFor(decimal chips) => chips switch
    {
        >= 100_000m => "chip_10000.png",
        >= 30_000m => "chip_5000.png",
        >= 10_000m => "chip_1000.png",
        _ => "chip_500.png",
    };

    private async void CloseButton_OnClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
