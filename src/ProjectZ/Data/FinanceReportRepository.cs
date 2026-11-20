using System.Data;
using Microsoft.Data.SqlClient;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Data;

/// <summary>The report has its own redacted projection. Never load the operational account/transfer details into the reporting UI.</summary>
public sealed class FinanceReportRepository(ConnectionSettingsProvider settings) : IFinanceReportSource
{
    public async Task<FinanceReportData> LoadAsync(ReportPeriod period,CancellationToken token)
    {
        try
        {
            await using var db=new SqlConnection(settings.GetConnectionString());
            await db.OpenAsync(token).ConfigureAwait(false);
            using var cmd=new SqlCommand("dbo.ReadFinanceReport",db){CommandType=CommandType.StoredProcedure,CommandTimeout=60};
            cmd.Parameters.Add("@FromUtc",SqlDbType.DateTime2).Value=period.StartUtc;
            cmd.Parameters.Add("@UntilUtc",SqlDbType.DateTime2).Value=period.EndUtc;
            await using var r=await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            DateTime Utc(int n)=>DateTime.SpecifyKind(r.GetDateTime(n),DateTimeKind.Utc);
            int? Id(int n)=>r.IsDBNull(n)?null:r.GetInt32(n);
            decimal? Money(int n)=>r.IsDBNull(n)?null:r.GetDecimal(n);
            async Task Next(){if(!await r.NextResultAsync(token).ConfigureAwait(false))throw new InvalidOperationException("نتائج تقرير المالية ناقصة. تحقق من سكربت 29.");}
            var accounts=new Dictionary<int,BookAccount>();
            while(await r.ReadAsync(token).ConfigureAwait(false)) accounts.Add(r.GetInt32(0),new(r.GetInt32(0),r.GetByte(1),r.GetString(2),r.GetString(3),
                r.GetBoolean(4),r.GetBoolean(5),Utc(6),Utc(7),r.GetDecimal(8),r.GetDecimal(9),r.GetDecimal(10),r.GetDecimal(11),r.GetDecimal(12),Money(13),Money(14),Money(15)));
            await Next().ConfigureAwait(false);
            var legs=new List<BookLeg>();
            while(await r.ReadAsync(token).ConfigureAwait(false)) legs.Add(new(r.GetInt32(0),r.GetInt32(1),Utc(2),r.GetBoolean(3),r.GetDecimal(4),r.GetDecimal(5),
                r.GetBoolean(6),r.GetInt32(7),(FinanceSource)r.GetInt32(8),Id(9),r.GetBoolean(10),r.IsDBNull(11)?"":r.GetString(11),r.GetString(12),r.IsDBNull(13)?null:r.GetGuid(13)));
            await Next().ConfigureAwait(false);
            var transfers=new Dictionary<int,FeeTransfer>();
            while(await r.ReadAsync(token).ConfigureAwait(false))transfers.Add(r.GetInt32(0),new(r.GetInt32(0),r.GetInt32(1),Id(2),r.GetByte(3),r.GetDecimal(4),r.GetDecimal(5),
                r.GetDecimal(6),r.GetString(7),r.GetInt32(8),Utc(9),r.GetByte(10)==1,r.IsDBNull(11)?null:Utc(11),Id(12),Id(13),Id(14),r.GetBoolean(15),r.GetBoolean(16)));
            await Next().ConfigureAwait(false);
            var usage=new List<LimitUse>();
            while(await r.ReadAsync(token).ConfigureAwait(false))usage.Add(new(r.GetInt32(0),r.GetBoolean(1),r.GetDecimal(2),r.GetDecimal(3)));
            await Next().ConfigureAwait(false);
            var actors=new Dictionary<int,string>();
            while(await r.ReadAsync(token).ConfigureAwait(false))actors.Add(r.GetInt32(0),r.GetString(1));
            await Next().ConfigureAwait(false);
            if(!await r.ReadAsync(token).ConfigureAwait(false))throw new InvalidOperationException("توقيت قراءة تقرير المالية مفقود.");
            return new(period,Utc(0),accounts,legs,transfers,usage,actors);
        }
        catch(SqlException ex) when(!token.IsCancellationRequested)
        {
            throw new InvalidOperationException(ex.Number switch {
                229 or 2812 or 207 or 208=>"تحقق من sql/29_EnableFinanceReports.sql وإذن تنفيذ تقرير المالية.",
                -2 or 1205=>"انشغل الخادم أثناء قراءة الدفتر. جرّب فترة أقصر ثم أعد التحميل.",
                _=>$"تعذر تحميل تقرير المالية من SQL Server (خطأ {ex.Number})." },ex);
        }
    }
}
