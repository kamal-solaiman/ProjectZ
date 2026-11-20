using System.Data;
using Microsoft.Data.SqlClient;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Data;

public sealed class MaintenanceReportRepository(ConnectionSettingsProvider settings) : IMaintenanceReportSource
{
    public async Task<MaintenanceReportData> LoadAsync(ReportPeriod period,CancellationToken token)
    {
        try
        {
            await using var db=new SqlConnection(settings.GetConnectionString());
            await db.OpenAsync(token).ConfigureAwait(false);
            using var cmd=new SqlCommand("dbo.ReadMaintenanceReport",db) {CommandType=CommandType.StoredProcedure,CommandTimeout=60};
            cmd.Parameters.Add("@FromUtc",SqlDbType.DateTime2).Value=period.StartUtc;
            cmd.Parameters.Add("@UntilUtc",SqlDbType.DateTime2).Value=period.EndUtc;
            await using var r=await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            DateTime Utc(int n)=>DateTime.SpecifyKind(r.GetDateTime(n),DateTimeKind.Utc);
            DateTime? Date(int n)=>r.IsDBNull(n)?null:Utc(n);
            int? Id(int n)=>r.IsDBNull(n)?null:r.GetInt32(n);
            string Name(int n)=>r.IsDBNull(n)?"":r.GetString(n);
            async Task Next() { if(!await r.NextResultAsync(token).ConfigureAwait(false)) throw new InvalidOperationException("نتائج تقرير الصيانة غير مكتملة. راجع سكربت 28."); }
            var orders=new Dictionary<int,ServiceTicket>();
            while(await r.ReadAsync(token).ConfigureAwait(false))
                orders.Add(r.GetInt32(0),new(r.GetInt32(0),r.GetString(1),Utc(2),Date(3),Date(4),r.GetByte(5),r.GetByte(6),r.GetString(7).Trim(),
                    r.GetInt32(8),r.GetString(9),Id(10),r.GetString(11),r.GetBoolean(12),r.GetString(13),r.GetString(14),r.GetString(15),r.GetString(16),r.GetInt32(17)));
            await Next().ConfigureAwait(false);
            var lines=new List<ServiceLine>();
            while(await r.ReadAsync(token).ConfigureAwait(false)) lines.Add(new(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetInt32(4),r.GetBoolean(5),Id(6),Id(7)));
            await Next().ConfigureAwait(false);
            var payments=new List<ServicePayment>();
            while(await r.ReadAsync(token).ConfigureAwait(false)) payments.Add(new(r.GetInt32(0),r.GetInt32(1),Utc(2),r.GetByte(3),r.GetByte(4),r.GetDecimal(5),r.GetInt32(6),
                r.GetString(7),r.GetByte(8),r.GetInt32(9),r.GetString(10),Id(11),r.GetBoolean(12),r.GetBoolean(13)));
            await Next().ConfigureAwait(false);
            var activities=new List<ServiceActivity>();
            while(await r.ReadAsync(token).ConfigureAwait(false)) activities.Add(new(r.GetInt32(0),r.GetInt32(1),Utc(2),r.GetInt32(3),r.GetString(4),r.GetInt32(5),r.IsDBNull(6)?null:r.GetByte(6)));
            await Next().ConfigureAwait(false);
            var parts=new Dictionary<int,WorkshopPart>();
            while(await r.ReadAsync(token).ConfigureAwait(false)) parts.Add(r.GetInt32(0),new(r.GetInt32(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetInt32(5),
                r.GetInt32(6),r.GetInt32(7),r.GetString(8),r.GetBoolean(9),Id(10),Name(11),r.GetBoolean(12)));
            await Next().ConfigureAwait(false);
            var moves=new List<WorkshopMove>();
            while(await r.ReadAsync(token).ConfigureAwait(false)) moves.Add(new(r.GetInt32(0),r.GetInt32(1),r.GetByte(2),r.GetInt32(3),r.GetInt32(4),r.GetInt32(5),Utc(6),Id(7),Id(8),Name(9),r.GetBoolean(10),r.GetInt32(11),r.GetString(12),Id(13),Name(14),r.GetBoolean(15)));
            await Next().ConfigureAwait(false);
            if(!await r.ReadAsync(token).ConfigureAwait(false)) throw new InvalidOperationException("لم يكتمل تحميل تقرير الصيانة.");
            return new(period,Utc(0),orders,lines,payments,activities,parts,moves);
        }
        catch(SqlException ex) when(!token.IsCancellationRequested)
        {
            throw new InvalidOperationException(ex.Number switch {
                2812 or 207 or 208 or 229=>"تحقق من تسوية الصيانة 24 ثم sql/28_EnableMaintenanceReports.sql وصلاحية تنفيذ التقرير.",
                -2 or 1205=>"الخادم مشغول. اختر فترة أقصر ثم أعد التحميل.",
                _=>$"تعذرت قراءة تقارير الصيانة من SQL Server (رقم {ex.Number})." },ex);
        }
    }
}
