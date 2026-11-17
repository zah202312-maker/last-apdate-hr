using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HR_Mangment2.Appreciation;
using HR_Mangment2.Delegations;
using HR_Mangment2.EmploymentContracts;
using HR_Mangment2.Evaluations;
using HR_Mangment2.Holidays;
using HR_Mangment2.Notices;
using HR_Mangment2.Requests;
using HR_Mangment2.Store;
using HR_Mangment2.Warnings;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;

namespace HR_Mangment2.DemoData;

public sealed partial class DemoDataSeeder
{
    /// <summary>A minimal PDF, enough for the file checks; the demo letters are not meant to be opened.</summary>
    private static readonly byte[] DemoPdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% demo letter\n%%EOF");

    private async Task SeedCompanyLifeAsync()
    {
        await SeedHolidayAsync();
        await SeedCircularsAndNoticesAsync();
        await SeedStoreAsync();
        await SeedWarningsAndLettersAsync();
        await SeedDelegationAsync();
        await SeedEvaluationsAsync();
    }

    // ---------------------------------------------------------------- holiday

    private async Task SeedHolidayAsync()
    {
        // Victory Day, 10 December, when it is still ahead; otherwise a day a month from now.
        var victory = new DateOnly(_today.Year, 12, 10);
        var (date, nameAr, nameEn) = victory > _today
            ? (victory, "يوم النصر", "Victory Day")
            : (_today.AddDays(30), "عطلة رسمية", "Public holiday");
        try
        {
            await AsAsync(_hrUserId, services => services.GetRequiredService<IHolidayAppService>()
                .CreateAsync(new HolidayWriteDto { Date = date, NameAr = nameAr, NameEn = nameEn }, Guid.NewGuid()));
            _log($"Holiday: {date}.");
        }
        catch (BusinessException e)
        {
            _log($"Holiday {date} skipped: {e.Code}.");
        }
    }

    // ---------------------------------------------------------------- circulars and notices

    private async Task SeedCircularsAndNoticesAsync()
    {
        async Task<Guid> Circular(Guid sender, CircularScope scope, string title, string content) =>
            (await AsAsync(sender, services => services.GetRequiredService<ICircularAppService>()
                .CreateAsync(new CircularCreateDto { Scope = scope, Title = title, Content = content }, Guid.NewGuid()))).Id;

        var winter = await Circular(_hrUserId, CircularScope.Company, "الدوام الشتوي",
            "يبدأ الدوام الشتوي من بداية الشهر القادم، من الساعة 8:00 صباحاً حتى 3:30 عصراً. يرجى الالتزام بأوقات البصمة.");
        await Circular(_executiveUserId, CircularScope.DepartmentManagers, "اجتماع مديري الأقسام",
            "اجتماع شهري لمديري الأقسام يوم الأحد القادم الساعة 10 صباحاً في قاعة الاجتماعات لمناقشة خطة الربع الأخير.");
        var salesTarget = await Circular(Person("ali").UserId, CircularScope.Team, "هدف مبيعات الشهر",
            "هدف القسم لهذا الشهر 150 مليون دينار. سيتم احتساب الحوافز حسب نسبة الإنجاز لكل مندوب.");

        foreach (var key in new[] { "haider", "maryam", "noor", "ahmed", "fatima" })
        {
            await AsAsync(Person(key).UserId, services => services.GetRequiredService<ICircularAppService>().AcknowledgeAsync(winter));
        }

        await AsAsync(Person("haider").UserId, services => services.GetRequiredService<ICircularAppService>().AcknowledgeAsync(salesTarget));

        async Task Notice(Guid sender, string[] recipients, string title, string content) =>
            await AsAsync(sender, services => services.GetRequiredService<INoticeAppService>().CreateAsync(new NoticeCreateDto
            {
                RecipientEmployeeIds = recipients.Select(x => Person(x).EmployeeId).ToList(),
                Title = title,
                Content = content
            }, Guid.NewGuid()));

        await Notice(_reviewerUserId, new[] { "karrar", "yousif" }, "مراجعة ملفات الأرشيف",
            "يرجى تسليم ملفات الأرشيف الخاصة بالربع الثالث إلى قسم المراجعة خلال ثلاثة أيام.");
        await Notice(_hrUserId, new[] { "abbas" }, "تحديث المستمسكات",
            "هويتك الوطنية المسجلة لدينا منتهية الصلاحية، يرجى تزويد الموارد البشرية بنسخة محدثة.");
        await Notice(_reviewerUserId, new[] { "mustafa", "noor" }, "ملاحظات زيارة المتابعة",
            "تمت زيارة القسم يوم أمس وتم تسجيل ملاحظات بسيطة حول ترتيب السجلات، يرجى الاطلاع ومعالجتها.");

        _log("Circulars: 3, notices: 3.");
    }

    // ---------------------------------------------------------------- store

    private async Task SeedStoreAsync()
    {
        var store = _storeOfficerUserId;
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        async Task<Guid> Category(string ar, string en) => (await AsAsync(store, services => services.GetRequiredService<IItemCategoryAppService>()
            .CreateAsync(new ItemCategoryCreateInputDto { NameAr = ar, NameEn = en }, Guid.NewGuid()))).Id;
        async Task<Guid> Unit(string ar, string en) => (await AsAsync(store, services => services.GetRequiredService<IItemUnitAppService>()
            .CreateAsync(new ItemUnitCreateInputDto { NameAr = ar, NameEn = en }, Guid.NewGuid()))).Id;

        var stationery = await Category("قرطاسية", "Stationery");
        var devices = await Category("أجهزة", "Devices");
        var cleaning = await Category("مواد تنظيف", "Cleaning supplies");
        var furniture = await Category("أثاث مكتبي", "Office furniture");
        var piece = await Unit("قطعة", "Piece");
        var box = await Unit("علبة", "Box");
        var carton = await Unit("كرتونة", "Carton");
        var litre = await Unit("لتر", "Litre");

        async Task<Guid> Item(string ar, string en, string sku, Guid category, Guid unit, ItemKind kind, decimal min) =>
            (await AsAsync(store, services => services.GetRequiredService<IItemAppService>().CreateAsync(new ItemCreateInputDto
            {
                NameAr = ar, NameEn = en, Sku = $"DEMO-{tag}-{sku}", CategoryId = category, UnitId = unit, Kind = kind, MinQuantity = min
            }, Guid.NewGuid()))).Id;

        var paper = await Item("ورق A4", "A4 paper", "PAPER", stationery, box, ItemKind.Consumable, 10);
        var pens = await Item("أقلام جاف", "Ballpoint pens", "PENS", stationery, box, ItemKind.Consumable, 5);
        var notebooks = await Item("دفتر ملاحظات", "Notebook", "NOTEBOOK", stationery, piece, ItemKind.Consumable, 20);
        var toner = await Item("حبر طابعة ليزرية", "Laser toner", "TONER", stationery, piece, ItemKind.Consumable, 4);
        var laptop = await Item("حاسبة محمولة", "Laptop", "LAPTOP", devices, piece, ItemKind.Returnable, 2);
        var headset = await Item("سماعة رأس", "Headset", "HEADSET", devices, piece, ItemKind.Returnable, 3);
        var monitor = await Item("شاشة عرض 24 إنش", "24-inch monitor", "MONITOR", devices, piece, ItemKind.Returnable, 2);
        var printer = await Item("طابعة ليزرية", "Laser printer", "PRINTER", devices, piece, ItemKind.Returnable, 1);
        var floorCleaner = await Item("سائل تنظيف أرضيات", "Floor cleaner", "CLEANER", cleaning, litre, ItemKind.Consumable, 15);
        var tissues = await Item("مناديل ورقية", "Paper tissues", "TISSUES", cleaning, carton, ItemKind.Consumable, 5);
        var chair = await Item("كرسي مكتبي", "Office chair", "CHAIR", furniture, piece, ItemKind.Returnable, 2);
        var desk = await Item("مكتب خشبي", "Wooden desk", "DESK", furniture, piece, ItemKind.Returnable, 1);

        async Task<Guid> Receipt(string number, string title, string supplier, int daysAgo, string? note, bool approve, params (Guid Item, decimal Quantity, decimal Price)[] lines)
        {
            var purchase = await AsAsync(store, services => services.GetRequiredService<IStorePurchaseAppService>().CreateAsync(new StorePurchaseCreateInputDto
            {
                Title = title,
                ReceiptNumber = $"DEMO-{tag}-{number}",
                SupplierName = supplier,
                PurchaseDate = _today.AddDays(-daysAgo - 2),
                ReceivedDate = _today.AddDays(-daysAgo),
                Note = note,
                Lines = lines.Select(l => new StorePurchaseLineInputDto { ItemId = l.Item, Quantity = l.Quantity, UnitPrice = l.Price }).ToList()
            }, Guid.NewGuid()));
            if (approve)
            {
                await AsAsync(store, services => services.GetRequiredService<IStorePurchaseAppService>().ApproveAsync(purchase.Id));
            }
            return purchase.Id;
        }

        await Receipt("R1", "قرطاسية الربع الأخير", "شركة الرافدين للتجهيزات", 40, "تجهيز الربع الأخير", true,
            (paper, 40, 9_000), (pens, 15, 6_500), (notebooks, 60, 2_500), (toner, 3, 95_000));
        await Receipt("R2", "أجهزة الموظفين الجدد", "شركة بغداد للحاسبات", 30, "حاسبات وشاشات وطابعات", true,
            (laptop, 4, 850_000), (headset, 6, 45_000), (monitor, 5, 210_000), (printer, 2, 375_000));
        await Receipt("R3", "مواد التنظيف الشهرية", "مخازن النخيل", 20, null, true,
            (floorCleaner, 40, 3_500), (tissues, 6, 27_000));
        await Receipt("R4", "تأثيث قاعة الاجتماعات", "معرض دجلة للأثاث", 15, "كراسي ومكاتب للقاعة الجديدة", true,
            (chair, 6, 125_000), (desk, 3, 290_000));
        await Receipt("R5", "حاسبات إضافية", "شركة بغداد للحاسبات", 8, null, true, (laptop, 2, 830_000));
        // Left as a draft, waiting for approval.
        await Receipt("R6", "مستلزمات مكتبية", "مكتبة المتنبي", 0, "بانتظار المطابقة مع الوصل الأصلي", false,
            (paper, 20, 8_750), (pens, 10, 6_250));

        async Task<Guid> Team(string name, string department) => (await AsAsync(store, services => services.GetRequiredService<IStoreTeamAppService>()
            .CreateAsync(new StoreTeamCreateInputDto { Name = name, DepartmentId = _departmentIds[department] }, Guid.NewGuid()))).Id;
        var reception = await Team("مكتب الاستقبال", "ops");
        var cleaningTeam = await Team("فريق الخدمات", "ops");
        var support = await Team("فريق الدعم الفني", "it");

        async Task Voucher(string number, int daysAgo, Guid team, string person, string note, bool approve, params (Guid Item, decimal Quantity)[] lines)
        {
            var voucher = await AsAsync(store, services => services.GetRequiredService<IStoreIssueVoucherAppService>().CreateAsync(new StoreIssueVoucherCreateInputDto
            {
                VoucherNumber = $"DEMO-{tag}-{number}",
                IssueDate = _today.AddDays(-daysAgo),
                TeamId = team,
                EmployeeId = Person(person).EmployeeId,
                Note = note,
                Lines = lines.Select(l => new StoreIssueVoucherLineInputDto { ItemId = l.Item, Quantity = l.Quantity }).ToList()
            }, Guid.NewGuid()));
            if (approve)
            {
                await AsAsync(store, services => services.GetRequiredService<IStoreIssueVoucherAppService>().ApproveAsync(voucher.Id));
            }
        }

        await Voucher("V1", 12, reception, "duaa", "احتياج مكتب الاستقبال", true, (paper, 5), (pens, 2), (notebooks, 10));
        await Voucher("V2", 6, cleaningTeam, "mohammed", "تنظيف الطوابق", true, (floorCleaner, 30), (tissues, 3));
        await Voucher("V3", 3, support, "yousif", "طباعة تقارير الدعم", true, (toner, 1), (paper, 4));
        // Left as a draft, waiting for approval.
        await Voucher("V4", 0, reception, "duaa", "دفاتر لسجل الزوار", false, (notebooks, 15));

        async Task<Guid> Custody(Guid item, string? person, string? department, string note) =>
            (await AsAsync(store, services => services.GetRequiredService<ICustodyAppService>().CreateAsync(new CustodyCreateInputDto
            {
                ItemId = item,
                Kind = person != null ? CustodyKind.Personal : CustodyKind.Shared,
                EmployeeId = person != null ? Person(person).EmployeeId : null,
                DepartmentId = department != null ? _departmentIds[department] : null,
                Quantity = 1,
                DeductFromStock = true,
                Condition = CustodyItemCondition.New,
                Note = note
            }, Guid.NewGuid()))).Id;
        async Task Acknowledge(Guid id, Guid userId) =>
            await AsAsync(userId, services => services.GetRequiredService<ICustodyAppService>().AcknowledgeAsync(id));

        // Personal custody: acknowledged by the holder, one still waiting, and one handed back to stock.
        await Acknowledge(await Custody(laptop, "ahmed", null, "حاسبة للتطوير"), Person("ahmed").UserId);
        await Acknowledge(await Custody(laptop, "sara", null, "حاسبة لإدارة الشبكة"), Person("sara").UserId);
        await Acknowledge(await Custody(monitor, "yousif", null, "شاشة إضافية للدعم الفني"), Person("yousif").UserId);
        await Custody(headset, "zahraa", null, "سماعة لمتابعة مكالمات الزبائن");
        var returnedChair = await Custody(chair, "ruqaya", null, "كرسي مؤقت");
        await Acknowledge(returnedChair, Person("ruqaya").UserId);
        await AsAsync(store, services => services.GetRequiredService<ICustodyAppService>()
            .ReturnAsync(returnedChair, new CustodyReturnInputDto { Restock = true, Note = "أُرجع سليمًا بعد انتهاء الحاجة" }));

        // Shared custody of a department: acknowledged by its manager, and one still waiting.
        await Acknowledge(await Custody(printer, null, "it", "طابعة قسم تقنية المعلومات"), Person("noor").UserId);
        await Custody(desk, null, "sales", "مكتب لقاعة استقبال الزبائن");

        _log("Store: 4 categories, 12 items, 6 receipts (1 draft), 3 teams, 4 issue vouchers (1 draft), 7 custodies (1 returned).");

        // A store request from an employee, waiting along its route. The type ships switched off, and turning it on is
        // the company's choice, so the demo only uses it when it is already on.
        var storeType = await InTenantAsync(async services =>
            await services.GetRequiredService<IRepository<RequestType, Guid>>().FirstAsync(x => x.Code == RequestTypeCodes.StoreItem));
        if (!storeType.IsActive)
        {
            _log("Store request skipped: the store item request type is switched off.");
            return;
        }

        var maryam = Person("maryam");
        var request = await AsAsync(maryam.UserId, services => services.GetRequiredService<IRequestAppService>().SubmitAsync(
            new RequestSubmitInputDto { RequestTypeId = storeType.Id, ItemId = headset, Quantity = 1, Description = "سماعة لمتابعة مكالمات الزبائن" }, Guid.NewGuid()));
        await AdvanceAsync(maryam, request.Id, approvals: 1);
        _log("Store request: 1.");
    }

    // ---------------------------------------------------------------- warnings, letters, delegation

    private async Task SeedWarningsAndLettersAsync()
    {
        // A full warning: the manager recommends, HR records it and notifies, the employee replies, HR issues it.
        var mohammed = Person("mohammed");
        var warning = await AsAsync(mohammed.ManagerUserId!.Value, services => services.GetRequiredService<IWarningAppService>().CreateAsync(new WarningCreateInput
        {
            EmployeeId = mohammed.EmployeeId,
            RecommendedLevel = WarningLevel.First,
            IncidentDate = _today.AddDays(-9),
            Description = "تأخر في تسليم شحنة لزبون مهم دون إبلاغ مسبق، مما أدى إلى شكوى من الزبون."
        }, Guid.NewGuid()));
        await AsAsync(_hrUserId, services => services.GetRequiredService<IWarningAppService>().NotifyAsync(warning.Id));
        await AsAsync(mohammed.UserId, services => services.GetRequiredService<IWarningAppService>()
            .RespondAsync(warning.Id, new WarningRespondInputDto { Content = "حصل عطل في السيارة في الطريق ولم يكن الهاتف مشحوناً. أعتذر وسألتزم بالإبلاغ مستقبلاً." }));
        await AsAsync(_hrUserId, services => services.GetRequiredService<IWarningAppService>()
            .DecideAsync(warning.Id, new WarningDecideInputDto { Decision = WarningDecisionOutcome.Issue, IssuedLevel = WarningLevel.First, Note = "إنذار أول مع مراعاة الظرف" }));

        // A recommendation still under HR review.
        var karrar = Person("karrar");
        await AsAsync(karrar.ManagerUserId!.Value, services => services.GetRequiredService<IWarningAppService>().CreateAsync(new WarningCreateInput
        {
            EmployeeId = karrar.EmployeeId,
            RecommendedLevel = WarningLevel.First,
            IncidentDate = _today.AddDays(-3),
            Description = "غياب ثلاثة أيام متتالية دون إذن."
        }, Guid.NewGuid()));

        foreach (var (key, title) in new[] { ("noor", "شكر وتقدير لإنجاز مشروع الشبكة الداخلية"), ("haider", "شكر وتقدير لتحقيق أعلى مبيعات في الربع الثالث") })
        {
            await AsAsync(_executiveUserId, services => services.GetRequiredService<IAppreciationLetterAppService>().CreateAsync(new AppreciationLetterCreateInput
            {
                EmployeeId = Person(key).EmployeeId,
                Title = title,
                Note = "مع تمنياتنا بدوام التوفيق",
                File = new UploadedFileInput { Content = DemoPdf, FileName = "letter.pdf", ContentType = "application/pdf" }
            }, Guid.NewGuid()));
        }

        _log("Warnings: 2 (one issued), appreciation letters: 2.");
    }

    private async Task SeedDelegationAsync()
    {
        var noor = Person("noor");
        var starts = _today.AddDays(8).ToDateTime(new TimeOnly(5, 0), DateTimeKind.Utc);
        await AsAsync(noor.UserId, services => services.GetRequiredService<IDelegationAppService>().CreateAsync(new DelegationCreateDto
        {
            ToUserId = Person("ahmed").UserId,
            Scope = DelegationScope.Requests,
            StartsAt = starts,
            EndsAt = starts.AddDays(5),
            Reason = "إجازة خارج البلد"
        }));
        _log("Delegation: 1.");
    }

    // ---------------------------------------------------------------- evaluations

    /// <summary>
    /// Runs the evaluation timetable at the current moment, as the Host's worker does. When that leaves a cycle open,
    /// each demo manager fills and submits some forms and one employee acknowledges.
    /// </summary>
    private async Task SeedEvaluationsAsync()
    {
        await InTenantAsync(async services =>
        {
            await services.GetRequiredService<EvaluationCycleService>().ProcessAsync(services.GetRequiredService<IClock>().Now);
            return true;
        });

        var submitted = 0;
        foreach (var department in DemoCatalog.Departments)
        {
            var manager = Person(department.Manager.Key);
            var forms = await AsAsync(manager.UserId, services => services.GetRequiredService<IEvaluationAppService>()
                .GetTeamListAsync(new EvaluationTeamListInput { Status = EvaluationStatus.Draft, MaxResultCount = 50 }));

            foreach (var (row, index) in forms.Items.Take(3).Select((x, i) => (x, i)))
            {
                var form = await AsAsync(manager.UserId, services => services.GetRequiredService<IEvaluationAppService>().GetManagerFormAsync(row.Id));
                if (!form.IsEditable || form.AvailableGrades.Count == 0)
                {
                    continue;
                }

                var grades = form.AvailableGrades.Select(x => x.Code).ToList();
                var saved = await AsAsync(manager.UserId, services => services.GetRequiredService<IEvaluationAppService>().SaveDraftAsync(row.Id, new EvaluationSaveDraftDto
                {
                    Scores = form.Scores.Select((s, i) => new EvaluationScoreInputDto { CriterionId = s.CriterionId, GradeCode = grades[(index + i) % Math.Min(3, grades.Count)] }).ToList(),
                    EmployeeNotes = "أداء جيد بشكل عام، مع ضرورة الالتزام بمواعيد التسليم.",
                    ConcurrencyStamp = form.ConcurrencyStamp
                }));
                await AsAsync(manager.UserId, services => services.GetRequiredService<IEvaluationAppService>()
                    .SubmitAsync(row.Id, new EvaluationSubmitInputDto { ConcurrencyStamp = saved.ConcurrencyStamp }));
                submitted++;

                if (index == 0)
                {
                    var employee = _people.Values.First(x => x.EmployeeId == row.Employee.Id);
                    await AsAsync(employee.UserId, services => services.GetRequiredService<IEvaluationAppService>().AcknowledgeAsync(row.Id));
                }
            }
        }

        _log(submitted == 0 ? "Evaluations: no open cycle right now, none filled." : $"Evaluations submitted: {submitted}.");
    }
}
