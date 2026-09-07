using System.Globalization;
using System.Text.RegularExpressions;
using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Application.Common.Text;
using EchoLifestyle.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Crm.Customers;

/// <summary>
/// Customer administration.
///
/// One rule shapes everything here: the phone number is the identity. It is
/// normalised on the way in and unique in the database, so the same person
/// ordering from Messenger on Monday and Instagram on Friday attaches to one
/// record with one history. Getting that wrong is not a data-quality nuisance -
/// it silently destroys repeat-customer reporting and COD risk, and nothing
/// looks broken while it happens.
/// </summary>
public partial class CustomerAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditLogger _audit;

    public CustomerAdminService(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
    }

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9\-]{1,19}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s\.]+(\.[^@\s\.]+)+$")]
    private static partial Regex EmailPattern();

    /// <summary>Whether the caller may see unmasked contact details.</summary>
    public bool CanSeePii =>
        _currentUser.IsOwner || _currentUser.HasPermission(Permissions.Crm.CustomerViewPii);

    // -----------------------------------------------------------------------
    // Creating and editing
    // -----------------------------------------------------------------------

    public async Task<OperationResult<long>> CreateAsync(
        SaveCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = string.IsNullOrWhiteSpace(request.Code)
            ? await NextCodeAsync(cancellationToken)
            : request.Code.Trim().ToUpperInvariant();

        var validated = await ValidateAsync(request, code, existingId: null, cancellationToken);

        if (!validated.Succeeded)
        {
            return OperationResult<long>.Failure(validated.Error!, validated.Field);
        }

        var customer = new Customer
        {
            Code = code,
            FullName = request.FullName.Trim(),
            Phone = BangladeshPhone.Normalise(request.Phone)!,
            AlternatePhone = NormaliseAlternate(request.AlternatePhone),
            Email = Trim(request.Email)?.ToLowerInvariant(),
            CustomerType = request.CustomerType,
            PriceListId = request.CustomerType == CustomerType.Wholesale ? request.PriceListId : null,
            Source = request.Source,
            DateOfBirth = request.DateOfBirth,
            Notes = Trim(request.Notes),
            IsActive = request.IsActive,
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            AuditActions.CustomerCreated,
            nameof(Customer),
            customer.Id.ToString(CultureInfo.InvariantCulture),
            $"Created customer {customer.Code} - {customer.FullName}.",

            // The number is deliberately not in the audit detail. The trail is
            // read by more people than the customer screen is, and copying
            // contact details into it would route around the permission that
            // guards them.
            new { customer.Code, customer.FullName, customer.CustomerType, customer.Source },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<long>.Success(customer.Id);
    }

    /// <summary>
    /// A name and a number, nothing else.
    ///
    /// Returns the existing customer when the number is already known rather
    /// than failing: somebody typing an order does not want an error, they want
    /// the customer. That is also the behaviour that keeps duplicates out -
    /// the fast path is the one that reuses.
    /// </summary>
    public async Task<OperationResult<QuickCreateResult>> QuickCreateAsync(
        QuickCreateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var phone = BangladeshPhone.Normalise(request.Phone);

        if (phone is null)
        {
            return OperationResult<QuickCreateResult>.Failure(
                PhoneError(request.Phone), nameof(QuickCreateCustomerRequest.Phone));
        }

        var name = request.FullName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult<QuickCreateResult>.Failure(
                "Enter a name - even a first name is enough to find them again.",
                nameof(QuickCreateCustomerRequest.FullName));
        }

        var existing = await _db.Customers
            .Where(c => c.Phone == phone)
            .Select(c => new { c.Id, c.Code, c.FullName })
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return OperationResult<QuickCreateResult>.Success(new QuickCreateResult
            {
                Id = existing.Id,
                WasExisting = true,
                FullName = existing.FullName,
                Code = existing.Code,
            });
        }

        var created = await CreateAsync(
            new SaveCustomerRequest
            {
                FullName = name,
                Phone = phone,
                Source = request.Source,
                IsActive = true,
            },
            cancellationToken);

        if (!created.Succeeded)
        {
            return OperationResult<QuickCreateResult>.Failure(created.Error!, created.Field);
        }

        var code = await _db.Customers
            .Where(c => c.Id == created.Value)
            .Select(c => c.Code)
            .FirstAsync(cancellationToken);

        return OperationResult<QuickCreateResult>.Success(new QuickCreateResult
        {
            Id = created.Value,
            WasExisting = false,
            FullName = name,
            Code = code,
        });
    }

    public async Task<OperationResult> UpdateAsync(
        long id,
        SaveCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (customer is null)
        {
            return OperationResult.Failure("That customer no longer exists.");
        }

        var code = string.IsNullOrWhiteSpace(request.Code)
            ? customer.Code
            : request.Code.Trim().ToUpperInvariant();

        var validated = await ValidateAsync(request, code, customer.Id, cancellationToken);

        if (!validated.Succeeded)
        {
            return validated;
        }

        var before = new
        {
            customer.Code,
            customer.FullName,
            customer.CustomerType,
            customer.Source,
            customer.IsActive,
        };

        var phone = BangladeshPhone.Normalise(request.Phone)!;
        var phoneChanged = phone != customer.Phone;

        customer.Code = code;
        customer.FullName = request.FullName.Trim();
        customer.Phone = phone;
        customer.AlternatePhone = NormaliseAlternate(request.AlternatePhone);
        customer.Email = Trim(request.Email)?.ToLowerInvariant();
        customer.CustomerType = request.CustomerType;
        customer.PriceListId = request.CustomerType == CustomerType.Wholesale
            ? request.PriceListId
            : null;
        customer.Source = request.Source;
        customer.DateOfBirth = request.DateOfBirth;
        customer.Notes = Trim(request.Notes);
        customer.IsActive = request.IsActive;

        await _audit.LogAsync(
            AuditActions.CustomerUpdated,
            nameof(Customer),
            customer.Id.ToString(CultureInfo.InvariantCulture),
            $"Updated customer {customer.Code} - {customer.FullName}."
            + (phoneChanged ? " Phone number changed." : string.Empty),
            new
            {
                Before = before,
                After = new
                {
                    customer.Code,
                    customer.FullName,
                    customer.CustomerType,
                    customer.Source,
                    customer.IsActive,
                },

                // Recorded as a flag, not a value. That a number changed is
                // worth knowing; putting the number itself in the trail would
                // route around the permission that guards it.
                PhoneChanged = phoneChanged,
            },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    // -----------------------------------------------------------------------
    // Blocking
    // -----------------------------------------------------------------------

    /// <summary>
    /// Refuses further orders from this customer.
    ///
    /// Not a moral judgement - it is a cash-on-delivery business, and a customer
    /// who has refused three parcels has cost three courier fees. The person
    /// taking the next order needs to know before they confirm it.
    /// </summary>
    public async Task<OperationResult> BlockAsync(
        long id,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationResult.Failure(
                "Say why. A block with no reason cannot be argued with later, by anybody.",
                nameof(reason));
        }

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (customer is null)
        {
            return OperationResult.Failure("That customer no longer exists.");
        }

        customer.IsBlocked = true;
        customer.BlockReason = reason.Trim();
        customer.BlockedAtUtc = _clock.UtcNow;
        customer.BlockedByUserId = _currentUser.UserId;

        await _audit.LogAsync(
            AuditActions.CustomerBlocked,
            nameof(Customer),
            customer.Id.ToString(CultureInfo.InvariantCulture),
            $"Blocked {customer.Code} - {customer.FullName}: {reason.Trim()}",
            new { customer.Code, Reason = reason.Trim() },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    public async Task<OperationResult> UnblockAsync(
        long id,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (customer is null)
        {
            return OperationResult.Failure("That customer no longer exists.");
        }

        if (!customer.IsBlocked)
        {
            return OperationResult.Success();
        }

        var was = customer.BlockReason;

        customer.IsBlocked = false;
        customer.BlockReason = null;
        customer.BlockedAtUtc = null;
        customer.BlockedByUserId = null;

        await _audit.LogAsync(
            AuditActions.CustomerUnblocked,
            nameof(Customer),
            customer.Id.ToString(CultureInfo.InvariantCulture),
            $"Unblocked {customer.Code} - {customer.FullName}. {Trim(note)}".TrimEnd(),

            // The old reason is kept in the trail because the column is cleared:
            // without this, why they were blocked disappears the moment somebody
            // lifts it.
            new { customer.Code, PreviousReason = was, Note = Trim(note) },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    // -----------------------------------------------------------------------
    // Validation
    // -----------------------------------------------------------------------

    private async Task<OperationResult> ValidateAsync(
        SaveCustomerRequest request,
        string code,
        long? existingId,
        CancellationToken cancellationToken)
    {
        if (!CodePattern().IsMatch(code))
        {
            return OperationResult.Failure(
                "Use 2 to 20 characters: letters, numbers and dashes.",
                nameof(SaveCustomerRequest.Code));
        }

        if (await _db.Customers.AnyAsync(
                c => c.Code == code && (existingId == null || c.Id != existingId), cancellationToken))
        {
            return OperationResult.Failure(
                $"Customer code '{code}' is already in use.", nameof(SaveCustomerRequest.Code));
        }

        var name = request.FullName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Failure("Enter a name.", nameof(SaveCustomerRequest.FullName));
        }

        if (name.Length > 200)
        {
            return OperationResult.Failure("That name is too long.", nameof(SaveCustomerRequest.FullName));
        }

        var phone = BangladeshPhone.Normalise(request.Phone);

        if (phone is null)
        {
            return OperationResult.Failure(
                PhoneError(request.Phone), nameof(SaveCustomerRequest.Phone));
        }

        // The database enforces this too. Checking here is what turns a
        // constraint violation into a message that names the customer already
        // holding the number, so the person typing can go to them instead.
        var clash = await _db.Customers
            .Where(c => c.Phone == phone && (existingId == null || c.Id != existingId))
            .Select(c => new { c.Code, c.FullName })
            .FirstOrDefaultAsync(cancellationToken);

        if (clash is not null)
        {
            return OperationResult.Failure(
                $"{BangladeshPhone.Format(phone)} already belongs to {clash.FullName} ({clash.Code}). "
                + "One number is one customer - open that record instead of making a second.",
                nameof(SaveCustomerRequest.Phone));
        }

        var alternate = Trim(request.AlternatePhone);

        if (alternate is not null && BangladeshPhone.Normalise(alternate) is null && alternate.Length > 20)
        {
            return OperationResult.Failure(
                "That alternate number is too long.", nameof(SaveCustomerRequest.AlternatePhone));
        }

        var email = Trim(request.Email);

        if (email is not null && !EmailPattern().IsMatch(email))
        {
            return OperationResult.Failure(
                "That does not look like an email address. Leave it blank if they do not use one.",
                nameof(SaveCustomerRequest.Email));
        }

        var normalisedEmail = email?.ToLowerInvariant();

        if (normalisedEmail is not null && await _db.Customers.AnyAsync(
                c => c.Email == normalisedEmail && (existingId == null || c.Id != existingId),
                cancellationToken))
        {
            return OperationResult.Failure(
                "Another customer already uses that email address.",
                nameof(SaveCustomerRequest.Email));
        }

        if (request.CustomerType == CustomerType.Wholesale && request.PriceListId is not null
            && !await _db.PriceLists.AnyAsync(
                p => p.Id == request.PriceListId && p.IsActive, cancellationToken))
        {
            return OperationResult.Failure(
                "Choose an active price list.", nameof(SaveCustomerRequest.PriceListId));
        }

        if (request.DateOfBirth is not null)
        {
            var today = _clock.ToBusinessDate(_clock.UtcNow);

            if (request.DateOfBirth > today)
            {
                return OperationResult.Failure(
                    "A date of birth cannot be in the future.",
                    nameof(SaveCustomerRequest.DateOfBirth));
            }
        }

        return OperationResult.Success();
    }

    /// <summary>
    /// One message for every way a number can be wrong, because the person
    /// typing does not care which rule they broke - they care what to type.
    /// </summary>
    private static string PhoneError(string? typed) =>
        string.IsNullOrWhiteSpace(typed)
            ? "Enter a mobile number. It is how this customer is identified everywhere else in the system."
            : $"'{typed}' is not a Bangladeshi mobile number. Use eleven digits starting 013 to 019, "
              + "for example 01712345678. +880 and spaces are fine.";

    /// <summary>
    /// Normalised when it is a valid mobile, kept as typed when it is not.
    ///
    /// The alternate is allowed to be a landline, an office extension or a
    /// number written with a note beside it. It is a fallback for a human to
    /// ring, not an identity, so it does not have to be machine-perfect.
    /// </summary>
    private static string? NormaliseAlternate(string? value)
    {
        var trimmed = Trim(value);

        if (trimmed is null)
        {
            return null;
        }

        return BangladeshPhone.Normalise(trimmed) ?? trimmed;
    }

    private async Task<string> NextCodeAsync(CancellationToken cancellationToken)
    {
        var existing = await _db.Customers
            .IgnoreQueryFilters()
            .Where(c => c.Code.StartsWith("CUS-"))
            .Select(c => c.Code)
            .ToListAsync(cancellationToken);

        var highest = existing
            .Select(c => c.Length == 9
                         && int.TryParse(c[4..], NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                ? n
                : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"CUS-{highest + 1:D5}";
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// What quick-create produced. <see cref="WasExisting"/> is the difference
/// between "new customer" and "we already know them", and the screen says
/// something different for each.
/// </summary>
public class QuickCreateResult
{
    public long Id { get; set; }

    public bool WasExisting { get; set; }

    public string Code { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;
}
