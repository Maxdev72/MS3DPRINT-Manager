using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Purchases;

public sealed class ExpensePlansTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"expense-plan-tests-"+Guid.NewGuid());
    private ExpensePlanStore Store=>new(_root);
    private static ExpensePlan Floa() => ExpensePlans.Create(Guid.NewGuid(),ExpensePlanKind.Financing,"FLOA","Tablette",35.08m,new(2026,11,5),1,new(2028,9,5),34.99m);

    [Fact]
    public void Bank_example_matches_and_time_alone_never_marks_an_installment_paid()
    {
        var plan=Floa();var summary=ExpensePlans.Summarize(plan,new(2027,1,1));
        Assert.Equal(23,summary.RemainingCount);Assert.Equal(806.75m,summary.RemainingAmount);
        Assert.Equal(new DateOnly(2026,11,5),summary.NextDue);Assert.Equal(new DateOnly(2028,9,5),summary.LastDue);
        Assert.Equal(34.99m,summary.LastAmount);
    }

    [Fact]
    public void Confirming_then_reversing_a_payment_keeps_schedule_and_uses_version_checks()
    {
        var plan=Floa();Store.Create(plan);
        var first=ExpensePlans.Schedule(plan,new(2026,11,5))[0];
        var paid=ExpensePlans.ConfirmPayment(plan,first.Id,new(2026,11,5),new(2026,11,5));
        paid=Store.Update(paid,plan.UpdatedAt);
        Assert.Equal(771.67m,ExpensePlans.Summarize(paid,new(2026,11,5)).RemainingAmount);
        Assert.Throws<ProfileConflictException>(()=>Store.Update(plan,plan.UpdatedAt));
        var reopened=ExpensePlans.ReversePayment(paid,first.Id);
        reopened=Store.Update(reopened,paid.UpdatedAt);
        Assert.Equal(806.75m,ExpensePlans.Summarize(reopened,new(2026,11,5)).RemainingAmount);
        Assert.False(Directory.Exists(Path.Combine(_root,".ms3dprint-manager","purchases")));
    }

    [Fact]
    public void Recurrence_anchors_day_31_and_open_ended_totals_are_only_a_forecast()
    {
        var plan=ExpensePlans.Create(Guid.NewGuid(),ExpensePlanKind.Recurring,"Assureur","Assurance",10m,new(2026,1,31),1,null,null);
        var rows=ExpensePlans.Schedule(plan,new(2026,1,31));
        Assert.Equal(new DateOnly(2026,2,28),rows[1].DueOn);
        Assert.Equal(new DateOnly(2026,3,31),rows[2].DueOn);
        Assert.True(ExpensePlans.Summarize(plan,new(2026,1,31)).IsForecast);
        Assert.Equal(rows.Select(r=>r.Id),ExpensePlans.Schedule(plan,new(2026,1,31)).Select(r=>r.Id));
    }

    [Fact]
    public void Bank_table_keeps_values_and_rejects_invalid_lines()
    {
        var rows=ExpenseScheduleImport.Parse("Date;Montant;Capital;Intérêts;Assurance;Capital restant dû\n05/11/2026;35,08;30;4,08;1;776,75\n05/12/2026;34,99;30;3,99;1;746,75",Guid.NewGuid());
        Assert.Equal(2,rows.Count);Assert.Equal(4.08m,rows[0].Interest);
        Assert.Equal(776.75m,rows[0].RemainingCapital);
        Assert.Throws<ArgumentException>(()=>ExpenseScheduleImport.Parse("Date;Montant\n31/02/2026;12",Guid.NewGuid()));
        Assert.Throws<ArgumentException>(()=>ExpenseScheduleImport.Parse("Date;Montant\n05/11/2026;-2",Guid.NewGuid()));
    }

    [Fact]
    public void Import_rejects_split_decimal_columns_and_accepts_quoted_decimals()
    {
        Assert.Throws<ArgumentException>(()=>ExpenseScheduleImport.Parse("Date,Montant\n05/11/2026,35,08",Guid.NewGuid()));
        Assert.Throws<ArgumentException>(()=>ExpenseScheduleImport.Parse("05/11/2026,1,234.56",Guid.NewGuid()));
        Assert.Equal(35.08m,ExpenseScheduleImport.Parse("Date,Montant\n05/11/2026,\"35,08\"",Guid.NewGuid())[0].Amount);
    }

    [Fact]
    public void Recurring_payment_ids_survive_a_change_of_regional_calendar()
    {
        var culture=System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var plan=ExpensePlans.Create(Guid.NewGuid(),ExpensePlanKind.Recurring,"Assureur","Assurance",10m,new(2026,1,31),1,null,null);
            var rows=ExpensePlans.Schedule(plan,new(2026,1,31));
            System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("th-TH");
            Assert.Equal(rows.Select(r=>r.Id),ExpensePlans.Schedule(plan,new(2026,1,31)).Select(r=>r.Id));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture=culture; }
    }

    [Fact]
    public void Bank_table_retains_regular_quarterly_frequency()
    {
        var p=ExpensePlans.Create(Guid.NewGuid(),ExpensePlanKind.Financing,"Banque","Matériel",100m,new(2026,1,31),3,new(2027,1,31),null);
        Assert.Equal(3,ExpensePlans.FromTable(p.Id,p.Organization,p.Label,p.Installments).IntervalMonths);
    }

    [Fact]
    public void Unreadable_plan_does_not_hide_valid_plans_and_future_payment_is_rejected()
    {
        var p=Floa();Store.Create(p);
        File.WriteAllText(Path.Combine(_root,".ms3dprint-manager","expense-plans","broken.json"),"{invalid");
        var store=Store;Assert.Single(store.LoadReadable());Assert.Single(store.ReadErrors);
        Assert.Throws<ArgumentException>(()=>ExpensePlans.ConfirmPayment(p,p.Installments[0].Id,new(2026,11,6),new(2026,11,5)));
        Assert.True(File.Exists(Path.Combine(_root,".ms3dprint-manager","expense-plans","broken.json")));
    }

    [Fact]
    public void Editing_cannot_remove_or_change_an_already_confirmed_payment()
    {
        var plan=Floa();Store.Create(plan);
        var row=plan.Installments[0];
        var paid=Store.Update(ExpensePlans.ConfirmPayment(plan,row.Id,new(2026,11,5),new(2026,11,5)),plan.UpdatedAt);
        Assert.Throws<InvalidOperationException>(()=>Store.Update(paid with {Installments=paid.Installments.Skip(1).ToArray()},paid.UpdatedAt));
        var renamed=Store.Update(paid with {Label="Tablette atelier"},paid.UpdatedAt);
        Assert.Single(Store.LoadReadable().Single().Payments);
        Assert.Equal("Tablette atelier",renamed.Label);
    }

    public void Dispose(){if(Directory.Exists(_root))Directory.Delete(_root,true);}
}
