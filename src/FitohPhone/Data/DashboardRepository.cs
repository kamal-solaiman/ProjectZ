using System.Data;
using FitohPhone.Models;
using FitohPhone.Services;
using Microsoft.Data.SqlClient;

namespace FitohPhone.Data;

public sealed class DashboardRepository(ConnectionSettingsProvider settings)
{
    public async Task<DashboardStats> GetStatsAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);

        // revenues
        decimal salesRev = await ExecuteScalarDecimal(conn,
            "SELECT ISNULL(SUM(GrandTotal),0) FROM dbo.SalesInvoices WHERE IsCancelled=0 AND CAST(InvoiceDate AS DATE)=CAST(SYSUTCDATETIME() AS DATE)", ct);
        decimal repairRev = await ExecuteScalarDecimal(conn,
            "SELECT ISNULL(SUM(TotalCost),0) FROM dbo.PhoneRepairs WHERE Status=N'تم التسليم' AND CAST(ActualDeliveryDate AS DATE)=CAST(SYSUTCDATETIME() AS DATE)", ct);
        decimal transferRev = await ExecuteScalarDecimal(conn,
            "SELECT ISNULL(SUM(Commission),0) FROM dbo.TransferTransactions WHERE Status=N'نجاح' AND CAST(TransferDate AS DATE)=CAST(SYSUTCDATETIME() AS DATE)", ct);
        decimal returnsAmt = await ExecuteScalarDecimal(conn,
            "SELECT ISNULL(SUM(TotalAmount),0) FROM dbo.ReturnsExchanges WHERE CAST(ReturnDate AS DATE)=CAST(SYSUTCDATETIME() AS DATE)", ct);

        // counts
        int repairsInProg = await ExecuteScalarInt(conn, "SELECT COUNT(*) FROM dbo.PhoneRepairs WHERE Status=N'قيد الصيانة'", ct);
        int repairsReady = await ExecuteScalarInt(conn, "SELECT COUNT(*) FROM dbo.PhoneRepairs WHERE Status=N'جاهز للتسليم'", ct);
        int lowStock = await ExecuteScalarInt(conn, "SELECT COUNT(*) FROM dbo.Products WHERE CurrentStock <= MinStock AND IsActive=1", ct);
        int totalCustomers = await ExecuteScalarInt(conn, "SELECT COUNT(*) FROM dbo.Customers WHERE IsActive=1", ct);
        int totalProducts = await ExecuteScalarInt(conn, "SELECT COUNT(*) FROM dbo.Products WHERE IsActive=1", ct);
        int totalInvoices = await ExecuteScalarInt(conn, "SELECT COUNT(*) FROM dbo.SalesInvoices WHERE IsCancelled=0", ct);

        // balances from transfer lines (Vodafone Cash & InstaPay)
        decimal vodafone1 = await ExecuteScalarDecimal(conn, "SELECT ISNULL(CurrentBalance,0) FROM dbo.TransferLines WHERE PhoneNumber=N'01012345678'", ct);
        decimal vodafone2 = await ExecuteScalarDecimal(conn, "SELECT ISNULL(CurrentBalance,0) FROM dbo.TransferLines WHERE PhoneNumber=N'01098765432'", ct);
        decimal instapay1 = await ExecuteScalarDecimal(conn, "SELECT ISNULL(CurrentBalance,0) FROM dbo.TransferLines WHERE PhoneNumber=N'01100032165'", ct);
        decimal instapay2 = await ExecuteScalarDecimal(conn, "SELECT ISNULL(CurrentBalance,0) FROM dbo.TransferLines WHERE PhoneNumber=N'012985411111'", ct);

        var totalProfit = salesRev + repairRev + transferRev - returnsAmt;
        var netAmount = totalProfit;

        return new DashboardStats(
            salesRev, repairRev, transferRev, 0m, returnsAmt, totalProfit, netAmount,
            repairsInProg, repairsReady, lowStock,
            0m, vodafone1, vodafone2, instapay1, instapay2,
            totalCustomers, totalProducts, totalInvoices);
    }

    private static async Task<decimal> ExecuteScalarDecimal(SqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 15 };
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? 0m : Convert.ToDecimal(result);
    }

    private static async Task<int> ExecuteScalarInt(SqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 15 };
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }
}
