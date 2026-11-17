using System;
using System.Threading.Tasks;

namespace HR_Mangment2.Identity;

/// <summary>Lets a module require two-step verification. None does since 2026-11-14.</summary>
public interface IMfaRequirementContributor
{
    Task<bool> IsRequiredAsync(Guid userId);
}
