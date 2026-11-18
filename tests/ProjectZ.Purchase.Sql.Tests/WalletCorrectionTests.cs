using System.Collections.ObjectModel;
using Microsoft.Data.SqlClient;
using ProjectZ.Data;
using ProjectZ.Models;
using ProjectZ.Services;
using static SqlTest;

internal static class WalletCorrectionTests
{
    // Use only the disposable instance guarded by Program.cs. For mutable headers/accounts a
    // rowversion also detects a write that sets a value back to its original value.
    internal static string Footprint(SqlConnection db) => Scalar<string>(db, """
        SELECT CONCAT((SELECT Id,CurrentBalance,CONVERT(varchar(18),RowVersion,1) AS Version FROM dbo.FinancialAccounts ORDER BY Id FOR JSON PATH),N'|',
                      (SELECT Id,CONVERT(varchar(18),RowVersion,1) AS Version FROM dbo.CustomerTransfers ORDER BY Id FOR JSON PATH),N'|',
                      (SELECT * FROM dbo.FinancialTransactions ORDER BY Id FOR JSON PATH),N'|',
                      (SELECT * FROM dbo.AuditLog ORDER BY Id FOR JSON PATH));
        """);

    internal sealed record LegacyEdit(int Id, int FirstWallet, int SavedWallet);

    // Before installing sql/23, represent an edit made by the old client: one overwritten header,
    // a 100+1 deposit on A, its inverse and a 60-3 withdrawal on B, all sharing one source ID.
    internal static LegacyEdit SeedLegacyEdit(SqlConnection db, FinancialRepository repo, int cash, int actor)
    {
        var a = repo.SaveAccount(null, FinancialAccountKind.VodafoneCash, "قديم أ", "01060000001", 700, 0, 0, 0, 0, "", false).AccountId;
        var b = repo.SaveAccount(null, FinancialAccountKind.VodafoneCash, "قديم ب", "01060000002", 800, 0, 0, 0, 0, "", false).AccountId;
        var id = Scalar<int>(db, $"""
            INSERT dbo.CustomerTransfers(FinancialAccountId,OperationKind,CustomerNumber,Amount,Commission,CashDelta,WalletBalanceAfter,Note,ActorUserId)
            OUTPUT inserted.Id VALUES({b},2,N'01000000060',60,3,-57,860,N'آخر بيان محفوظ',{actor});
            """);
        Execute(db, $"""
            DECLARE @c decimal(19,2)=(SELECT CurrentBalance FROM dbo.FinancialAccounts WHERE Id={cash});
            INSERT dbo.FinancialTransactions(FinancialAccountId,IsIncoming,Category,Amount,BalanceAfter,Counterparty,Note,ActorUserId,SourceEntityType,SourceEntityId)
            SELECT wallet,incoming,category,amount,balance,customer,note,{actor},N'CustomerTransfer',{id} FROM (VALUES
                ({a},0,N'تحويل للعميل',100,600,N'01000000100',N'قبل التعديل'),({cash},1,N'تحويل من العميل',101,@c+101,N'01000000100',N''),
                ({a},1,N'عكس تعديل',100,700,N'01000000100',N''),({cash},0,N'عكس تعديل',101,@c,N'01000000100',N''),
                ({b},1,N'تعديل سجل',60,860,N'01000000060',N'تطبيق الأثر الجديد'),({cash},0,N'تعديل سجل',57,@c-57,N'01000000060',N'')) v(wallet,incoming,category,amount,balance,customer,note);
            UPDATE dbo.FinancialAccounts SET CurrentBalance=860 WHERE Id={b};
            UPDATE dbo.FinancialAccounts SET CurrentBalance=@c-57 WHERE Id={cash};
            """);
        return new(id, a, b);
    }

    internal static void Run(SqlConnection db, ConnectionSettingsProvider settings, int actor, int cash, LegacyEdit legacy)
    {
        var first = new FinancialRepository(settings, actor);
        var second = new FinancialRepository(settings, actor);
        const FinancialAccountKind vf = FinancialAccountKind.VodafoneCash, ip = FinancialAccountKind.InstaPay;
        decimal Balance(int id) => Scalar<decimal>(db, $"SELECT CurrentBalance FROM dbo.FinancialAccounts WHERE Id={id};");
        void Refused(Action request, int number, string reason)
        {
            var before = Footprint(db);
            try { request(); }
            catch (FinancialDataException ex) when (ex.InnerException is SqlException sql && sql.Number == number)
            {
                Check(Footprint(db) == before, reason + ": every balance, version, ledger and audit row is unchanged");
                return;
            }
            throw new InvalidOperationException("Expected financial refusal: " + reason);
        }

        var legacyRows = new ObservableCollection<VodafoneTransferOperation>();
        first.LoadVodafoneWalletMovementsInto(legacyRows);
        var active = legacyRows.Single(r => r.Id == legacy.Id && r.CanEditOrDelete);
        var original = legacyRows.Single(r => r.Id == legacy.Id && r.CategoryTitle == "تحويل للعميل");
        Check(active is { IsDeposit: false, Amount: 60, Commission: 3, CashDelta: -57, CustomerNumber: "01000000060", Note: "آخر بيان محفوظ" }
              && active.WalletId == legacy.SavedWallet && active.RowVersion.Length == 8
              && original is { CanEditOrDelete: false, Amount: 100, Commission: 0, CustomerNumber: "01000000100", Note: "قبل التعديل" }
              && original.WalletId == legacy.FirstWallet, "Legacy ledger displays each leg on its actual wallet; only the saved terms are actionable");
        var cashBeforeLegacy = Balance(cash);
        first.ReverseCustomerTransfer(active.Id, active.RowVersion, vf, "عكس تحويل قديم بعد المطابقة");
        Check(Balance(legacy.FirstWallet) == 700 && Balance(legacy.SavedWallet) == 800 && Balance(cash) == cashBeforeLegacy + 57,
            "Fresh reversal of a legacy in-place edit undoes only the reconciled saved effect");

        foreach (var provider in new[] { vf, ip })
        {
            var a = first.SaveAccount(null, provider, "اختبار أ", "01060000003", 900, 0, 0, 0, 0, "", false).AccountId;
            var b = first.SaveAccount(null, provider, "اختبار ب", "01060000004", 300, 0, 0, 0, 0, "", false).AccountId;
            var baseline = Balance(cash);
            var issued = first.ExecuteCustomerTransfer(provider, new(a, true, "01000000200", 200, 5, "الأصل"));
            var left = ReadVersion(first, provider, issued.TransferId);
            var right = ReadVersion(second, provider, issued.TransferId);
            Check(issued.RowVersion.SequenceEqual(left) && left.SequenceEqual(right) && Balance(a) == 700 && Balance(cash) == baseline + 205,
                $"{provider}: both terminals opened the same saved deposit");
            CustomerTransferTerms withdrawal = new(b, false, "01000000030", 30, 2, "التصحيح");
            Refused(() => first.ReverseCustomerTransfer(issued.TransferId, right, provider == vf ? ip : vf, ""), 50234, "Wrong provider");
            first.SetAccountActive(b, false);
            Refused(() => first.UpdateCustomerTransfer(issued.TransferId, left, provider, withdrawal), 50234, "Stopped replacement wallet");
            first.SetAccountActive(b, true);
            var corrected = first.UpdateCustomerTransfer(issued.TransferId, left, provider, withdrawal);
            var id = corrected.TransferId;
            Check(id != issued.TransferId && Balance(a) == 900 && Balance(b) == 330 && Balance(cash) == baseline - 28
                  && corrected.CashAccountId == cash && corrected.WalletAccountId == b && corrected.CashBalanceAfter == Balance(cash),
                $"{provider}: correction restores A, applies the withdrawal to B and pays only 28 cash");
            Check(Scalar<int>(db, $"""
                SELECT COUNT(*) FROM dbo.CustomerTransfers old JOIN dbo.CustomerTransfers child ON child.CorrectsTransferId=old.Id
                WHERE old.Id={issued.TransferId} AND old.OperationStatus=1 AND old.ReversedByUserId={actor} AND old.ReversedAtUtc IS NOT NULL
                  AND old.Amount=200 AND old.Commission=5 AND old.WalletBalanceAfter=700 AND old.Note=N'الأصل'
                  AND child.Id={id} AND child.OperationStatus=0 AND child.OperationKind=2 AND child.Amount=30 AND child.CashDelta=-28
                  AND child.FinancialAccountId={b} AND child.CashAccountId={cash} AND child.Note=N'التصحيح';
                """) == 1 && Scalar<int>(db, $"""
                SELECT COUNT(*) FROM dbo.AuditLog WHERE EntityId=N'{issued.TransferId}' AND ActionName=N'CorrectCustomerTransfer'
                  AND ActorUserId={actor} AND JSON_VALUE(OldValuesJson,'$.Id')='{issued.TransferId}'
                  AND JSON_VALUE(NewValuesJson,'$.Id')='{id}' AND JSON_VALUE(NewValuesJson,'$.CorrectsTransferId')='{issued.TransferId}';
                """) == 1, "Original terms, counter-entry actor and linked corrected terms remain auditable");
            var fresh = ReadVersion(second, provider, id);
            Check(fresh.SequenceEqual(corrected.RowVersion) && !fresh.SequenceEqual(left), "The replacement opens on its own version");
            Refused(() => second.UpdateCustomerTransfer(issued.TransferId, right, provider, withdrawal), 50232, "Stale edit and replay");
            Refused(() => second.ReverseCustomerTransfer(issued.TransferId, right, provider, "تأكيد حذف قديم"), 50232, "Stale reverse/delete after edit");
            Refused(() => second.ReverseCustomerTransfer(id, left, provider, ""), 50232, "Wrong version even though the replacement is active");
            second.ReverseCustomerTransfer(id, fresh, provider, "عكس التصحيح");
            Check(Balance(a) == 900 && Balance(b) == 300 && Balance(cash) == baseline, "Reversing the replacement restores the exact three starting balances");
            Refused(() => second.ReverseCustomerTransfer(id, fresh, provider, ""), 50232, "Replayed reversal");
            Refused(() => second.UpdateCustomerTransfer(id, ReadVersion(second, provider, id), provider, withdrawal), 50232, "Freshly read but reversed row");

            // Simultaneously deliver two requests from separate connections with the version shown
            // to each till. One edit/edit race and one edit/reverse race; either winner is legal.
            var root = first.ExecuteCustomerTransfer(provider, new(a, true, "01000000010", 10, 1, "تزامن"));
            var v1 = ReadVersion(first, provider, root.TransferId);
            var v2 = ReadVersion(second, provider, root.TransferId);
            using var ready = new Barrier(2);
            Task<(bool Won, int? Child)> AtTill(Func<int?> action) => Task.Run(() =>
            {
                if (!ready.SignalAndWait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Wallet race barrier");
                try { return (true, action()); }
                catch (FinancialDataException ex) when (ex.InnerException is SqlException { Number: 50232 }) { return (false, (int?)null); }
            });
            var t1 = AtTill(() => first.UpdateCustomerTransfer(root.TransferId, v1, provider, withdrawal).TransferId);
            var t2 = AtTill(() =>
            {
                if (provider == vf) return second.UpdateCustomerTransfer(root.TransferId, v2, provider, withdrawal).TransferId;
                second.ReverseCustomerTransfer(root.TransferId, v2, provider, "تأكيد العكس");
                return null;
            });
            var results = Task.WhenAll(t1, t2).GetAwaiter().GetResult();
            var winner = results.Single(r => r.Won);
            var children = winner.Child.HasValue ? 1 : 0;
            Check(results.Count(r => !r.Won) == 1 && Balance(a) == 900 && Balance(b) == 300 + 30 * children && Balance(cash) == baseline - 28 * children
                  && Scalar<int>(db, $"SELECT COUNT(*) FROM dbo.CustomerTransfers WHERE CorrectsTransferId={root.TransferId};") == children
                  && Scalar<int>(db, $"SELECT COUNT(*) FROM dbo.FinancialTransactions WHERE SourceEntityType=N'CustomerTransfer' AND SourceEntityId={root.TransferId};") == 4
                  && Scalar<int>(db, $"SELECT COUNT(*) FROM dbo.AuditLog WHERE EntityType=N'CustomerTransfer' AND EntityId=N'{root.TransferId}';") == 2,
                $"{provider}: exactly one concurrent request committed its movements and history");
        }

        // No cash leg exists when commission equals the amount withdrawn. The saved cash ID still
        // matters, and a failure on the final audit insert must roll back both sides of a correction.
        var zero = first.ExecuteCustomerTransfer(vf, new(legacy.FirstWallet, false, "01000000005", 5, 5, "صفر نقدي"));
        var zeroCash = Balance(cash);
        Execute(db, "CREATE TRIGGER dbo.WalletTest_FailAudit ON dbo.AuditLog AFTER INSERT AS THROW 50299, N'اختبار تراجع المعاملة', 1;");
        try { Refused(() => first.UpdateCustomerTransfer(zero.TransferId, zero.RowVersion, vf, new(legacy.SavedWallet, true, "01000000050", 50, 3, "")), 50299, "Last-write failure"); }
        finally { Execute(db, "DROP TRIGGER dbo.WalletTest_FailAudit;"); }
        Execute(db, $"UPDATE dbo.FinancialAccounts SET IsActive=0 WHERE Id={cash};");
        try { Refused(() => first.ReverseCustomerTransfer(zero.TransferId, zero.RowVersion, vf, ""), 50233, "Stopped cash account"); }
        finally { Execute(db, $"UPDATE dbo.FinancialAccounts SET IsActive=1 WHERE Id={cash};"); }
        first.ReverseCustomerTransfer(zero.TransferId, zero.RowVersion, vf, "");
        Check(Balance(cash) == zeroCash && Scalar<int>(db, $"SELECT COUNT(*) FROM dbo.FinancialTransactions WHERE SourceEntityType=N'CustomerTransfer' AND SourceEntityId={zero.TransferId};") == 2,
            "Zero cash effect produces only the two opposite wallet legs");

        // Simulate an inconsistent historical header, not a proposed fix to historical balances.
        var mismatch = first.ExecuteCustomerTransfer(vf, new(legacy.FirstWallet, true, "01000000012", 12, 1, ""));
        Execute(db, $"UPDATE dbo.CustomerTransfers SET Commission=4, CashDelta=16 WHERE Id={mismatch.TransferId};");
        Refused(() => first.ReverseCustomerTransfer(mismatch.TransferId, ReadVersion(second, vf, mismatch.TransferId), vf, ""), 50235, "Fresh version but inconsistent ledger");
        using var raw = new SqlConnection(settings.GetConnectionString());
        raw.Open();
        try { Execute(raw, $"UPDATE dbo.CustomerTransfers SET Commission=1 WHERE Id={mismatch.TransferId};"); throw new InvalidOperationException("Unexpected direct write permission"); }
        catch (SqlException ex) when (ex.Number is 229 or 230) { Check(true, "Limited client cannot bypass the version guard with an in-place header update"); }
        Check(Scalar<int>(raw, "SELECT COALESCE(HAS_PERMS_BY_NAME('dbo.WriteCustomerTransferEffect','OBJECT','EXECUTE'),0);") == 0, "Internal posting helper is not executable by the client");
    }

    private static byte[] ReadVersion(FinancialRepository repo, FinancialAccountKind provider, int id)
    {
        if (provider == FinancialAccountKind.VodafoneCash)
        {
            var rows = new ObservableCollection<VodafoneTransferOperation>();
            repo.LoadVodafoneWalletMovementsInto(rows);
            return rows.Single(r => r.Id == id && r.IsCustomerTransfer).RowVersion;
        }
        var other = new ObservableCollection<InstapayTransferOperation>();
        repo.LoadInstapayWalletMovementsInto(other);
        return other.Single(r => r.Id == id && r.IsCustomerTransfer).RowVersion;
    }
}
