using System.Globalization;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Application.Common.Text;
using EchoLifestyle.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Crm.Customers;

/// <summary>
/// Delivery addresses.
///
/// Kept out of the main customer form on purpose: one customer commonly has
/// three - home, office, and wherever their mother lives - and a form that
/// edits all of them at once is a form that saves the wrong one.
/// </summary>
public partial class CustomerAdminService
{
    /// <summary>
    /// Adds or updates one address, and keeps "exactly one default" true.
    ///
    /// The first address a customer gets is always the default, whatever the
    /// form said. A customer with one address and no default would mean every
    /// order screen asking a question with one possible answer.
    /// </summary>
    public async Task<OperationResult<long>> SaveAddressAsync(
        SaveAddressRequest request,
        CancellationToken cancellationToken = default)
    {
        var customer = await _db.Customers
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken);

        if (customer is null)
        {
            return OperationResult<long>.Failure("That customer no longer exists.");
        }

        var district = await _db.Districts
            .AsNoTracking()
            .Where(d => d.Id == request.DistrictId && d.IsActive)
            .Select(d => new { d.Id, d.DivisionId, d.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (district is null)
        {
            return OperationResult<long>.Failure(
                "Choose a district. Couriers price by district, so an address without one cannot ship.",
                nameof(SaveAddressRequest.DistrictId));
        }

        var area = request.AreaOrThana?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(area))
        {
            return OperationResult<long>.Failure(
                "Enter the area or thana. District alone is not enough for a rider to find anything.",
                nameof(SaveAddressRequest.AreaOrThana));
        }

        var line = request.AddressLine?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(line))
        {
            return OperationResult<long>.Failure(
                "Enter the address - house, road, flat.", nameof(SaveAddressRequest.AddressLine));
        }

        // The recipient falls back to the customer. Most deliveries go to the
        // person who ordered, and asking for their name twice is how forms get
        // abandoned.
        var recipientName = Trim(request.RecipientName) ?? customer.FullName;
        var typedPhone = Trim(request.RecipientPhone);

        var recipientPhone = typedPhone is null
            ? customer.Phone
            : BangladeshPhone.Normalise(typedPhone);

        if (recipientPhone is null)
        {
            return OperationResult<long>.Failure(
                $"'{typedPhone}' is not a Bangladeshi mobile number. The courier rings this one on "
                + "arrival, so it has to be a number that works.",
                nameof(SaveAddressRequest.RecipientPhone));
        }

        var address = request.Id is null
            ? null
            : customer.Addresses.FirstOrDefault(a => a.Id == request.Id);

        if (request.Id is not null && address is null)
        {
            return OperationResult<long>.Failure("That address no longer exists.");
        }

        var isNew = address is null;

        address ??= new CustomerAddress { CustomerId = customer.Id };

        address.Label = Trim(request.Label) ?? "Home";
        address.RecipientName = recipientName;
        address.RecipientPhone = recipientPhone;
        address.DivisionId = district.DivisionId;
        address.DistrictId = district.Id;
        address.AreaOrThana = area;
        address.AddressLine = line;
        address.Landmark = Trim(request.Landmark);
        address.PostCode = Trim(request.PostCode);
        address.DeliveryNotes = Trim(request.DeliveryNotes);

        // First one in is the default whatever the form said; after that the
        // form decides. Demoting the previous default has to happen before this
        // one is promoted, or the filtered unique index rejects the save.
        var shouldBeDefault = request.IsDefault || customer.Addresses.Count == 0;

        if (shouldBeDefault)
        {
            foreach (var other in customer.Addresses.Where(a => a.IsDefault && a != address))
            {
                other.IsDefault = false;
            }
        }

        address.IsDefault = shouldBeDefault;

        if (isNew)
        {
            customer.Addresses.Add(address);
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            AuditActions.CustomerAddressChanged,
            nameof(CustomerAddress),
            address.Id.ToString(CultureInfo.InvariantCulture),
            $"{(isNew ? "Added" : "Updated")} the '{address.Label}' address for "
            + $"{customer.Code} - {customer.FullName} ({district.Name}).",

            // District and label only. The street line is the part that is
            // sensitive, and the audit trail is read by more people than the
            // customer record is.
            new { customer.Code, address.Label, District = district.Name, address.IsDefault },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<long>.Success(address.Id);
    }

    /// <summary>
    /// Removes an address.
    ///
    /// Hard delete, deliberately: an address is not a document and nothing is
    /// posted against it. Once orders exist they will hold their own snapshot of
    /// where they were sent, so deleting the address here will never rewrite
    /// where a past parcel went.
    /// </summary>
    public async Task<OperationResult> DeleteAddressAsync(
        long customerId,
        long addressId,
        CancellationToken cancellationToken = default)
    {
        var customer = await _db.Customers
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);

        var address = customer?.Addresses.FirstOrDefault(a => a.Id == addressId);

        if (customer is null || address is null)
        {
            return OperationResult.Failure("That address no longer exists.");
        }

        var wasDefault = address.IsDefault;

        _db.CustomerAddresses.Remove(address);
        customer.Addresses.Remove(address);

        // Something has to be the default, or the next order screen has no
        // answer to a question it must ask.
        if (wasDefault)
        {
            var next = customer.Addresses.FirstOrDefault();

            if (next is not null)
            {
                next.IsDefault = true;
            }
        }

        await _audit.LogAsync(
            AuditActions.CustomerAddressChanged,
            nameof(CustomerAddress),
            addressId.ToString(CultureInfo.InvariantCulture),
            $"Removed the '{address.Label}' address for {customer.Code} - {customer.FullName}.",
            new { customer.Code, address.Label, Removed = true },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    /// <summary>
    /// Promotes one address to default and demotes the rest.
    /// </summary>
    public async Task<OperationResult> SetDefaultAddressAsync(
        long customerId,
        long addressId,
        CancellationToken cancellationToken = default)
    {
        var customer = await _db.Customers
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);

        var address = customer?.Addresses.FirstOrDefault(a => a.Id == addressId);

        if (customer is null || address is null)
        {
            return OperationResult.Failure("That address no longer exists.");
        }

        // Demote first. Two rows briefly holding IsDefault would break the
        // filtered unique index at save time, and the error would name the
        // index rather than anything a person could act on.
        foreach (var other in customer.Addresses.Where(a => a.IsDefault && a.Id != addressId))
        {
            other.IsDefault = false;
        }

        address.IsDefault = true;

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }
}
