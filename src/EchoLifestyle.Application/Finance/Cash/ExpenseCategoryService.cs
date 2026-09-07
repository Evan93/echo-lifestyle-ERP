using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Finance.Cash;

/// <summary>
/// Maintaining the list of things money gets spent on.
///
/// Small on purpose. Categories are never deleted - an expense recorded last
/// March still points at one - so the only way to retire a category is to
/// deactivate it, which hides it from the form and leaves history intact.
/// </summary>
public class ExpenseCategoryService
{
    private readonly IApplicationDbContext _db;

    public ExpenseCategoryService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ExpenseCategoryRow>> ListAsync(
        CancellationToken cancellationToken = default) =>
        await _db.ExpenseCategories
            .AsNoTracking()
            .OrderBy(c => c.IsActive ? 0 : 1)
            .ThenBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .Select(c => new ExpenseCategoryRow
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                IsCostOfSale = c.IsCostOfSale,
                IsSystem = c.IsSystem,
                IsActive = c.IsActive,
                DisplayOrder = c.DisplayOrder,
                TimesUsed = _db.CashTransactions.Count(t => t.ExpenseCategoryId == c.Id),
            })
            .ToListAsync(cancellationToken);

    public async Task<OperationResult<long>> SaveAsync(
        SaveExpenseCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return OperationResult<long>.Failure(
                "Give the category a name.", nameof(SaveExpenseCategoryRequest.Name));
        }

        var name = request.Name.Trim();

        var clash = await _db.ExpenseCategories
            .AnyAsync(c => c.Name == name && c.Id != request.Id, cancellationToken);

        if (clash)
        {
            return OperationResult<long>.Failure(
                $"There is already a category called {name}. Two categories with one name means "
                + "every total is split between them.",
                nameof(SaveExpenseCategoryRequest.Name));
        }

        ExpenseCategory category;

        if (request.Id == 0)
        {
            category = new ExpenseCategory { DisplayOrder = request.DisplayOrder };
            _db.ExpenseCategories.Add(category);
        }
        else
        {
            var found = await _db.ExpenseCategories
                .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

            if (found is null)
            {
                return OperationResult<long>.Failure("That category no longer exists.");
            }

            if (found.IsSystem && !string.Equals(found.Name, name, StringComparison.Ordinal))
            {
                // Renaming a seeded row means the next seed run recreates it
                // under its old name, and one year of expenses ends up split
                // across two categories that mean the same thing.
                return OperationResult<long>.Failure(
                    $"{found.Name} is a built-in category and cannot be renamed. Deactivate it and "
                    + "add your own if the wording is wrong.",
                    nameof(SaveExpenseCategoryRequest.Name));
            }

            category = found;
            category.DisplayOrder = request.DisplayOrder;
        }

        category.Name = name;
        category.Description = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();
        category.IsCostOfSale = request.IsCostOfSale;
        category.IsActive = request.IsActive;

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<long>.Success(category.Id);
    }
}

public class SaveExpenseCategoryRequest
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsCostOfSale { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }
}

public class ExpenseCategoryRow
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsCostOfSale { get; set; }

    public bool IsSystem { get; set; }

    public bool IsActive { get; set; }

    public int DisplayOrder { get; set; }

    /// <summary>Entries pointing here. Why a category is deactivated, not deleted.</summary>
    public int TimesUsed { get; set; }
}
