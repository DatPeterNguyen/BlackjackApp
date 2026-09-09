namespace BlackjackApp.core.Services;

// Future: real-money chip purchases. Not needed for the initial build.
public interface IChipPurchaseService
{
    void PurchaseChips(decimal amountUsd);
}
