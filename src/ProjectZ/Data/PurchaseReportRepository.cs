using System.Data;
using Microsoft.Data.SqlClient;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Data;

public sealed class PurchaseReportRepository(ConnectionSettingsProvider settings) : IPurchaseReportSource
{
    public async Task<PurchaseReportData> LoadAsync(ReportPeriod period, CancellationToken token)
    {
        try
        {
            await using var db = new SqlConnection(settings.GetConnectionString());
            await db.OpenAsync(token).ConfigureAwait(false);
            using var cmd = new SqlCommand("dbo.ReadPurchaseReport", db) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 };
            cmd.Parameters.Add("@FromUtc", SqlDbType.DateTime2).Value = period.StartUtc;
            cmd.Parameters.Add("@UntilUtc", SqlDbType.DateTime2).Value = period.EndUtc;
            await using var r = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            DateTime Utc(int n) => DateTime.SpecifyKind(r.GetDateTime(n), DateTimeKind.Utc);
            int? OptionalId(int n) => r.IsDBNull(n) ? null : r.GetInt32(n);
            decimal? OptionalMoney(int n) => r.IsDBNull(n) ? null : r.GetDecimal(n);
            async Task Next()
            {
                if (!await r.NextResultAsync(token).ConfigureAwait(false))
                    throw new InvalidOperationException("نتائج المشتريات غير مكتملة. تحقّق من إصدار سكربت 27.");
            }
            var docs = new Dictionary<int,PurchaseReportDocument>();
            while (await r.ReadAsync(token).ConfigureAwait(false))
                docs.Add(r.GetInt32(0), new(r.GetInt32(0), r.GetString(1), r.GetString(2), OptionalId(3), r.GetString(4),
                    OptionalId(5), r.GetString(6), r.GetBoolean(7), r.GetBoolean(8)));
            await Next().ConfigureAwait(false);
            var invoices = new List<PurchaseReportInvoice>();
            while (await r.ReadAsync(token).ConfigureAwait(false))
                invoices.Add(new(r.GetInt32(0), Utc(1), Utc(2), r.GetByte(3), r.GetDecimal(4), r.GetDecimal(5),
                    r.GetDecimal(6), r.GetDecimal(7), r.GetDecimal(8), r.GetDecimal(9), r.GetDecimal(10), r.GetDecimal(11),
                    r.GetDecimal(12), r.GetDecimal(13), r.GetBoolean(14)));
            await Next().ConfigureAwait(false);
            var lines = new List<PurchaseReportLine>();
            while (await r.ReadAsync(token).ConfigureAwait(false))
                lines.Add(new(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3), r.GetString(4), r.GetString(5),
                    r.GetString(6), r.GetInt32(7), r.GetInt32(8), r.GetInt32(9), r.GetDecimal(10), r.GetDecimal(11), r.GetDecimal(12)));
            await Next().ConfigureAwait(false);
            var events = new List<PurchaseFact>();
            while (await r.ReadAsync(token).ConfigureAwait(false))
                events.Add(new((PurchaseFactKind)r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), OptionalId(4),
                    Utc(5), OptionalId(6), r.GetString(7), r.GetString(8), r.GetString(9), OptionalMoney(10), OptionalMoney(11),
                    OptionalMoney(12), OptionalId(13), r.IsDBNull(14) ? null : r.GetByte(14), r.IsDBNull(15) ? null : r.GetString(15),
                    OptionalId(16), OptionalId(17), r.GetBoolean(18)));
            await Next().ConfigureAwait(false);
            var returns = new List<PurchaseReportReturnLine>();
            while (await r.ReadAsync(token).ConfigureAwait(false))
                returns.Add(new(r.GetInt32(0),r.GetInt32(1),r.GetString(2),Utc(3),r.GetInt32(4),r.GetString(5),r.GetString(6),
                    r.GetString(7),r.GetInt32(8),r.GetDecimal(9),r.GetInt64(10),r.GetInt64(11)));
            await Next().ConfigureAwait(false);
            var notes = new List<PurchaseReportNote>();
            while (await r.ReadAsync(token).ConfigureAwait(false))
                notes.Add(new(r.GetInt32(0),r.GetInt32(1),r.GetString(2),Utc(3),r.GetDecimal(4),r.GetDecimal(5),r.GetDecimal(6),r.GetDecimal(7)));
            await Next().ConfigureAwait(false);
            if (!await r.ReadAsync(token).ConfigureAwait(false)) throw new InvalidOperationException("لم يكتمل تحميل تقرير المشتريات.");
            return new(period, Utc(0), docs, invoices, lines, events, returns, notes);
        }
        catch (SqlException ex) when (!token.IsCancellationRequested)
        {
            throw new InvalidOperationException(ex.Number switch {
                2812 or 208 or 207 or 229 => "تعذرت قراءة المشتريات. تحقق من 07 و25 ثم sql/27_EnablePurchaseReports.sql وصلاحية تنفيذ إجراء التقرير.",
                -2 or 1205 => "القاعدة مشغولة أو انتهت المهلة. اختر فترة أقصر ثم أعد التحديث.",
                _ => $"تعذرت قراءة تقرير المشتريات من SQL Server (رقم {ex.Number}). تحقق من الاتصال." }, ex);
        }
    }
}
