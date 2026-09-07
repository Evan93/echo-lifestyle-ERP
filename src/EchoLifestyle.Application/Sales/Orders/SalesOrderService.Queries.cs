using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Application.Common.Text;
using EchoLifestyle.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Sales.Orders;

/// <summary>Reading orders back.</summary>
public partial class SalesOrderService
{
    public async Task<PagedResult<SalesOrderListItem>> ListAsync(
        string? search,
        int skip,
        int take,
        string? sortColumn,
        bool sortDescending,
        SalesOrderStatus? status,
        bool awaitingCashOnly,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        var query = _db.SalesOrders.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        if (status is not null)
        {
            query = query.Where(o => o.Status == status);
        }

        if (awaitingCashOnly)
        {
            // Shipped but not paid for. In a cash-on-delivery business this is
            // the working-capital question, and it is the one screen an owner
            // opens every morning.
            query = query.Where(o => o.Status == SalesOrderStatus.Dispatched
                                     || (o.Status == SalesOrderStatus.Delivered
                                         && o.AmountCollected < o.GrandTotal));
        }

        if (from is not null)
        {
            query = query.Where(o => o.OrderDate >= from);
        }

        if (to is not null)
        {
            query = query.Where(o => o.OrderDate <= to);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var asPhone = BangladeshPhone.Normalise(term);

            query = query.Where(o =>
                EF.Functions.Like(o.Number, $"%{term}%")
                || EF.Functions.Like(o.Customer!.FullName, $"%{term}%")
                || EF.Functions.Like(o.RecipientName, $"%{term}%")
                || EF.Functions.Like(o.RecipientPhone, $"%{term}%")
                || (asPhone != null && o.RecipientPhone == asPhone)
                || (o.ConsignmentNumber != null
                    && EF.Functions.Like(o.ConsignmentNumber, $"%{term}%"))
                || o.Lines.Any(l => EF.Functions.Like(l.Sku, $"%{term}%")
                                    || EF.Functions.Like(l.ProductName, $"%{term}%")));
        }

        var filteredCount = await query.CountAsync(cancellationToken);

        query = (sortColumn, sortDescending) switch
        {
            ("number", false) => query.OrderBy(o => o.Number),
            ("customer", false) => query.OrderBy(o => o.Customer!.FullName).ThenByDescending(o => o.Number),
            ("customer", true) => query.OrderByDescending(o => o.Customer!.FullName).ThenByDescending(o => o.Number),
            ("date", false) => query.OrderBy(o => o.OrderDate).ThenBy(o => o.Number),
            ("date", true) => query.OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.Number),
            ("total", false) => query.OrderBy(o => o.GrandTotal),
            ("total", true) => query.OrderByDescending(o => o.GrandTotal),

            // Newest first: the order somebody wants is nearly always the one
            // just taken.
            _ => query.OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.Id),
        };

        var rows = await query
            .Skip(skip)
            .Take(take)
            .Select(o => new SalesOrderListItem
            {
                Id = o.Id,
                Number = o.Number,
                CustomerId = o.CustomerId,
                CustomerName = o.Customer!.FullName,
                CustomerPhone = o.RecipientPhone,
                OrderDate = o.OrderDate,
                Status = o.Status,
                Channel = o.Channel,
                DistrictName = o.DistrictName,
                LineCount = o.Lines.Count,
                TotalQuantity = o.Lines.Sum(l => (decimal?)l.Quantity) ?? 0m,
                GrandTotal = o.GrandTotal,
                AmountCollected = o.AmountCollected,
                CourierName = o.CourierName,
                ConsignmentNumber = o.ConsignmentNumber,
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<SalesOrderListItem>(rows, totalCount, filteredCount);
    }

    public async Task<SalesOrderDetail?> GetAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var order = await _db.SalesOrders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Warehouse)
            .Include(o => o.Lines)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order is null)
        {
            return null;
        }

        // Loaded separately: only a confirmed or packed order holds any, and
        // joining a table that is usually empty onto every read is waste.
        var reservations = SalesOrder.HoldsReservations(order.Status)
            ? await _db.StockReservations
                .AsNoTracking()
                .Where(r => r.SalesOrderId == id)
                .Select(r => new
                {
                    r.SalesOrderLineId,
                    r.Quantity,
                    BatchNumber = r.StockBatch!.BatchNumber,
                    r.StockBatch.IsAutoGenerated,
                    r.StockBatch.ExpiryDate,
                })
                .ToListAsync(cancellationToken)
            : [];

        return new SalesOrderDetail
        {
            Id = order.Id,
            Number = order.Number,
            CustomerId = order.CustomerId,
            CustomerName = order.Customer?.FullName ?? string.Empty,
            CustomerCode = order.Customer?.Code ?? string.Empty,
            CustomerPhone = order.Customer?.Phone ?? string.Empty,
            CustomerIsBlocked = order.Customer?.IsBlocked ?? false,
            WarehouseId = order.WarehouseId,
            WarehouseName = order.Warehouse?.Name ?? string.Empty,
            BranchId = order.BranchId,
            OrderDate = order.OrderDate,
            Status = order.Status,
            Channel = order.Channel,
            PaymentMethod = order.PaymentMethod,
            RecipientName = order.RecipientName,
            RecipientPhone = order.RecipientPhone,
            DivisionName = order.DivisionName,
            DistrictName = order.DistrictName,
            AreaOrThana = order.AreaOrThana,
            AddressLine = order.AddressLine,
            Landmark = order.Landmark,
            PostCode = order.PostCode,
            DeliveryNotes = order.DeliveryNotes,
            IsInsideCity = order.IsInsideCity,
            SubTotal = order.SubTotal,
            DiscountAmount = order.DiscountAmount,
            DeliveryCharge = order.DeliveryCharge,
            GrandTotal = order.GrandTotal,
            AmountCollected = order.AmountCollected,
            CostOfGoods = order.CostOfGoods,
            CourierName = order.CourierName,
            ConsignmentNumber = order.ConsignmentNumber,
            DispatchedAtUtc = order.DispatchedAtUtc,
            DeliveredAtUtc = order.DeliveredAtUtc,
            CancelReason = order.CancelReason,
            ReturnReason = order.ReturnReason,
            Notes = order.Notes,
            Lines = order.Lines
                .OrderBy(l => l.Id)
                .Select(l => new SalesOrderLineDetail
                {
                    Id = l.Id,
                    ProductVariantId = l.ProductVariantId,
                    Sku = l.Sku,
                    ProductName = l.ProductName,
                    VariantName = l.VariantName,
                    Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice,
                    DiscountAmount = l.DiscountAmount,
                    LineTotal = l.LineTotal,
                    CostOfGoods = l.CostOfGoods,
                    Notes = l.Notes,
                    Reserved = reservations
                        .Where(r => r.SalesOrderLineId == l.Id)
                        .Select(r => new ReservedBatch
                        {
                            BatchNumber = r.BatchNumber,
                            IsAutoGenerated = r.IsAutoGenerated,
                            ExpiryDate = r.ExpiryDate,
                            Quantity = r.Quantity,
                        })
                        .ToList(),
                })
                .ToList(),
            History = order.StatusHistory
                .OrderBy(h => h.OccurredAtUtc)
                .ThenBy(h => h.Id)
                .Select(h => new OrderStatusEntry
                {
                    FromStatus = h.FromStatus,
                    ToStatus = h.ToStatus,
                    OccurredAtUtc = h.OccurredAtUtc,
                    Note = h.Note,
                })
                .ToList(),
        };
    }

    /// <summary>
    /// Products matching a term, priced for this customer and with what can
    /// actually be promised.
    ///
    /// Availability is on hand minus reserved, because showing on hand would
    /// mean offering stock that is already in somebody else's box.
    /// </summary>
    public async Task<IReadOnlyList<SellableItem>> SearchSellableAsync(
        string? term,
        long? customerId,
        long warehouseId,
        int take = 15,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
        {
            return [];
        }

        var search = term.Trim();

        var matches = await _db.ProductVariants
            .AsNoTracking()
            .Where(v => v.IsActive
                        && v.Product!.IsActive
                        && (EF.Functions.Like(v.Sku, $"%{search}%")
                            || (v.Barcode != null && EF.Functions.Like(v.Barcode, $"%{search}%"))
                            || EF.Functions.Like(v.Product.Name, $"%{search}%")
                            || EF.Functions.Like(v.Product.Brand!.Name, $"%{search}%")))
            .Select(v => new SellableItem
            {
                ProductVariantId = v.Id,
                Sku = v.Sku,
                ProductName = v.Product!.Name,
                VariantName = v.VariantName,
                BrandName = v.Product.Brand!.Name,

                Available = _db.StockBalances
                    .Where(b => b.ProductVariantId == v.Id && b.WarehouseId == warehouseId)
                    .Sum(b => (decimal?)(b.QuantityOnHand - b.QuantityReserved)) ?? 0m,
            })
            .OrderBy(v => v.ProductName)
            .ThenBy(v => v.VariantName)
            .Take(take)
            .ToListAsync(cancellationToken);

        if (matches.Count == 0)
        {
            return matches;
        }

        var prices = await _prices.ResolveAsync(
            matches.Select(m => m.ProductVariantId).ToList(), customerId, cancellationToken);

        foreach (var match in matches)
        {
            match.UnitPrice = prices.TryGetValue(match.ProductVariantId, out var price)
                ? price
                : null;
        }

        return matches;
    }

    /// <summary>
    /// Counts for the order list's tabs: what is waiting to be acted on.
    /// </summary>
    public async Task<OrderWorkload> GetWorkloadAsync(CancellationToken cancellationToken = default)
    {
        var counts = await _db.SalesOrders
            .AsNoTracking()
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var outstanding = await _db.SalesOrders
            .AsNoTracking()
            .Where(o => o.Status == SalesOrderStatus.Dispatched
                        || (o.Status == SalesOrderStatus.Delivered
                            && o.AmountCollected < o.GrandTotal))
            .SumAsync(o => (decimal?)(o.GrandTotal - o.AmountCollected), cancellationToken) ?? 0m;

        return new OrderWorkload
        {
            Draft = counts.FirstOrDefault(c => c.Status == SalesOrderStatus.Draft)?.Count ?? 0,
            Confirmed = counts.FirstOrDefault(c => c.Status == SalesOrderStatus.Confirmed)?.Count ?? 0,
            Packed = counts.FirstOrDefault(c => c.Status == SalesOrderStatus.Packed)?.Count ?? 0,
            Dispatched = counts.FirstOrDefault(c => c.Status == SalesOrderStatus.Dispatched)?.Count ?? 0,
            AmountOutstanding = outstanding,
        };
    }
}

/// <summary>What is on somebody's plate right now.</summary>
public class OrderWorkload
{
    public int Draft { get; set; }

    public int Confirmed { get; set; }

    public int Packed { get; set; }

    public int Dispatched { get; set; }

    /// <summary>Money with couriers and customers. The COD working-capital figure.</summary>
    public decimal AmountOutstanding { get; set; }
}
