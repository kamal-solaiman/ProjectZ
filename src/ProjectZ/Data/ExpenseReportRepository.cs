using System.Data;
using Microsoft.Data.SqlClient;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Data;

/// <summary>Typed, read-only snapshot. No operational invoice or private text is fetched by the reporting screen.</summary>
public sealed class ExpenseReportRepository(ConnectionSettingsProvider settings) : IExpenseReportSource
{
    public async Task<ExpenseReportData> LoadAsync(ReportPeriod period, CancellationToken token)
    {
        var previous = ExpenseReportData.PreviousOf(period);
        try
        {
            await using var db = new SqlConnection(settings.GetConnectionString());
            await db.OpenAsync(token).ConfigureAwait(false);
            using var cmd = new SqlCommand("dbo.ReadExpenseReport", db) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 };
            cmd.Parameters.Add("@FromDate", SqlDbType.Date).Value = period.From;
            cmd.Parameters.Add("@ThroughDate", SqlDbType.Date).Value = period.Through;
            cmd.Parameters.Add("@PreviousStartUtc", SqlDbType.DateTime2).Value = previous.StartUtc;
            cmd.Parameters.Add("@UntilUtc", SqlDbType.DateTime2).Value = period.EndUtc;
            await using var r = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            DateTime Utc(int i) => DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Utc);
            async Task Next()
            {
                if (!await r.NextResultAsync(token).ConfigureAwait(false)) throw new InvalidOperationException("نتائج تقرير المصروفات ناقصة. تحقق من سكربت 30.");
            }
            var docs = new List<ExpenseDocument>();
            while (await r.ReadAsync(token).ConfigureAwait(false))
                docs.Add(new(r.GetInt32(0), r.GetString(1), DateOnly.FromDateTime(r.GetDateTime(2)), r.GetDecimal(3), r.GetInt32(4), r.GetString(5), r.GetInt32(6), Utc(7)));
            await Next().ConfigureAwait(false);
            var shares = new List<ExpenseShare>();
            while (await r.ReadAsync(token).ConfigureAwait(false)) shares.Add(new(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetDecimal(3)));
            await Next().ConfigureAwait(false);
            var legs = new List<ExpensePosting>();
            while (await r.ReadAsync(token).ConfigureAwait(false))
                legs.Add(new(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), Utc(3), r.GetBoolean(4), r.GetDecimal(5), r.GetDecimal(6), r.GetInt32(7)));
            await Next().ConfigureAwait(false);
            var accounts = new List<ExpenseReportAccount>();
            while (await r.ReadAsync(token).ConfigureAwait(false)) accounts.Add(new(r.GetInt32(0), r.GetByte(1), r.GetString(2), r.GetBoolean(3)));
            await Next().ConfigureAwait(false);
            var actors = new Dictionary<int, string>();
            while (await r.ReadAsync(token).ConfigureAwait(false)) actors.Add(r.GetInt32(0), r.GetString(1));
            await Next().ConfigureAwait(false);
            if (!await r.ReadAsync(token).ConfigureAwait(false)) throw new InvalidOperationException("توقيت قراءة تقرير المصروفات مفقود.");
            return new(period, Utc(0), docs, shares, legs, accounts, actors);
        }
        catch (SqlException ex) when (!token.IsCancellationRequested)
        {
            throw new InvalidOperationException(ex.Number switch {
                207 or 208 or 229 or 2812 => "تحقق من sql/30_EnableExpenseReports.sql بعد ترقية المصروفات 21 ومن إذن التنفيذ للحساب المحدود.",
                50269 => "فترة المصروفات أو حدود المقارنة غير صالحة.",
                -2 or 1205 => "انشغل الخادم أثناء قراءة المصروفات. أعد التحميل أو اختر فترة أقصر.",
                _ => $"تعذر تحميل تقرير المصروفات من SQL Server (خطأ {ex.Number})."
            }, ex);
        }
    }
}
