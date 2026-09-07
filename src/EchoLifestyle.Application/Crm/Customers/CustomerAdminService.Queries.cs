using System.Globalization;
using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Application.Common.Text;
using EchoLifestyle.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Crm.Customers;

/// <summary>
/// Reading customers back.
///
/// Contact details leave this class already masked for anybody without
/// <c>Crm.Customer.ViewPii</c>. Masking here rather than in the views means a
/// new screen, a JSON endpoint or an export cannot forget to do it - the number
/// simply is not in the object they receive.
/// </summary>
public partial class CustomerAdminService
{
    public async Task<PagedResult<CustomerListItem>> ListAsync(
        string? search,
        int skip,
        int take,
        string? sortColumn,
        bool sortDescending,
        StatusFilter status,
        bool blockedOnly,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Customers.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        query = status switch
        {
            StatusFilter.Active => query.Where(c => c.IsActive),
            StatusFilter.Inactive => query.Where(c => !c.IsActive),
            _ => query,
        };

        if (blockedOnly)
        {
            query = query.Where(c => c.IsBlocked);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            // A number is searched in its normalised form as well as raw, so
            // pasting "+880 1712-345678" out of a Messenger thread finds the
            // customer. Without this the search box quietly fails on exactly
            // the input people paste most.
            var asPhone = BangladeshPhone.Normalise(term);

            query = query.Where(c =>
                EF.Functions.Like(c.FullName, $"%{term}%")
                || EF.Functions.Like(c.Code, $"%{term}%")
                || EF.Functions.Like(c.Phone, $"%{term}%")
                || (asPhone != null && c.Phone == asPhone)
                || (c.AlternatePhone != null && EF.Functions.Like(c.AlternatePhone, $"%{term}%"))
                || (c.Email != null && EF.Functions.Like(c.Email, $"%{term}%")));
        }

        var filteredCount = await query.CountAsync(cancellationToken);

        query = (sortColumn, sortDescending) switch
        {
            ("code", false) => query.OrderBy(c => c.Code),
            ("code", true) => query.OrderByDescending(c => c.Code),
            ("name", false) => query.OrderBy(c => c.FullName),
            ("name", true) => query.OrderByDescending(c => c.FullName),
            ("created", false) => query.OrderBy(c => c.CreatedAtUtc),

            // Newest first: the customer somebody is looking for is usually the
            // one who just ordered.
            _ => query.OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.Id),
        };

        var rows = await query
            .Skip(skip)
            .Take(take)
            .Select(c => new CustomerListItem
            {
                Id = c.Id,
                Code = c.Code,
                FullName = c.FullName,
                Phone = c.Phone,
                Email = c.Email,
                CustomerType = c.CustomerType,
                Source = c.Source,
                DistrictName = c.Addresses
                    .Where(a => a.IsDefault)
                    .Select(a => a.District!.Name)
                    .FirstOrDefault()
                    ?? c.Addresses.Select(a => a.District!.Name).FirstOrDefault(),
                AddressCount = c.Addresses.Count,
                IsBlocked = c.IsBlocked,
                BlockReason = c.BlockReason,
                HasAccount = c.UserId != null,
                IsActive = c.IsActive,
                CreatedAtUtc = c.CreatedAtUtc,
            })
            .ToListAsync(cancellationToken);

        if (!CanSeePii)
        {
            foreach (var row in rows)
            {
                row.Phone = BangladeshPhone.Mask(row.Phone);
                row.Email = MaskEmail(row.Email);
            }
        }

        return new PagedResult<CustomerListItem>(rows, totalCount, filteredCount);
    }

    /// <summary>
    /// One customer with their addresses.
    /// </summary>
    /// <param name="recordAccess">
    /// Log that somebody read this customer's contact details. Set by the
    /// screen a person opened, not by internal callers - an order screen
    /// resolving a delivery address is the system doing its job, and logging it
    /// would bury the reads that actually matter.
    /// </param>
    public async Task<CustomerDetail?> GetAsync(
        long id,
        bool recordAccess = false,
        CancellationToken cancellationToken = default)
    {
        var customer = await _db.Customers
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CustomerDetail
            {
                Id = c.Id,
                Code = c.Code,
                FullName = c.FullName,
                Phone = c.Phone,
                AlternatePhone = c.AlternatePhone,
                Email = c.Email,
                CustomerType = c.CustomerType,
                PriceListId = c.PriceListId,
                PriceListName = c.PriceList!.Name,
                Source = c.Source,
                DateOfBirth = c.DateOfBirth,
                Notes = c.Notes,
                IsBlocked = c.IsBlocked,
                BlockReason = c.BlockReason,
                BlockedAtUtc = c.BlockedAtUtc,
                HasAccount = c.UserId != null,
                IsActive = c.IsActive,
                CreatedAtUtc = c.CreatedAtUtc,
                Addresses = c.Addresses
                    .OrderByDescending(a => a.IsDefault)
                    .ThenBy(a => a.Label)
                    .Select(a => new CustomerAddressItem
                    {
                        Id = a.Id,
                        Label = a.Label,
                        RecipientName = a.RecipientName,
                        RecipientPhone = a.RecipientPhone,
                        DivisionId = a.DivisionId,
                        DivisionName = a.Division!.Name,
                        DistrictId = a.DistrictId,
                        DistrictName = a.District!.Name,
                        IsInsideCity = a.District.IsInsideCity,
                        AreaOrThana = a.AreaOrThana,
                        AddressLine = a.AddressLine,
                        Landmark = a.Landmark,
                        PostCode = a.PostCode,
                        DeliveryNotes = a.DeliveryNotes,
                        IsDefault = a.IsDefault,
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (customer is null)
        {
            return null;
        }

        if (!CanSeePii)
        {
            customer.Phone = BangladeshPhone.Mask(customer.Phone);
            customer.AlternatePhone = customer.AlternatePhone is null
                ? null
                : BangladeshPhone.Mask(customer.AlternatePhone);
            customer.Email = MaskEmail(customer.Email);

            foreach (var address in customer.Addresses)
            {
                address.RecipientPhone = BangladeshPhone.Mask(address.RecipientPhone);

                // The street line is as identifying as the number. District and
                // area stay visible so somebody can still answer "where is this
                // going?" without being handed the doorstep.
                address.AddressLine = "•••••";
                address.Landmark = null;
            }
        }
        else if (recordAccess && !_currentUser.IsOwner)
        {
            // Owners are not logged. See AuditActions.CustomerPiiViewed for
            // why, and for the rule that an export must log regardless.
            await _audit.LogAsync(
                AuditActions.CustomerPiiViewed,
                nameof(Customer),
                customer.Id.ToString(CultureInfo.InvariantCulture),
                $"Viewed contact details for {customer.Code} - {customer.FullName}.",
                new { customer.Code },
                cancellationToken: cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        }

        return customer;
    }

    /// <summary>
    /// Customers matching a term, for the order screen's picker.
    ///
    /// An exact phone match wins outright - somebody pasting a number from a
    /// chat wants that one customer at the top, not third behind two people
    /// whose names contain the digits.
    /// </summary>
    public async Task<IReadOnlyList<CustomerLookupItem>> LookupAsync(
        string? term,
        int take = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
        {
            return [];
        }

        var search = term.Trim();
        var asPhone = BangladeshPhone.Normalise(search);

        var rows = await _db.Customers
            .AsNoTracking()
            .Where(c => c.IsActive
                        && (EF.Functions.Like(c.FullName, $"%{search}%")
                            || EF.Functions.Like(c.Code, $"%{search}%")
                            || EF.Functions.Like(c.Phone, $"%{search}%")
                            || (asPhone != null && c.Phone == asPhone)
                            || (c.AlternatePhone != null
                                && EF.Functions.Like(c.AlternatePhone, $"%{search}%"))))
            .Select(c => new CustomerLookupItem
            {
                Id = c.Id,
                Code = c.Code,
                FullName = c.FullName,
                Phone = c.Phone,
                IsBlocked = c.IsBlocked,
                BlockReason = c.BlockReason,
                DefaultAddress = c.Addresses
                    .Where(a => a.IsDefault)
                    .Select(a => a.AreaOrThana + ", " + a.District!.Name)
                    .FirstOrDefault(),
                Rank = asPhone != null && c.Phone == asPhone ? 0
                    : c.Phone.StartsWith(search) ? 1
                    : c.FullName.StartsWith(search) ? 2
                    : 3,
            })
            .OrderBy(c => c.Rank)
            .ThenBy(c => c.FullName)
            .Take(take)
            .ToListAsync(cancellationToken);

        // The picker keeps the real number even for a masked viewer - a
        // salesperson has to confirm they have the right person - but formats
        // it rather than handing over a raw string to copy in bulk.
        foreach (var row in rows)
        {
            row.Phone = CanSeePii ? BangladeshPhone.Format(row.Phone) : BangladeshPhone.Mask(row.Phone);
        }

        return rows;
    }

    // -----------------------------------------------------------------------
    // Geography
    // -----------------------------------------------------------------------

    public async Task<IReadOnlyList<DivisionOption>> GetDivisionsAsync(
        CancellationToken cancellationToken = default) =>
        await _db.Divisions
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.DisplayOrder)
            .Select(d => new DivisionOption { Id = d.Id, Name = d.Name })
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Every district, with its division, in one call.
    ///
    /// Sixty-four rows. Fetching them all once and filtering in the browser is
    /// faster and simpler than a round trip every time somebody changes the
    /// division dropdown, and it works when the connection does not.
    /// </summary>
    public async Task<IReadOnlyList<DistrictOption>> GetDistrictsAsync(
        CancellationToken cancellationToken = default) =>
        await _db.Districts
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => new DistrictOption
            {
                Id = d.Id,
                DivisionId = d.DivisionId,
                Name = d.Name,
                FormerName = d.FormerName,
                IsInsideCity = d.IsInsideCity,
            })
            .ToListAsync(cancellationToken);

    /// <summary>
    /// a•••@gmail.com - the domain is rarely identifying, the local part always
    /// is.
    /// </summary>
    private static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);

        return at <= 0 ? "•••••" : $"{email[0]}•••{email[at..]}";
    }
}
