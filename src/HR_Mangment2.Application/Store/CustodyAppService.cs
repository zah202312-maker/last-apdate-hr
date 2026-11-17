using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using HR_Mangment2.Departments;
using HR_Mangment2.Employees;
using HR_Mangment2.Errors;
using HR_Mangment2.Idempotency;
using HR_Mangment2.Identity;
using HR_Mangment2.Leaves;
using HR_Mangment2.Notifications;
using HR_Mangment2.Permissions;
using HR_Mangment2.Warnings;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace HR_Mangment2.Store;

[Authorize]
public class CustodyAppService : HR_Mangment2AppService, ICustodyAppService
{
    private const string CreateOperation = "Custody_Create";
    private const string CreateBatchOperation = "Custody_CreateBatch";

    private static readonly Dictionary<string, string> SortFields = new()
    {
        ["assignedAt"] = nameof(Custody.AssignedAt),
        ["status"] = nameof(Custody.Status),
        ["kind"] = nameof(Custody.Kind),
        ["quantity"] = nameof(Custody.Quantity)
    };

    private readonly IRepository<Custody, Guid> _custodies;
    private readonly IRepository<Item, Guid> _items;
    private readonly IRepository<Employee, Guid> _employees;
    private readonly IRepository<Department, Guid> _departments;
    private readonly StockLedger _ledger;
    private readonly CustodyDepartmentScope _scope;
    private readonly ItemReferenceReader _itemRefs;
    private readonly EmployeeReferenceReader _references;
    private readonly HrNotificationPublisher _notifications;
    private readonly IIdentityUserRepository _users;
    private readonly IdempotencyStore _idempotency;
    private readonly StoreAttachmentManager _attachments;

    public CustodyAppService(
        IRepository<Custody, Guid> custodies,
        IRepository<Item, Guid> items,
        IRepository<Employee, Guid> employees,
        IRepository<Department, Guid> departments,
        StockLedger ledger,
        CustodyDepartmentScope scope,
        ItemReferenceReader itemRefs,
        EmployeeReferenceReader references,
        HrNotificationPublisher notifications,
        IIdentityUserRepository users,
        IdempotencyStore idempotency,
        StoreAttachmentManager attachments)
    {
        _custodies = custodies;
        _items = items;
        _employees = employees;
        _departments = departments;
        _ledger = ledger;
        _scope = scope;
        _itemRefs = itemRefs;
        _references = references;
        _notifications = notifications;
        _users = users;
        _idempotency = idempotency;
        _attachments = attachments;
    }

    [Authorize(HrPermissions.CustodyPermissions.Create)]
    public virtual async Task<CustodyDto> CreateAsync(CustodyCreateInputDto input, Guid? idempotencyKey)
    {
        var earlier = await _idempotency.FindResourceAsync(CreateOperation, idempotencyKey, input);
        if (earlier != null)
        {
            return await ToDtoAsync(await _custodies.GetAsync(earlier.Value));
        }

        var created = await CreateCoreAsync(input.Kind, input.EmployeeId, input.DepartmentId, new[]
        {
            new CustodyBatchLineInputDto
            {
                ItemId = input.ItemId,
                Quantity = input.Quantity,
                SerialNumber = input.SerialNumber,
                Condition = input.Condition,
                DeductFromStock = input.DeductFromStock,
                Note = input.Note
            }
        });

        var custody = created.Single();
        await _idempotency.SaveAsync(CreateOperation, idempotencyKey, input, custody.Id);
        return await ToDtoAsync(custody);
    }

    /// <summary>
    /// Several custody records for one holder, all saved or none (decision 2026-11-17). Each line is a custody of its
    /// own, acknowledged and returned on its own.
    /// </summary>
    [Authorize(HrPermissions.CustodyPermissions.Create)]
    public virtual async Task<CustodyBatchResultDto> CreateBatchAsync(CustodyBatchCreateInputDto input, Guid? idempotencyKey)
    {
        var earlier = await _idempotency.FindResourceAsync(CreateBatchOperation, idempotencyKey, input);
        if (earlier != null)
        {
            // The batch is found again through its first custody: same assigner and assignment time.
            var first = await _custodies.GetAsync(earlier.Value);
            var batch = await AsyncExecuter.ToListAsync((await _custodies.GetQueryableAsync())
                .Where(x => x.AssignedByUserId == first.AssignedByUserId && x.AssignedAt == first.AssignedAt)
                .OrderBy(x => x.CreationTime));
            return new CustodyBatchResultDto { Items = await ToDtosAsync(batch) };
        }

        var created = await CreateCoreAsync(input.Kind, input.EmployeeId, input.DepartmentId, input.Lines);
        await _idempotency.SaveAsync(CreateBatchOperation, idempotencyKey, input, created[0].Id);
        return new CustodyBatchResultDto { Items = await ToDtosAsync(created) };
    }

    [Authorize(HrPermissions.CustodyPermissions.Create)]
    public virtual async Task<AttachmentDto> UploadAttachmentAsync(Guid id, UploadedFileInput file)
    {
        await _custodies.GetAsync(id);
        return await _attachments.SaveAsync(id, file, CurrentUser.GetId());
    }

    [Authorize(HrPermissions.CustodyPermissions.Create)]
    public virtual async Task DeleteAttachmentAsync(Guid id)
    {
        await _custodies.GetAsync(id);
        await _attachments.RemoveAsync(id, CurrentUser.GetId());
    }

    /// <summary>Whoever may open the custody (Custody_Get) may download its attachment.</summary>
    [Authorize(HrPermissions.CustodyPermissions.View)]
    public virtual async Task<FileDownloadUrlDto> GetAttachmentDownloadUrlAsync(Guid id)
    {
        await CheckCanViewAsync(await _custodies.GetAsync(id));
        return await _attachments.LinkAsync(id);
    }

    private async Task<List<Custody>> CreateCoreAsync(CustodyKind? kind, Guid? employeeId, Guid? departmentId, IReadOnlyList<CustodyBatchLineInputDto> lines)
    {
        await CheckHolderAsync(kind, employeeId, departmentId);

        var itemIds = lines.Select(x => x.ItemId!.Value).Distinct().ToList();
        var items = (await _items.GetListAsync(x => itemIds.Contains(x.Id))).ToDictionary(x => x.Id);
        if (items.Count != itemIds.Count)
        {
            throw new Volo.Abp.Domain.Entities.EntityNotFoundException(typeof(Item), itemIds.First(x => !items.ContainsKey(x)));
        }

        if (items.Values.Any(x => x.Kind != ItemKind.Returnable))
        {
            throw new BusinessException(HrErrorCodes.Custody.ItemNotReturnable);
        }

        if (lines.Any(x => !string.IsNullOrWhiteSpace(x.SerialNumber) && x.Quantity != 1))
        {
            throw new BusinessException(HrErrorCodes.Custody.SerialNumberRequiresSingleQuantity);
        }

        // Checked first, on the total per item, because a custody row must never exist for a deduction that fails.
        var deducted = lines.Where(x => x.DeductFromStock == true).GroupBy(x => x.ItemId!.Value);
        if (deducted.Any(g => items[g.Key].QuantityOnHand < g.Sum(x => x.Quantity!.Value)))
        {
            throw new BusinessException(HrErrorCodes.Stock.InsufficientStock);
        }

        var me = CurrentUser.GetId();
        var now = Clock.Now;
        var created = new List<Custody>();
        foreach (var line in lines)
        {
            var item = items[line.ItemId!.Value];
            var note = string.IsNullOrWhiteSpace(line.Note) ? null : line.Note.Trim();
            var custody = await _custodies.InsertAsync(
                new Custody(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    item.Id,
                    kind!.Value,
                    employeeId,
                    departmentId,
                    line.Quantity!.Value,
                    now,
                    me,
                    null,
                    null,
                    note,
                    string.IsNullOrWhiteSpace(line.SerialNumber) ? null : line.SerialNumber.Trim(),
                    line.Condition ?? CustodyItemCondition.New),
                autoSave: true);

            if (line.DeductFromStock == true)
            {
                await _ledger.RecordAsync(item, new StockMovementDraft(
                    StockMovementType.Issue,
                    -line.Quantity!.Value,
                    me,
                    note,
                    CustodyId: custody.Id,
                    EmployeeId: employeeId));
            }

            await NotifyHoldersAsync(custody, NotificationTemplateCode.CustodyAssigned);
            created.Add(custody);
        }

        return created;
    }

    [Authorize(HrPermissions.CustodyPermissions.ViewAll)]
    public virtual async Task<CustodyPagedResultDto> GetListAsync(CustodyListInput input)
    {
        var query = (await _custodies.GetQueryableAsync())
            .WhereIf(input.Kind != null, x => x.Kind == input.Kind)
            .WhereIf(input.Status != null, x => x.Status == input.Status)
            .WhereIf(input.EmployeeId != null, x => x.EmployeeId == input.EmployeeId)
            .WhereIf(input.DepartmentId != null, x => x.DepartmentId == input.DepartmentId)
            .WhereIf(input.ItemId != null, x => x.ItemId == input.ItemId);

        return await ToPageAsync(query, input);
    }

    /// <summary>Personal custody only. Shared custody in the user's department is not theirs.</summary>
    [Authorize(HrPermissions.CustodyPermissions.ViewOwn)]
    public virtual async Task<CustodyPagedResultDto> GetMyAsync(CustodyMyListInput input)
    {
        var me = await _employees.FirstOrDefaultAsync(x => x.UserId == CurrentUser.GetId());
        if (me == null)
        {
            return new CustodyPagedResultDto();
        }

        var query = (await _custodies.GetQueryableAsync())
            .Where(x => x.EmployeeId == me.Id)
            .WhereIf(input.Status != null, x => x.Status == input.Status);

        return await ToPageAsync(query, input);
    }

    [Authorize(HrPermissions.CustodyPermissions.ViewTeam)]
    public virtual async Task<CustodyPagedResultDto> GetTeamAsync(CustodyTeamListInput input)
    {
        var me = await _employees.FirstOrDefaultAsync(x => x.UserId == CurrentUser.GetId());
        if (me == null)
        {
            return new CustodyPagedResultDto();
        }

        var team = await AsyncExecuter.ToListAsync((await _employees.GetQueryableAsync()).Where(x => x.ManagerEmployeeId == me.Id).Select(x => x.Id));
        var departments = await _scope.GetManagedDepartmentIdsAsync(CurrentUser.GetId());

        var query = (await _custodies.GetQueryableAsync())
            .Where(x =>
                (x.EmployeeId != null && team.Contains(x.EmployeeId.Value)) ||
                (input.EmployeeId == null && x.DepartmentId != null && departments.Contains(x.DepartmentId.Value)))
            .WhereIf(input.EmployeeId != null, x => x.EmployeeId == input.EmployeeId)
            .WhereIf(input.Kind != null, x => x.Kind == input.Kind)
            .WhereIf(input.Status != null, x => x.Status == input.Status);

        return await ToPageAsync(query, input);
    }

    [Authorize(HrPermissions.CustodyPermissions.View)]
    public virtual async Task<CustodyDto> GetAsync(Guid id)
    {
        var custody = await _custodies.GetAsync(id);
        await CheckCanViewAsync(custody);
        return await ToDtoAsync(custody);
    }

    private async Task CheckCanViewAsync(Custody custody)
    {
        var userId = CurrentUser.GetId();
        var roles = await _users.GetRoleNamesAsync(userId);

        if (!roles.Contains(HrRoles.StoreOfficer) && !roles.Contains(HrRoles.HrManager) && !await IsHolderOrManagerAsync(custody, userId))
        {
            throw new AbpAuthorizationException();
        }
    }

    [Authorize(HrPermissions.CustodyPermissions.Acknowledge)]
    public virtual async Task<CustodyDto> AcknowledgeAsync(Guid id)
    {
        var custody = await _custodies.GetAsync(id);
        var userId = CurrentUser.GetId();

        if (!await CanAcknowledgeAsync(custody, userId))
        {
            throw new AbpAuthorizationException();
        }

        switch (custody.Status)
        {
            case CustodyStatus.Acknowledged:
                throw new BusinessException(HrErrorCodes.Custody.AlreadyAcknowledged);
            case CustodyStatus.Returned:
                throw new BusinessException(HrErrorCodes.Custody.AlreadyReturned);
        }

        custody.Acknowledge(userId, Clock.Now);
        await _custodies.UpdateAsync(custody, autoSave: true);
        return await ToDtoAsync(custody);
    }

    [Authorize(HrPermissions.CustodyPermissions.Return)]
    public virtual async Task<CustodyDto> ReturnAsync(Guid id, CustodyReturnInputDto input)
    {
        var custody = await _custodies.GetAsync(id);
        if (custody.Status == CustodyStatus.Returned)
        {
            throw new BusinessException(HrErrorCodes.Custody.AlreadyReturned);
        }

        var restock = input.Restock!.Value;
        if (!restock && string.IsNullOrWhiteSpace(input.Note))
        {
            throw new BusinessException(HrErrorCodes.Custody.ReturnNoteRequired);
        }

        var me = CurrentUser.GetId();
        custody.Return(me, Clock.Now, restock, input.Note);
        await _custodies.UpdateAsync(custody, autoSave: true);

        if (restock)
        {
            await _ledger.RecordAsync(await _items.GetAsync(custody.ItemId), new StockMovementDraft(
                StockMovementType.Return,
                custody.Quantity,
                me,
                input.Note,
                RequestId: custody.RequestId,
                StockIssueId: custody.StockIssueId,
                CustodyId: custody.Id,
                EmployeeId: custody.EmployeeId));
        }

        await NotifyHoldersAsync(custody, NotificationTemplateCode.CustodyReturned);
        return await ToDtoAsync(custody);
    }

    private async Task CheckHolderAsync(CustodyKind? kind, Guid? employeeId, Guid? departmentId)
    {
        var valid = kind == CustodyKind.Personal
            ? employeeId != null && departmentId == null &&
              await _employees.AnyAsync(x => x.Id == employeeId && x.Status != EmployeeStatus.Ended)
            : departmentId != null && employeeId == null &&
              await _departments.AnyAsync(x => x.Id == departmentId);

        if (!valid)
        {
            throw new BusinessException(HrErrorCodes.Custody.InvalidHolder);
        }
    }

    private async Task<bool> CanAcknowledgeAsync(Custody custody, Guid userId)
    {
        if (custody.Kind == CustodyKind.Shared)
        {
            return (await _scope.GetManagedDepartmentIdsAsync(userId)).Contains(custody.DepartmentId!.Value);
        }

        return await _employees.AnyAsync(x => x.Id == custody.EmployeeId && x.UserId == userId);
    }

    private async Task<bool> IsHolderOrManagerAsync(Custody custody, Guid userId)
    {
        if (custody.Kind == CustodyKind.Shared)
        {
            return (await _scope.GetManagedDepartmentIdsAsync(userId)).Contains(custody.DepartmentId!.Value);
        }

        var holder = await _employees.GetAsync(custody.EmployeeId!.Value);
        if (holder.UserId == userId)
        {
            return true;
        }

        return holder.ManagerEmployeeId != null &&
               (await _users.GetRoleNamesAsync(userId)).Contains(HrRoles.DepartmentManager) &&
               await _employees.AnyAsync(x => x.Id == holder.ManagerEmployeeId && x.UserId == userId);
    }

    private async Task NotifyHoldersAsync(Custody custody, NotificationTemplateCode template)
    {
        List<Guid> recipients;
        if (custody.Kind == CustodyKind.Shared)
        {
            recipients = await _scope.GetDepartmentManagerUserIdsAsync(custody.DepartmentId!.Value);
        }
        else
        {
            var holder = await _employees.GetAsync(custody.EmployeeId!.Value);
            recipients = holder.UserId == null ? new List<Guid>() : new List<Guid> { holder.UserId.Value };
        }

        foreach (var recipient in recipients)
        {
            await _notifications.PublishAsync(recipient, template, NotificationResourceType.Custody, custody.Id);
        }
    }

    private async Task<CustodyPagedResultDto> ToPageAsync(IQueryable<Custody> query, PagedAndSortedResultRequestDto input)
    {
        var total = await AsyncExecuter.LongCountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(
            query.ApplySorting(input.Sorting, nameof(Custody.AssignedAt) + " desc", SortFields).Skip(input.SkipCount).Take(input.MaxResultCount));

        return new CustodyPagedResultDto { TotalCount = total, Items = await ToDtosAsync(rows) };
    }

    private async Task<CustodyDto> ToDtoAsync(Custody custody) => (await ToDtosAsync(new[] { custody })).Single();

    private async Task<List<CustodyDto>> ToDtosAsync(IReadOnlyCollection<Custody> rows)
    {
        var items = await _itemRefs.GetRefsAsync(rows.Select(x => x.ItemId));
        var employees = await _references.GetRefsAsync(rows.Where(x => x.EmployeeId != null).Select(x => x.EmployeeId!.Value));
        var departments = await _references.GetDepartmentsAsync(rows.Where(x => x.DepartmentId != null).Select(x => x.DepartmentId!.Value));
        var users = await _references.GetUserRefsAsync(rows
            .Select(x => x.AssignedByUserId)
            .Concat(rows.Where(x => x.AcknowledgedByUserId != null).Select(x => x.AcknowledgedByUserId!.Value)));
        var attachments = await _attachments.FindManyAsync(rows.Select(x => x.Id));

        return rows.Select(x => new CustodyDto
        {
            Id = x.Id,
            Item = items[x.ItemId],
            Kind = x.Kind,
            Employee = x.EmployeeId == null ? null : employees[x.EmployeeId.Value],
            Department = x.DepartmentId == null ? null : departments.GetValueOrDefault(x.DepartmentId.Value),
            Quantity = x.Quantity,
            Status = x.Status,
            AssignedAt = x.AssignedAt,
            AssignedBy = users[x.AssignedByUserId],
            AcknowledgedAt = x.AcknowledgedAt,
            AcknowledgedBy = x.AcknowledgedByUserId == null ? null : users[x.AcknowledgedByUserId.Value],
            ReturnedAt = x.ReturnedAt,
            RequestId = x.RequestId,
            StockIssueId = x.StockIssueId,
            Note = x.ReturnNote ?? x.Note,
            SerialNumber = x.SerialNumber,
            Condition = x.Condition,
            Attachment = attachments.TryGetValue(x.Id, out var attachment) ? StoreAttachmentManager.ToDto(attachment) : null
        }).ToList();
    }
}

public class CustodyCreateInputDtoValidator : AbstractValidator<CustodyCreateInputDto>
{
    public CustodyCreateInputDtoValidator()
    {
        RuleFor(x => x.ItemId).NotNull();
        RuleFor(x => x.Kind).NotNull();
        RuleFor(x => x.Quantity).NotNull().GreaterThan(0).PrecisionScale(StoreConsts.QuantityPrecision, StoreConsts.QuantityScale, true);
        RuleFor(x => x.DeductFromStock).NotNull();
        RuleFor(x => x.Note).MaximumLength(StoreConsts.MaxNoteLength);
        RuleFor(x => x.SerialNumber).MaximumLength(StoreConsts.MaxSerialNumberLength);
        RuleFor(x => x.Condition).NotNull();
    }
}

public class CustodyBatchCreateInputDtoValidator : AbstractValidator<CustodyBatchCreateInputDto>
{
    public CustodyBatchCreateInputDtoValidator()
    {
        RuleFor(x => x.Kind).NotNull();
        RuleFor(x => x.Lines).NotNull().Must(x => x.Count is >= 1 and <= StoreConsts.MaxCustodyBatchLines);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ItemId).NotNull();
            line.RuleFor(x => x.Quantity).NotNull().GreaterThan(0).PrecisionScale(StoreConsts.QuantityPrecision, StoreConsts.QuantityScale, true);
            line.RuleFor(x => x.SerialNumber).MaximumLength(StoreConsts.MaxSerialNumberLength);
            line.RuleFor(x => x.Condition).NotNull();
            line.RuleFor(x => x.DeductFromStock).NotNull();
            line.RuleFor(x => x.Note).MaximumLength(StoreConsts.MaxNoteLength);
        });
    }
}

public class CustodyReturnInputDtoValidator : AbstractValidator<CustodyReturnInputDto>
{
    public CustodyReturnInputDtoValidator()
    {
        RuleFor(x => x.Restock).NotNull();
        RuleFor(x => x.Note).MaximumLength(StoreConsts.MaxNoteLength);
    }
}
