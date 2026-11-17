using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;

namespace HR_Mangment2.Identity;

/// <summary>
/// x-global-rules.access-gates: computed from the account in the database on every request, never from the token.
/// </summary>
public class AccountAccessEvaluator : ITransientDependency
{
    private readonly IIdentityUserRepository _userRepository;
    private readonly IEnumerable<IMfaRequirementContributor> _mfaContributors;

    public AccountAccessEvaluator(IIdentityUserRepository userRepository, IEnumerable<IMfaRequirementContributor> mfaContributors)
    {
        _userRepository = userRepository;
        _mfaContributors = mfaContributors;
    }

    public virtual async Task<AccountAccessState> GetAsync(Guid userId)
    {
        var user = await _userRepository.FindAsync(userId, includeDetails: false);
        if (user == null)
        {
            return AccountAccessState.Missing;
        }

        var mfaRequired = await IsMfaRequiredAsync(user);
        var mustChange = user.GetMustChangePassword();

        var gate = mustChange
            ? AccessGate.PasswordChangeRequired
            : mfaRequired && !user.TwoFactorEnabled
                ? AccessGate.MfaEnrollmentRequired
                : AccessGate.None;

        return new AccountAccessState(true, user.IsActive, gate, mustChange, mfaRequired, user.TwoFactorEnabled);
    }

    /// <summary>
    /// Decision 2026-11-14: two-step verification is required of no one, complaint handlers included (QUESTIONS.md item B cancelled).
    /// No contributor is registered, so this is false; the hook stays in case a module needs it again.
    /// </summary>
    public virtual async Task<bool> IsMfaRequiredAsync(IdentityUser user)
    {
        foreach (var contributor in _mfaContributors)
        {
            if (await contributor.IsRequiredAsync(user.Id))
            {
                return true;
            }
        }

        return false;
    }
}
