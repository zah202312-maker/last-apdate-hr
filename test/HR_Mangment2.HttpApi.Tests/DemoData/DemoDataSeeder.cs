using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using HR_Mangment2.Departments;
using HR_Mangment2.Employees;
using HR_Mangment2.Finance;
using HR_Mangment2.Identity;
using HR_Mangment2.WorkShifts;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.TenantManagement;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace HR_Mangment2.DemoData;

/// <summary>
/// Fills a database with a believable company for demos. Every write goes through the application services as the
/// person who would do it (HR adds employees, the accountant runs payroll, HR approves), so the business rules,
/// permissions, notifications and audit run exactly as in real use. Nothing is written around them, except the demo
/// accounts' sign-in password. Runs once: it stops when it finds its own employees (numbers start with DEMO-).
/// </summary>
public sealed partial class DemoDataSeeder
{
    public const string NumberPrefix = "DEMO-";
    public const string EmailDomain = "demo.example";
    public const string Currency = "IQD";

    private readonly IServiceProvider _root;
    private readonly string _tenantName;
    private readonly string _demoPassword;
    private readonly Action<string> _log;

    private Guid _tenantId;
    private Guid _hrUserId;
    private Guid _executiveUserId;
    private Guid _systemAdminUserId;
    private HashSet<DayOfWeek> _workDays = new();
    private Guid _hrEmployeeId;
    private Guid _executiveEmployeeId;
    private Guid _shiftId;
    private Guid _accountantUserId;
    private Guid _storeOfficerUserId;
    private Guid _reviewerUserId;
    private DateOnly _today;

    /// <summary>Everyone the seeder created, by key, so later steps can act as them.</summary>
    private readonly Dictionary<string, DemoPerson> _people = new();
    private readonly Dictionary<string, Guid> _departmentIds = new();

    public DemoDataSeeder(IServiceProvider root, string tenantName, string demoPassword, Action<string> log)
    {
        _root = root;
        _tenantName = tenantName;
        _demoPassword = demoPassword;
        _log = log;
    }

    public IReadOnlyCollection<DemoPerson> People => _people.Values;

    /// <returns>False when the demo data is already there.</returns>
    public async Task<bool> SeedAsync()
    {
        await LoadContextAsync();
        if (await AlreadySeededAsync())
        {
            _log("Demo data is already in this database. Nothing was changed.");
            return false;
        }

        await SeedOrganizationAsync();
        await SeedAttendanceAsync();
        await SeedLeavesAsync();
        await SeedRequestsAndMemosAsync();
        await SeedCompanyLifeAsync();
        await SeedFinanceAsync();

        _log($"Done: {_people.Count} demo people.");
        return true;
    }

    // ---------------------------------------------------------------- context and actors

    private async Task LoadContextAsync()
    {
        using var scope = _root.CreateScope();
        var services = scope.ServiceProvider;
        var tenant = await services.GetRequiredService<ITenantRepository>().FindByNameAsync(
                         services.GetRequiredService<ITenantNormalizer>().NormalizeName(_tenantName)!)
                     ?? throw new InvalidOperationException($"Tenant '{_tenantName}' not found. Run the DbMigrator first.");
        _tenantId = tenant.Id;

        using var change = services.GetRequiredService<ICurrentTenant>().Change(_tenantId);
        using var uow = services.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var users = services.GetRequiredService<IdentityUserManager>();
        var employees = services.GetRequiredService<IRepository<Employee, Guid>>();

        _hrUserId = (await users.FindByNameAsync("hrmanager") ?? throw new InvalidOperationException("The hrmanager account is missing.")).Id;
        _executiveUserId = (await users.FindByNameAsync("executive") ?? throw new InvalidOperationException("The executive account is missing.")).Id;
        _systemAdminUserId = (await users.FindByNameAsync("sysadmin") ?? throw new InvalidOperationException("The sysadmin account is missing.")).Id;
        _hrEmployeeId = (await employees.FirstAsync(x => x.UserId == _hrUserId)).Id;
        _executiveEmployeeId = (await employees.FirstAsync(x => x.UserId == _executiveUserId)).Id;

        var shifts = services.GetRequiredService<IRepository<WorkShift, Guid>>();
        var shift = await AsyncExecuterOf(services).FirstOrDefaultAsync((await shifts.WithDetailsAsync(x => x.Days)).Where(x => x.NameEn == "Default Shift" && x.IsActive))
                    ?? throw new InvalidOperationException("The seeded 'Default Shift' is missing or inactive.");
        _shiftId = shift.Id;
        _workDays = shift.Days.Where(x => x.DayOfWeek != null).Select(x => Enum.Parse<DayOfWeek>(x.DayOfWeek!.Value.ToString())).ToHashSet();

        _today = DateOnly.FromDateTime(services.GetRequiredService<IClock>().Now);
        await uow.CompleteAsync();
    }

    private async Task<bool> AlreadySeededAsync() => await InTenantAsync(async services =>
        await services.GetRequiredService<IRepository<Employee, Guid>>()
            .AnyAsync(x => x.EmployeeNumber != null && x.EmployeeNumber.StartsWith(NumberPrefix)));

    /// <summary>Runs inside the company tenant, with no signed-in user, in its own unit of work.</summary>
    private async Task<T> InTenantAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = _root.CreateScope();
        var services = scope.ServiceProvider;
        using var change = services.GetRequiredService<ICurrentTenant>().Change(_tenantId);
        using var uow = services.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var result = await action(services);
        await uow.CompleteAsync();
        return result;
    }

    /// <summary>
    /// Calls the application services as this user: their id and roles become the current principal, so permission
    /// checks, data scopes and "who did it" behave as if they had signed in.
    /// </summary>
    private async Task<T> AsAsync<T>(Guid userId, Func<IServiceProvider, Task<T>> action)
    {
        var (userName, roles) = await InTenantAsync(async services =>
        {
            var users = services.GetRequiredService<IdentityUserManager>();
            var user = await users.GetByIdAsync(userId);
            return (user.UserName!, (await users.GetRolesAsync(user)).ToList());
        });

        using var scope = _root.CreateScope();
        var scoped = scope.ServiceProvider;
        using var change = scoped.GetRequiredService<ICurrentTenant>().Change(_tenantId);

        var claims = new List<Claim>
        {
            new(AbpClaimTypes.UserId, userId.ToString()),
            new(AbpClaimTypes.UserName, userName),
            new(AbpClaimTypes.TenantId, _tenantId.ToString())
        };
        claims.AddRange(roles.Select(role => new Claim(AbpClaimTypes.Role, role)));
        using var principal = scoped.GetRequiredService<ICurrentPrincipalAccessor>()
            .Change(new ClaimsPrincipal(new ClaimsIdentity(claims, "DemoData")));

        try
        {
            return await action(scoped);
        }
        catch (Volo.Abp.BusinessException e) when (e.Code != null)
        {
            throw new InvalidOperationException("Refused as " + userName + ": " + e.Code, e);
        }
        catch (Volo.Abp.Validation.AbpValidationException e)
        {
            // The plain message names no field; this one does.
            throw new InvalidOperationException("Validation failed as " + userName + ": " +
                string.Join("; ", e.ValidationErrors.Select(x => string.Join(",", x.MemberNames) + " " + x.ErrorMessage)), e);
        }
    }

    private Task AsAsync(Guid userId, Func<IServiceProvider, Task> action) =>
        AsAsync(userId, async services =>
        {
            await action(services);
            return true;
        });

    private DemoPerson Person(string key) => _people[key];

    // ---------------------------------------------------------------- organization

    private async Task SeedOrganizationAsync()
    {
        var departments = _departmentIds;
        foreach (var department in DemoCatalog.Departments)
        {
            var created = await AsAsync(_hrUserId, services => services.GetRequiredService<IDepartmentAppService>()
                .CreateAsync(new DepartmentCreateDto { NameAr = department.NameAr, NameEn = department.NameEn }));
            departments[department.Key] = created.Id;
        }

        _log($"Departments: {departments.Count}.");

        var number = 0;
        foreach (var department in DemoCatalog.Departments)
        {
            var manager = await AddEmployeeAsync(department.Manager, departments[department.Key], _hrEmployeeId,
                new[] { HrRoles.DepartmentManager }, ++number, withShift: true);

            foreach (var staff in department.Staff)
            {
                await AddEmployeeAsync(staff, departments[department.Key], manager.EmployeeId, new[] { HrRoles.Employee }, ++number, withShift: true);
            }
        }

        // The back-office roles: the demo uses the company's own holders when they exist, so the demo does not add a
        // second accountant to a company that has one.
        _accountantUserId = await FindRoleHolderAsync(HrRoles.Accountant)
                            ?? (await AddEmployeeAsync(DemoCatalog.Accountant, departments["ops"], _executiveEmployeeId, new[] { HrRoles.Accountant }, ++number, withShift: false)).UserId;
        _storeOfficerUserId = await FindRoleHolderAsync(HrRoles.StoreOfficer)
                              ?? (await AddEmployeeAsync(DemoCatalog.StoreOfficer, departments["ops"], _executiveEmployeeId, new[] { HrRoles.StoreOfficer }, ++number, withShift: false)).UserId;
        _reviewerUserId = await FindRoleHolderAsync(HrRoles.Reviewer)
                          ?? (await AddEmployeeAsync(DemoCatalog.Reviewer, departments["it"], _executiveEmployeeId, new[] { HrRoles.Reviewer }, ++number, withShift: true)).UserId;

        _log($"Employees: {_people.Count}.");
    }

    private async Task<DemoPerson> AddEmployeeAsync(DemoCatalog.PersonSpec spec, Guid departmentId, Guid managerEmployeeId, string[] roles, int number, bool withShift)
    {
        var userName = "demo." + spec.Key;
        var created = await AsAsync(_hrUserId, services => services.GetRequiredService<IEmployeeAppService>().CreateAsync(new EmployeeCreateDto
        {
            FullNameAr = spec.NameAr,
            FullNameEn = spec.NameEn,
            Email = userName + "@" + EmailDomain,
            PhoneNumber = "0770" + (1000000 + number * 7919 % 9000000).ToString(),
            Gender = spec.Female ? AppGender.Female : AppGender.Male,
            DepartmentId = departmentId,
            JobTitle = spec.JobTitle,
            HireDate = spec.HireDate,
            WorkShiftId = withShift ? _shiftId : null,
            ManagerEmployeeId = managerEmployeeId,
            UserName = userName,
            RoleNames = roles.ToList(),
            EmployeeNumber = $"{NumberPrefix}{number:D3}",
            // HR records the salary agreed at hiring (decision 2026-11-10); the accountant adds allowances later.
            BaseSalary = spec.BaseSalary,
            SalaryCurrency = Currency
        }));

        await SetKnownPasswordAsync(created.Credentials.UserId);

        var managerUserId = await InTenantAsync(async services =>
            (await services.GetRequiredService<IRepository<Employee, Guid>>().GetAsync(managerEmployeeId)).UserId);
        var person = new DemoPerson(spec.Key, created.Employee.Id, created.Credentials.UserId, userName, spec.NameEn, roles, spec.BaseSalary, withShift, managerUserId);
        _people[spec.Key] = person;
        return person;
    }

    /// <summary>The demo accounts sign in with one known password and no forced change, so anyone can try them.</summary>
    private Task SetKnownPasswordAsync(Guid userId) => InTenantAsync(async services =>
    {
        var users = services.GetRequiredService<IdentityUserManager>();
        var user = await users.GetByIdAsync(userId);
        Check(await users.RemovePasswordAsync(user));
        Check(await users.AddPasswordAsync(user, _demoPassword));
        user.SetMustChangePassword(false);
        Check(await users.UpdateAsync(user));
        return true;
    });

    private Task<Guid?> FindRoleHolderAsync(string role) => InTenantAsync(async services =>
    {
        var holders = await services.GetRequiredService<IdentityUserManager>().GetUsersInRoleAsync(role);
        var employees = services.GetRequiredService<IRepository<Employee, Guid>>();
        foreach (var user in holders.Where(x => x.IsActive).OrderBy(x => x.CreationTime))
        {
            if (await employees.AnyAsync(x => x.UserId == user.Id && x.Status == EmployeeStatus.Active))
            {
                return (Guid?)user.Id;
            }
        }

        return null;
    });

    private static void Check(Microsoft.AspNetCore.Identity.IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
        }
    }

    // ---------------------------------------------------------------- finance

    private async Task SeedFinanceAsync()
    {
        // The demo people got their base salary from HR at hiring. The accountant adds a second version with
        // allowances and deductions, and opens a profile for the company's own employees who have none, so they get
        // payslips too. Nobody changes their own financial records, so the accountant's own profile is left alone. The
        // executive director has no salary in the system, and a system admin with no business role is not an employee.
        var accountantEmployeeId = await InTenantAsync(async services =>
            (await services.GetRequiredService<IRepository<Employee, Guid>>().FirstOrDefaultAsync(x => x.UserId == _accountantUserId))?.Id);
        var targets = await InTenantAsync(async services =>
        {
            var employees = await services.GetRequiredService<IRepository<Employee, Guid>>()
                .GetListAsync(x => x.Status == EmployeeStatus.Active && x.UserId != null && x.Id != _executiveEmployeeId && x.Id != accountantEmployeeId);
            var profiles = await services.GetRequiredService<IRepository<SalaryProfile, Guid>>().GetListAsync();
            var users = services.GetRequiredService<IdentityUserManager>();
            var result = new List<(Guid EmployeeId, string? Stamp)>();
            foreach (var employee in employees)
            {
                var latest = profiles.Where(p => p.EmployeeId == employee.Id).OrderByDescending(p => p.EffectiveFrom).FirstOrDefault();
                var isDemo = _people.Values.Any(p => p.EmployeeId == employee.Id);
                if (latest != null && !isDemo)
                {
                    continue;
                }

                var roles = (await users.GetRolesAsync(await users.GetByIdAsync(employee.UserId!.Value))).ToList();
                if (roles.All(r => r == HrRoles.SystemAdmin || r == HrRoles.Employee) && roles.Contains(HrRoles.SystemAdmin))
                {
                    continue;
                }

                result.Add((employee.Id, latest?.ConcurrencyStamp));
            }

            return result;
        });

        var index = 0;
        foreach (var (employeeId, stamp) in targets)
        {
            var person = _people.Values.FirstOrDefault(x => x.EmployeeId == employeeId);
            var baseSalary = person?.BaseSalary ?? 1_200_000m;
            var components = new List<SalaryComponentInputDto>
            {
                new() { Name = "بدل نقل", Type = SalaryComponentType.Allowance, Amount = 100_000m },
                new() { Name = "ضمان اجتماعي", Type = SalaryComponentType.FixedDeduction, Amount = Math.Round(baseSalary * 0.05m / 1000m) * 1000m }
            };
            if (index % 3 == 0)
            {
                components.Add(new() { Name = "بدل طعام", Type = SalaryComponentType.Allowance, Amount = 75_000m });
            }

            if (index % 4 == 1)
            {
                components.Add(new() { Name = "قسط سلفة", Type = SalaryComponentType.FixedDeduction, Amount = 100_000m });
            }

            index++;
            await AsAsync(_accountantUserId, services => services.GetRequiredService<ISalaryProfileAppService>().UpdateAsync(employeeId, new SalaryProfileUpdateInputDto
            {
                BaseSalary = baseSalary,
                Currency = Currency,
                // After every demo hire date, and before the payroll months.
                EffectiveFrom = new DateOnly(2025, 6, 1),
                Components = components,
                ConcurrencyStamp = stamp
            }));
        }

        _log($"Salary profiles set by the accountant: {targets.Count}.");

        // Two closed and delivered months, so payslips show, and the current month as a draft.
        var thisMonth = new DateOnly(_today.Year, _today.Month, 1);
        foreach (var month in new[] { thisMonth.AddMonths(-2), thisMonth.AddMonths(-1) })
        {
            await RunPayrollAsync($"{month:yyyy-MM}", close: true);
        }

        await RunPayrollAsync($"{thisMonth:yyyy-MM}", close: false);
    }

    private async Task RunPayrollAsync(string period, bool close)
    {
        var existing = await AsAsync(_accountantUserId, services => services.GetRequiredService<IPayrollRunAppService>()
            .GetListAsync(new PayrollRunListInput { MaxResultCount = 200 }));
        if (existing.Items.Any(x => x.Period == period))
        {
            _log($"Payroll {period}: the company already has a run, left as it is.");
            return;
        }

        var run = await AsAsync(_accountantUserId, services => services.GetRequiredService<IPayrollRunAppService>()
            .CreateAsync(new PayrollRunCreateInputDto { Period = period }, Guid.NewGuid()));
        if (!close)
        {
            _log($"Payroll {period}: draft.");
            return;
        }

        run = await AsAsync(_accountantUserId, services => services.GetRequiredService<IPayrollRunAppService>()
            .RequestApprovalAsync(run.Id, new PayrollRunActionInputDto { ConcurrencyStamp = run.ConcurrencyStamp }));
        run = await AsAsync(_hrUserId, services => services.GetRequiredService<IPayrollRunAppService>()
            .ApproveAsync(run.Id, new PayrollRunActionInputDto { ConcurrencyStamp = run.ConcurrencyStamp, Note = "معتمد" }));
        run = await AsAsync(_accountantUserId, services => services.GetRequiredService<IPayrollRunAppService>()
            .CloseAsync(run.Id, new PayrollRunActionInputDto { ConcurrencyStamp = run.ConcurrencyStamp }));
        var lastDay = DateOnly.ParseExact(period + "-01", "yyyy-MM-dd").AddMonths(1).AddDays(-1);
        await AsAsync(_accountantUserId, services => services.GetRequiredService<IPayrollRunAppService>()
            .DeliverAsync(run.Id, new PayrollDeliveryInputDto { DeliveredOn = lastDay < _today ? lastDay : _today }));
        _log($"Payroll {period}: closed and delivered, {run.Items.Count} payslips.");
    }
}

public record DemoPerson(string Key, Guid EmployeeId, Guid UserId, string UserName, string NameEn, string[] Roles, decimal BaseSalary, bool HasShift, Guid? ManagerUserId);
