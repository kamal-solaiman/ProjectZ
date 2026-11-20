using System.Data;
using Microsoft.Data.SqlClient;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Data;

public sealed class OverallReportRepository(ConnectionSettingsProvider settings) : IOverallReportSource
{
    public async Task<OverallReportData> LoadAsync(ReportPeriod period, CancellationToken token)
    {
        try
        {
            await using var db = new SqlConnection(settings.GetConnectionString());
            await db.OpenAsync(token).ConfigureAwait(false);
            using var cmd = new SqlCommand("dbo.ReadOverallReport",db) {CommandType=CommandType.StoredProcedure,CommandTimeout=60};
            cmd.Parameters.Add("@FromDate",SqlDbType.Date).Value=period.From;
            cmd.Parameters.Add("@ThroughDate",SqlDbType.Date).Value=period.Through;
            await using var r=await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            var figures=new List<DailyFigure>(); var channels=new List<ChannelBalance>();
            InvalidOperationException Contract() => new("نتائج إجمالي التقارير ناقصة أو غير متوافقة. تحقّق من سكربت 31.");
            while(await r.ReadAsync(token).ConfigureAwait(false))
            {
                var measure=(OverallMeasure)r.GetInt16(1);
                if(!Enum.IsDefined(measure))throw Contract();
                figures.Add(new(DateOnly.FromDateTime(r.GetDateTime(0)),measure,r.GetDecimal(2),r.GetInt64(3),r.GetInt64(4)));
            }
            if(!await r.NextResultAsync(token).ConfigureAwait(false))throw Contract();
            while(await r.ReadAsync(token).ConfigureAwait(false)) channels.Add(new(r.GetByte(0),r.GetInt64(1),r.GetDecimal(2)));
            if(!await r.NextResultAsync(token).ConfigureAwait(false) || !await r.ReadAsync(token).ConfigureAwait(false))throw Contract();
            return new(period,DateTime.SpecifyKind(r.GetDateTime(0),DateTimeKind.Utc),figures,channels);
        }
        catch(SqlException ex) when(!token.IsCancellationRequested)
        {
            throw new InvalidOperationException(ex.Number switch {
                207 or 208 or 229 or 2812=>"تحقّق من تجهيز التقارير 26–30 وتركيب sql/31_EnableOverallReports.sql وإذن التنفيذ للكاشير.",
                50271=>"فترة المقارنة غير صالحة. اختر حتى 366 يومًا.",
                -2 or 1205=>"الخادم مشغول بقراءة الملخص. أعد المحاولة بفترة أقصر.",
                _=>$"تعذرت قراءة إجمالي التقارير من SQL Server (خطأ {ex.Number})."},ex);
        }
    }
}
