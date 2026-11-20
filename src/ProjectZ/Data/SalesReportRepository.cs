using System.Data;
using Microsoft.Data.SqlClient;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Data;

public sealed class SalesReportRepository(ConnectionSettingsProvider settings) : ISalesReportSource
{
    public async Task<SalesReportData> LoadAsync(SalesReportPeriod period, CancellationToken token)
    {
        try
        {
            await using var db = new SqlConnection(settings.GetConnectionString());
            await db.OpenAsync(token).ConfigureAwait(false);
            using var cmd = new SqlCommand("dbo.ReadSalesReport", db) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 };
            cmd.Parameters.Add("@FromUtc", SqlDbType.DateTime2).Value = period.StartUtc;
            cmd.Parameters.Add("@UntilUtc", SqlDbType.DateTime2).Value = period.EndUtc;
            await using var r = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            ReportDocument Doc() => new(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2), r.GetString(3), r.GetInt32(4), r.GetString(5));
            DateTime Utc(int n) => DateTime.SpecifyKind(r.GetDateTime(n), DateTimeKind.Utc);
            async Task Next()
            {
                if (!await r.NextResultAsync(token).ConfigureAwait(false)) throw new InvalidOperationException("نتائج التقرير غير مكتملة؛ تحقق من إصدار sql/26_EnableSalesReports.sql.");
            }
            var docs = new List<ReportInvoice>(); var lines = new List<ReportLine>(); var events = new List<SalesReportEvent>();
            while (await r.ReadAsync(token).ConfigureAwait(false))
                docs.Add(new(Doc(), Utc(6), r.GetByte(7), r.GetDecimal(8), r.GetDecimal(9), r.GetDecimal(10), r.GetDecimal(11),
                    r.GetDecimal(12), r.GetDecimal(13), r.GetDecimal(14)));
            await Next().ConfigureAwait(false);
            while (await r.ReadAsync(token).ConfigureAwait(false))
                lines.Add(new(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
                    r.GetBoolean(7), r.GetInt32(8), r.GetInt32(9), r.GetDecimal(10), r.GetDecimal(11), r.GetDecimal(12), r.GetDecimal(13),
                    r.GetDecimal(14), r.GetDecimal(15), r.GetDecimal(16)));
            await Next().ConfigureAwait(false);
            while (await r.ReadAsync(token).ConfigureAwait(false))
                events.Add(new(Doc(), (SalesEventKind)r.GetInt32(6), r.GetInt32(7), Utc(9), r.IsDBNull(10) ? null : r.GetInt32(10),
                    r.GetString(11), r.GetString(12), r.IsDBNull(13) ? "" : r.GetString(13), r.IsDBNull(14) ? null : r.GetInt64(14),
                    r.IsDBNull(15) ? null : r.GetDecimal(15), r.IsDBNull(16) ? null : r.GetDecimal(16), r.IsDBNull(17) ? null : r.GetInt32(17),
                    r.IsDBNull(18) ? null : r.GetByte(18), r.IsDBNull(19) ? null : r.GetString(19), r.GetDecimal(20), r.GetDecimal(21), r.IsDBNull(8) ? 0 : r.GetInt32(8)));
            await Next().ConfigureAwait(false);
            if (!await r.ReadAsync(token).ConfigureAwait(false)) throw new InvalidOperationException("لم يكتمل تحميل تقرير المبيعات.");
            return new(period, Utc(0), docs, lines, events);
        }
        catch (SqlException ex) when (!token.IsCancellationRequested)
        {
            throw new InvalidOperationException(ex.Number switch {
                2812 or 208 or 207 or 229 => "تعذرت قراءة تقرير المبيعات. راجع مخطط 07 و22 ثم sql/26_EnableSalesReports.sql وصلاحية تنفيذ إجراء التقرير، دون ترقية حساب الكاشير.",
                -2 or 1205 => "انتهت مهلة التقرير أو انشغلت القاعدة. حدّد فترة أقصر وأعد التحديث.",
                _ => $"تعذرت قراءة تقرير المبيعات من SQL Server (رقم {ex.Number}). تحقق من الاتصال ثم أعد المحاولة." }, ex);
        }
    }
}
