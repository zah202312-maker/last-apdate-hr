using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HR_Mangment2.Attendance;
using HR_Mangment2.Identity;
using HR_Mangment2.Leaves;
using HR_Mangment2.Memos;
using HR_Mangment2.Requests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Uow;

namespace HR_Mangment2.DemoData;

public sealed partial class DemoDataSeeder
{
    private const int AttendanceDays = 28;

    private static IAsyncQueryableExecuter AsyncExecuterOf(IServiceProvider services) =>
        services.GetRequiredService<IAsyncQueryableExecuter>();

    /// <summary>A stable pseudo-random number in [0, 100) for a person and a day, so each run gives the same month.</summary>
    private static int Roll(int person, int day) => (int)((uint)(person * 7919 + day * 104729 + person * day * 31) % 100);

    // ---------------------------------------------------------------- attendance

    /// <summary>
    /// A gate device, like the real one: the system admin registers it and maps each person to a device number, then
    /// the device sends four weeks of punches. The attendance jobs build the days, including the absences.
    /// </summary>
    private async Task SeedAttendanceAsync()
    {
        var device = await AsAsync(_systemAdminUserId, services => services.GetRequiredService<IAttendanceDeviceAppService>()
            .CreateAsync(new AttendanceDeviceCreateDto { SerialNumber = "DEMO-GATE-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), Name = "بوابة المقر الرئيسي" }, Guid.NewGuid()));
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById((await AsAsync(_systemAdminUserId, services =>
            services.GetRequiredService<IAttendanceSettingsAppService>().GetAsync())).TimeZone);

        var workers = _people.Values.Where(x => x.HasShift).ToList();
        for (var i = 0; i < workers.Count; i++)
        {
            var person = workers[i];
            await AsAsync(_systemAdminUserId, services => services.GetRequiredService<IAttendanceDeviceAppService>()
                .CreateUserMapAsync(device.Device.Id, new DeviceUserMapCreateDto { DeviceUserId = DeviceUserId(i), EmployeeId = person.EmployeeId }));
        }

        var from = _today.AddDays(-AttendanceDays);
        var to = _today.AddDays(-1);
        var punches = new List<AttendancePunchInputDto>();
        for (var i = 0; i < workers.Count; i++)
        {
            for (var date = from; date <= to; date = date.AddDays(1))
            {
                if (!_workDays.Contains(date.DayOfWeek))
                {
                    continue;
                }

                var roll = Roll(i + 1, date.DayNumber);
                if (roll < 5)
                {
                    continue; // absent
                }

                var checkIn = roll < 20
                    ? new TimeOnly(8, 12).AddMinutes(roll * 3 % 50) // late
                    : new TimeOnly(7, 40).AddMinutes(roll % 22);
                var checkOut = roll is >= 50 and < 54
                    ? new TimeOnly(15, 5).AddMinutes(roll % 20) // left early
                    : new TimeOnly(16, 0).AddMinutes(roll * 7 % 40);

                punches.Add(Punch(i, date, checkIn, timeZone));
                punches.Add(Punch(i, date, checkOut, timeZone));
            }
        }

        foreach (var batch in punches.Chunk(200))
        {
            using var scope = _root.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IAttendancePunchAppService>()
                .IngestAsync(device.DeviceKey, new AttendancePunchBatchDto { Punches = batch.ToList() });
        }

        // The days with no punch become absences only when the days are built for the whole range.
        await InTenantAsync(async services =>
        {
            await services.GetRequiredService<IBackgroundJobManager>().EnqueueAsync(new AttendanceRecalculationArgs
            {
                TenantId = _tenantId,
                Ranges = workers.Select(x => new AttendanceRecalculationRange { EmployeeId = x.EmployeeId, From = from, To = to }).ToList()
            });
            return true;
        });
        await RunAttendanceJobsAsync();

        _log($"Attendance: {punches.Count} punches for {workers.Count} people, {from} to {to}.");
    }

    private static string DeviceUserId(int index) => (9001 + index).ToString();

    private static AttendancePunchInputDto Punch(int index, DateOnly date, TimeOnly local, TimeZoneInfo zone) => new()
    {
        DeviceUserId = DeviceUserId(index),
        PunchTime = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(local, DateTimeKind.Unspecified), zone)
    };

    /// <summary>What the Host's job worker would run, oldest first, until no attendance job is left.</summary>
    private async Task RunAttendanceJobsAsync()
    {
        using var scope = _root.CreateScope();
        var services = scope.ServiceProvider;
        var repository = services.GetRequiredService<IBackgroundJobRepository>();
        var unitOfWorkManager = services.GetRequiredService<IUnitOfWorkManager>();
        var options = services.GetRequiredService<IOptions<AbpBackgroundJobOptions>>().Value;
        var serializer = services.GetRequiredService<IBackgroundJobSerializer>();
        var executer = services.GetRequiredService<IBackgroundJobExecuter>();

        while (true)
        {
            List<BackgroundJobRecord> jobs;
            using (var read = unitOfWorkManager.Begin(requiresNew: true))
            {
                jobs = (await repository.GetWaitingListAsync(null, 1000))
                    .Where(x => x.JobName.StartsWith("Hr.Attendance", StringComparison.Ordinal))
                    .OrderBy(x => x.CreationTime)
                    .ToList();
                await read.CompleteAsync();
            }

            if (jobs.Count == 0)
            {
                return;
            }

            foreach (var job in jobs)
            {
                var configuration = options.GetJob(job.JobName);
                await executer.ExecuteAsync(new JobExecutionContext(services, configuration.JobType, serializer.Deserialize(job.JobArgs, configuration.ArgsType)));

                using var remove = unitOfWorkManager.Begin(requiresNew: true);
                await repository.DeleteAsync(job.Id);
                await remove.CompleteAsync();
            }
        }
    }

    // ---------------------------------------------------------------- leaves

    /// <summary>Leave requests in every state: approved, waiting for HR, waiting for the manager, and rejected.</summary>
    private async Task SeedLeavesAsync()
    {
        var nextWeek = NextWorkDay(_today.AddDays(1));
        var count = 0;

        async Task Annual(string key, DateOnly start, int workDays, int approvals, string? reject = null)
        {
            var person = Person(key);
            var end = start;
            for (var added = 1; added < workDays;)
            {
                end = end.AddDays(1);
                if (_workDays.Contains(end.DayOfWeek))
                {
                    added++;
                }
            }

            var leave = await AsAsync(person.UserId, services => services.GetRequiredService<ILeaveRequestAppService>().CreateAsync(
                new LeaveRequestCreateDto { Kind = LeaveKind.Annual, StartDate = start, EndDate = end, Notes = "إجازة اعتيادية" }, Guid.NewGuid()));
            await AdvanceAsync(person, leave.RequestId, approvals, reject);
            count++;
        }

        async Task Hourly(string key, DateOnly date, TimeOnly fromTime, TimeOnly toTime, int approvals)
        {
            var person = Person(key);
            var leave = await AsAsync(person.UserId, services => services.GetRequiredService<ILeaveRequestAppService>().CreateAsync(
                new LeaveRequestCreateDto { Kind = LeaveKind.Hourly, Date = date, FromTime = fromTime, ToTime = toTime, Notes = "مراجعة دائرة رسمية" }, Guid.NewGuid()));
            await AdvanceAsync(person, leave.RequestId, approvals);
            count++;
        }

        await Annual("haider", nextWeek, 3, approvals: 9);
        await Annual("fatima", NextWorkDay(nextWeek.AddDays(7)), 2, approvals: 9);
        await Annual("maryam", NextWorkDay(nextWeek.AddDays(14)), 2, approvals: 0);
        await Annual("sara", NextWorkDay(nextWeek.AddDays(3)), 4, approvals: 0, reject: "ضغط عمل خلال هذه الفترة، يرجى اختيار موعد آخر");
        await Annual("abbas", NextWorkDay(nextWeek.AddDays(10)), 1, approvals: 1);
        await Hourly("ahmed", nextWeek, new TimeOnly(10, 0), new TimeOnly(12, 0), approvals: 1);
        await Hourly("yousif", NextWorkDay(nextWeek.AddDays(2)), new TimeOnly(13, 0), new TimeOnly(15, 0), approvals: 9);

        _log($"Leave requests: {count}.");
    }

    private DateOnly NextWorkDay(DateOnly date)
    {
        while (!_workDays.Contains(date.DayOfWeek))
        {
            date = date.AddDays(1);
        }

        return date;
    }

    // ---------------------------------------------------------------- requests and memos

    private async Task SeedRequestsAndMemosAsync()
    {
        var types = await InTenantAsync(async services =>
            (await services.GetRequiredService<IRepository<RequestType, Guid>>().GetListAsync()).ToDictionary(x => x.Code, x => x.Id));
        var count = 0;

        async Task Submit(string key, string code, string description, int approvals, string? reject = null)
        {
            var person = Person(key);
            var request = await AsAsync(person.UserId, services => services.GetRequiredService<IRequestAppService>().SubmitAsync(
                new RequestSubmitInputDto { RequestTypeId = types[code], Description = description }, Guid.NewGuid()));
            await AdvanceAsync(person, request.Id, approvals, reject);
            count++;
        }

        await Submit("karrar", RequestTypeCodes.Accountant, "طلب سلفة بمبلغ 500,000 دينار تستقطع على خمسة أشهر", approvals: 9);
        await Submit("mohammed", RequestTypeCodes.Accountant, "طلب سلفة بمبلغ 1,000,000 دينار", approvals: 0, reject: "المبلغ أكبر من الحد المسموح للسلف");
        await Submit("yousif", RequestTypeCodes.AdministrativeNeed, "حاسبة محمولة جديدة لأن الحالية تتوقف باستمرار", approvals: 0);
        await Submit("duaa", RequestTypeCodes.AdministrativeNeed, "كرسي مكتبي مريح لمكتب الاستقبال", approvals: 1);
        await Submit("ruqaya", RequestTypeCodes.AdministrativeNeed, "اشتراك في دورة تحليل الأنظمة المتقدمة", approvals: 2);

        _log($"Requests: {count}.");

        var memos = 0;
        async Task Memo(string key, string content, bool forward)
        {
            var person = Person(key);
            var memo = await AsAsync(person.UserId, services => services.GetRequiredService<IMemoAppService>()
                .CreateAsync(new MemoCreateInputDto { Kind = MemoKind.Problem, Content = content }, Guid.NewGuid()));
            if (forward && person.ManagerUserId != null)
            {
                await AsAsync(person.ManagerUserId.Value, services => services.GetRequiredService<IMemoAppService>()
                    .ForwardAsync(memo.Id, new NoteInputDto { Note = "يرجى المتابعة" }));
            }

            memos++;
        }

        await Memo("zahraa", "جهاز التكييف في قاعة المبيعات معطل منذ يومين ويؤثر على استقبال الزبائن.", forward: false);
        await Memo("abbas", "نقص في معدات السلامة للفريق الميداني ونحتاج خوذ وقفازات جديدة.", forward: true);
        await Memo("ahmed", "انقطاع متكرر في خدمة الإنترنت داخل الطابق الثاني.", forward: false);

        _log($"Memos: {memos}.");
    }

    /// <summary>
    /// Moves a request along its route as each approver would: the employee's manager, HR, the executive director or
    /// the destination, read from the request itself. Stops after the given approvals, rejecting there when asked.
    /// </summary>
    private async Task AdvanceAsync(DemoPerson owner, Guid requestId, int approvals, string? rejectReason = null)
    {
        for (var done = 0; ; done++)
        {
            var request = await AsAsync(owner.UserId, services => services.GetRequiredService<IRequestAppService>().GetAsync(requestId));
            if (request.CurrentApproverRole == null)
            {
                return;
            }

            var actor = request.CurrentApproverRole.Value switch
            {
                AppRole.DepartmentManager => owner.ManagerUserId ?? throw new InvalidOperationException($"{owner.Key} has no manager."),
                AppRole.HrManager => _hrUserId,
                AppRole.ExecutiveDirector => _executiveUserId,
                AppRole.Accountant => _accountantUserId,
                AppRole.StoreOfficer => _storeOfficerUserId,
                AppRole.SystemAdmin => _systemAdminUserId,
                var role => throw new InvalidOperationException($"No demo actor for {role}.")
            };

            if (done == approvals)
            {
                if (rejectReason != null)
                {
                    await AsAsync(actor, services => services.GetRequiredService<IRequestAppService>()
                        .RejectAsync(requestId, new ReasonInputDto { Reason = rejectReason }));
                }

                return;
            }

            await AsAsync(actor, services => services.GetRequiredService<IRequestAppService>()
                .ApproveAsync(requestId, new NoteInputDto { Note = "موافق" }));
        }
    }
}
