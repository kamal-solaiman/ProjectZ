namespace FitohPhone.Models;

public sealed record DashboardStats(
    decimal SalesRevenue,
    decimal RepairRevenue,
    decimal TransferRevenue,
    decimal Expenses,
    decimal ReturnsAmount,
    decimal TotalProfit,
    decimal NetAmount,
    int RepairsInProgress,
    int RepairsReadyForDelivery,
    int LowStockCount,
    decimal CashBalance,
    decimal VodafoneCashBalance1,
    decimal VodafoneCashBalance2,
    decimal InstaPayBalance1,
    decimal InstaPayBalance2,
    int TotalCustomers,
    int TotalProducts,
    int TotalInvoices);
