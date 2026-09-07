using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Finance.Cash;

/// <summary>
/// Maintaining who the partners are.
///
/// Owner accounts get a partner record automatically on first run, so this
/// exists for the cases seeding cannot cover: somebody who put money in without
/// ever having a login, and a partner who leaves while their capital account
/// stays open. Partners are deactivated, never deleted - the transactions
/// pointing at them are permanent.
/// </summary>
public class PartnerService
{
    private readonly IApplicationDbContext _db;

    public PartnerService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<OperationResult<long>> SaveAsync(
        SavePartnerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return OperationResult<long>.Failure(
                "What is the partner called?", nameof(SavePartnerRequest.Name));
        }

        var name = request.Name.Trim();

        if (await _db.Partners.AnyAsync(p => p.Name == name && p.Id != request.Id, cancellationToken))
        {
            return OperationResult<long>.Failure(
                $"There is already a partner called {name}.", nameof(SavePartnerRequest.Name));
        }

        if (request.OwnershipPercent is < 0m or > 100m)
        {
            return OperationResult<long>.Failure(
                "A share is between 0 and 100.", nameof(SavePartnerRequest.OwnershipPercent));
        }

        Partner partner;

        if (request.Id == 0)
        {
            partner = new Partner
            {
                DisplayOrder = await _db.Partners.CountAsync(cancellationToken) + 1,
            };

            _db.Partners.Add(partner);
        }
        else
        {
            var found = await _db.Partners
                .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

            if (found is null)
            {
                return OperationResult<long>.Failure("That partner no longer exists.");
            }

            partner = found;
        }

        if (!request.IsActive && partner.Id != 0)
        {
            // Deactivating the last partner would leave capital and drawings
            // unrecordable, with the existing balances still on screen and no
            // way to move them.
            var othersActive = await _db.Partners
                .AnyAsync(p => p.Id != partner.Id && p.IsActive, cancellationToken);

            if (!othersActive)
            {
                return OperationResult<long>.Failure(
                    $"{partner.Name} is the only active partner. Deactivating them would leave "
                    + "nowhere to record capital or drawings.",
                    nameof(SavePartnerRequest.IsActive));
            }
        }

        partner.Name = name;
        partner.OwnershipPercent = request.OwnershipPercent;
        partner.IsActive = request.IsActive;
        partner.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<long>.Success(partner.Id);
    }
}

public class SavePartnerRequest
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal? OwnershipPercent { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }
}
