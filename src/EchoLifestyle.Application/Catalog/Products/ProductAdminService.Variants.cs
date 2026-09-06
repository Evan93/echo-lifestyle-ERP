using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Catalog.Products;

/// <summary>
/// Options, the variant matrix and pricing.
///
/// The rule this half exists to hold: the set of variants is derived from the
/// options, never edited directly. Someone who could add a variant by hand
/// could create one whose combination does not exist, and every listing filter
/// would then disagree with the product page.
/// </summary>
public partial class ProductAdminService
{
    /// <summary>Name carried by the single variant of a product with no options.</summary>
    public const string DefaultVariantName = "Standard";

    [GeneratedRegex(@"^\s*(?<value>.+?)(?:\s+(?<hex>#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})))?\s*$")]
    private static partial Regex OptionValuePattern();

    /// <summary>
    /// Replaces the product's options and rebuilds its variants to match.
    ///
    /// Variants are matched to their new combination by the text of their
    /// option values, not by id, so reordering options or renaming the product
    /// keeps every SKU and price intact. Renaming a *value* reads as removing
    /// one and adding another, which is the honest interpretation: "Ruby Red"
    /// becoming "Crimson" is either a typo fix or a different shade, and the
    /// system cannot tell which.
    /// </summary>
    public async Task<OperationResult<VariantRebuildSummary>> SaveOptionsAsync(
        long productId,
        SaveOptionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var product = await _db.Products
            .Include(p => p.Options).ThenInclude(o => o.Values)
            .Include(p => p.Variants).ThenInclude(v => v.OptionValues)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is null)
        {
            return OperationResult<VariantRebuildSummary>.Failure("That product no longer exists.");
        }

        var parsed = ParseOptions(request);
        if (!parsed.Succeeded)
        {
            return OperationResult<VariantRebuildSummary>.Failure(parsed.Error!, parsed.Field);
        }

        var desiredOptions = parsed.Value!;

        var combinationCount = desiredOptions.Count == 0
            ? 1
            : desiredOptions.Aggregate(1, (total, option) => total * option.Values.Count);

        if (combinationCount > MaxVariantsPerProduct)
        {
            return OperationResult<VariantRebuildSummary>.Failure(
                $"That would create {combinationCount} variants. "
                + $"The limit is {MaxVariantsPerProduct} - beyond that the table stops being editable by hand. "
                + "Split the product, or drop an option.");
        }

        // EVERYTHING derived from the current option links has to be computed
        // here, before ClearAllOptionLinksAsync empties them. That includes the
        // default variant's signature: reading it afterwards returns the empty
        // string for every variant, which silently makes the adoption rule fire
        // when it should not and not fire when it should.
        var optionNameById = product.Options.ToDictionary(o => o.Id, o => o.Name);

        var valueTextById = product.Options
            .SelectMany(o => o.Values)
            .ToDictionary(v => v.Id, v => v.Value);

        string SignatureOfVariant(ProductVariant variant) =>
            string.Join(
                "|",
                variant.OptionValues
                    .Where(link => optionNameById.ContainsKey(link.ProductOptionId)
                                   && valueTextById.ContainsKey(link.ProductOptionValueId))
                    .Select(link => ValueKey(
                        optionNameById[link.ProductOptionId],
                        valueTextById[link.ProductOptionValueId]))
                    .OrderBy(key => key, StringComparer.Ordinal));

        // Built by hand rather than with ToDictionary because several variants
        // can legitimately share the empty signature - every variant retired by
        // an earlier rebuild has one - and those must not compete to represent
        // it.
        var signatures = new Dictionary<string, ProductVariant>(StringComparer.Ordinal);

        foreach (var variant in product.Variants)
        {
            var signature = SignatureOfVariant(variant);

            if (signature.Length == 0 && product.Options.Count > 0)
            {
                continue;
            }

            signatures.TryAdd(signature, variant);
        }

        var defaultVariant = product.Variants.FirstOrDefault(v => v.IsDefault)
                             ?? product.Variants.FirstOrDefault();

        var defaultSignature = defaultVariant is null ? null : SignatureOfVariant(defaultVariant);

        var pricedVariantIds = (await _db.PriceListItems
                .Where(i => i.ProductVariant!.ProductId == productId)
                .Select(i => i.ProductVariantId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();

        // Images pin a variant just as firmly as price history does: deleting
        // one would leave a photograph pointing at nothing.
        var imagedVariantIds = (await _db.ProductImages
                .Where(i => i.ProductId == productId && i.ProductVariantId != null)
                .Select(i => i.ProductVariantId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();

        // Every SKU that starts with this product's code, gathered once.
        // Generating them one query at a time would not see the variants
        // created earlier in this same rebuild, and would hand out duplicates.
        var reservedSkus = (await _db.ProductVariants
                .IgnoreQueryFilters()
                .Where(v => v.Sku.StartsWith(product.Code))
                .Select(v => v.Sku)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await ClearAllOptionLinksAsync(productId, cancellationToken);
        var valuesByKey = await ReconcileOptionsAsync(product, desiredOptions, cancellationToken);

        var combinations = BuildCombinations(desiredOptions);
        var summary = new VariantRebuildSummary();

        // Claimed by reference, not by id: a new variant has an id of 0 until it
        // is saved, so an id-based set would treat every new one as the same
        // variant. Nothing here may claim a variant twice - two combinations
        // sharing one would put two rows for the same (variant, option) into the
        // link table, which the unique index rejects.
        var claimed = new HashSet<ProductVariant>(ByReference.Instance);

        var links = new List<(ProductVariant Variant, long OptionId, long ValueId)>();
        var ordered = new List<ProductVariant>();

        // Adoption rule, in one sentence: the product's default variant is
        // reused for the first combination that has no match of its own,
        // provided the default itself no longer matches anything.
        //
        // That covers the two cases where retiring it would lose something real
        // - adding options to a quick-created product, and collapsing a
        // multi-variant product back to none - while staying out of the way
        // when values are renamed one at a time, where guessing which old
        // variant became which new one would be exactly that.
        var comboSignatures = combinations.Select(SignatureOf).ToHashSet(StringComparer.Ordinal);

        var adoptable = defaultSignature is not null && !comboSignatures.Contains(defaultSignature)
            ? defaultVariant
            : null;

        for (var index = 0; index < combinations.Count; index++)
        {
            var combination = combinations[index];
            var signature = SignatureOf(combination);

            ProductVariant variant;

            if (signatures.TryGetValue(signature, out var existing) && claimed.Add(existing))
            {
                variant = existing;
                summary.Retained++;
            }
            else if (adoptable is not null && claimed.Add(adoptable))
            {
                variant = adoptable;
                adoptable = null;
                summary.Retained++;
            }
            else
            {
                variant = new ProductVariant
                {
                    ProductId = product.Id,
                    Sku = NextVariantSku(product.Code, reservedSkus),
                    IsActive = true,
                };

                _db.ProductVariants.Add(variant);
                claimed.Add(variant);
                summary.Created++;
            }

            variant.VariantName = NameOf(combination);
            variant.DisplayOrder = index;
            variant.IsActive = true;
            variant.IsDefault = false;

            ordered.Add(variant);

            foreach (var (optionName, value) in combination)
            {
                var key = ValueKey(optionName, value.Value);
                var (optionId, valueId) = valuesByKey[key];

                links.Add((variant, optionId, valueId));
            }
        }

        // Materialised: removing a variant modifies the very collection being
        // walked. Leftovers are identified by reference too, so a brand-new
        // variant is never mistaken for an unclaimed old one.
        var leftovers = product.Variants
            .Where(v => v.Id != 0 && !claimed.Contains(v))
            .ToList();

        foreach (var leftover in leftovers)
        {
            if (pricedVariantIds.Contains(leftover.Id) || imagedVariantIds.Contains(leftover.Id))
            {
                // Kept, deactivated, and stripped of its links so the option
                // values it referenced can be deleted. VariantName still reads
                // "30ml / Ruby Red" - which is exactly why that field is
                // denormalised rather than derived on the fly.
                leftover.IsActive = false;
                leftover.IsDefault = false;
                summary.Retired++;
            }
            else
            {
                _db.ProductVariants.Remove(leftover);
                summary.Removed++;
            }
        }

        // Defaults are cleared in their own round trip: the filtered unique
        // index allows one, and EF gives no ordering guarantee between the row
        // that gives the flag up and the row that takes it.
        await _db.SaveChangesAsync(cancellationToken);

        // Links are built only now, from explicit key values rather than from a
        // navigation property. New variants have real ids at this point, and
        // assigning navigations instead would let EF discover the same link
        // twice - once by fixup and once by Add.
        foreach (var (variant, optionId, valueId) in links)
        {
            _db.ProductVariantOptionValues.Add(new ProductVariantOptionValue
            {
                ProductVariantId = variant.Id,
                ProductOptionId = optionId,
                ProductOptionValueId = valueId,
            });
        }

        var newDefault = ordered.FirstOrDefault();
        if (newDefault is not null)
        {
            newDefault.IsDefault = true;
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            AuditActions.VariantCreated,
            nameof(Product),
            product.Id.ToString(CultureInfo.InvariantCulture),
            $"Rebuilt variants for {product.Code}: {summary.Describe()}",
            new
            {
                product.Code,
                Options = desiredOptions.Select(o => new { o.Name, Values = o.Values.Select(v => v.Value) }),
                summary.Created,
                summary.Retained,
                summary.Retired,
                summary.Removed,
            },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<VariantRebuildSummary>.Success(summary);
    }

    /// <summary>
    /// Saves the editable columns of the variant table: SKU, barcode, price,
    /// MRP, weight and whether the variant is sellable.
    ///
    /// Rows are not created or removed here - that only happens through
    /// <see cref="SaveOptionsAsync"/>, so the matrix can never drift away from
    /// the options that define it.
    /// </summary>
    public async Task<OperationResult> SaveVariantsAsync(
        long productId,
        SaveVariantsRequest request,
        CancellationToken cancellationToken = default)
    {
        var variants = await _db.ProductVariants
            .Where(v => v.ProductId == productId)
            .ToListAsync(cancellationToken);

        if (variants.Count == 0)
        {
            return OperationResult.Failure("That product has no variants.");
        }

        var byId = variants.ToDictionary(v => v.Id);
        var inputs = request.Variants.Where(i => byId.ContainsKey(i.Id)).ToList();

        if (inputs.Count == 0)
        {
            return OperationResult.Failure("Nothing to save.");
        }

        // Checked across the posted set as well as against the database: two
        // rows of the same form given the same SKU would otherwise reach the
        // unique index as an unexplained constraint violation.
        var seenSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in inputs)
        {
            var sku = input.Sku?.Trim().ToUpperInvariant() ?? string.Empty;

            if (!seenSkus.Add(sku))
            {
                return OperationResult.Failure($"SKU '{sku}' is used twice in this form.");
            }

            var skuCheck = await ValidateSkuAsync(sku, input.Id, cancellationToken);
            if (!skuCheck.Succeeded)
            {
                return skuCheck;
            }

            var barcode = Trim(input.Barcode);

            if (barcode is not null)
            {
                if (!seenBarcodes.Add(barcode))
                {
                    return OperationResult.Failure($"Barcode '{barcode}' is used twice in this form.");
                }

                var barcodeCheck = await ValidateBarcodeAsync(barcode, input.Id, cancellationToken);
                if (!barcodeCheck.Succeeded)
                {
                    return barcodeCheck;
                }
            }

            if (input.Price is < 0 || input.Mrp is < 0 || input.CompareAtPrice is < 0)
            {
                return OperationResult.Failure($"Prices for '{sku}' cannot be negative.");
            }
        }

        if (inputs.All(i => !i.IsActive))
        {
            return OperationResult.Failure(
                "At least one variant has to stay active - a product with none cannot be sold or received. "
                + "Deactivate the whole product instead.");
        }

        foreach (var input in inputs)
        {
            var variant = byId[input.Id];

            variant.Sku = input.Sku.Trim().ToUpperInvariant();
            variant.Barcode = Trim(input.Barcode);
            variant.Mrp = input.Mrp;
            variant.CompareAtPrice = input.CompareAtPrice;
            variant.WeightGrams = input.WeightGrams;
            variant.IsActive = input.IsActive;
            variant.DisplayOrder = input.DisplayOrder;
        }

        await _db.SaveChangesAsync(cancellationToken);

        if (request.MayEditPrices)
        {
            foreach (var input in inputs.Where(i => i.Price is not null))
            {
                await SetPriceAsync(byId[input.Id], input.Price!.Value, cancellationToken);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    /// <summary>
    /// Records a selling price on the default list.
    ///
    /// Prices are never updated in place. The open row is closed and a new one
    /// opened, so "what did this sell for in March" stays answerable - which is
    /// the entire reason price is a table rather than a column.
    /// </summary>
    private async Task SetPriceAsync(
        ProductVariant variant,
        decimal price,
        CancellationToken cancellationToken)
    {
        var priceListId = await DefaultPriceListIdAsync(cancellationToken);
        var now = _clock.UtcNow;

        var current = await _db.PriceListItems
            .FirstOrDefaultAsync(
                i => i.PriceListId == priceListId
                     && i.ProductVariantId == variant.Id
                     && i.EffectiveToUtc == null,
                cancellationToken);

        if (current is not null && current.UnitPrice == price)
        {
            return;
        }

        if (current is not null)
        {
            current.EffectiveToUtc = now;

            // Saved before the replacement is inserted. The filtered unique
            // index allows one open row per variant, and EF does not promise to
            // send the update before the insert.
            await _db.SaveChangesAsync(cancellationToken);
        }

        _db.PriceListItems.Add(new PriceListItem
        {
            PriceListId = priceListId,
            ProductVariantId = variant.Id,
            UnitPrice = price,
            EffectiveFromUtc = now,
        });

        await _audit.LogAsync(
            AuditActions.PriceChanged,
            nameof(ProductVariant),
            variant.Id.ToString(CultureInfo.InvariantCulture),
            current is null
                ? $"Set price for {variant.Sku} to {price:N2}."
                : $"Changed price for {variant.Sku} from {current.UnitPrice:N2} to {price:N2}.",
            new { variant.Sku, From = current?.UnitPrice, To = price },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Removes every option link for the product in one go.
    ///
    /// Rebuilding the links wholesale rather than diffing them keeps the
    /// (variant, option) unique index satisfied at all times, and the row count
    /// involved is at most a few hundred.
    /// </summary>
    private async Task ClearAllOptionLinksAsync(long productId, CancellationToken cancellationToken)
    {
        var links = await _db.ProductVariantOptionValues
            .Where(l => l.ProductVariant!.ProductId == productId)
            .ToListAsync(cancellationToken);

        foreach (var link in links)
        {
            _db.ProductVariantOptionValues.Remove(link);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Brings the stored options and values in line with what was posted, and
    /// returns a lookup from (option name, value text) to the pair of ids the
    /// variant links need.
    /// </summary>
    private async Task<Dictionary<string, (long OptionId, long ValueId)>> ReconcileOptionsAsync(
        Product product,
        IReadOnlyList<ParsedOption> desired,
        CancellationToken cancellationToken)
    {
        var existingOptions = product.Options.ToList();

        foreach (var option in existingOptions.Where(o =>
                     !desired.Any(d => string.Equals(d.Name, o.Name, StringComparison.OrdinalIgnoreCase))))
        {
            foreach (var value in option.Values.ToList())
            {
                _db.ProductOptionValues.Remove(value);
            }

            _db.ProductOptions.Remove(option);
        }

        // Removals go in their own round trip. Renaming "Size" to "Volume" and
        // introducing a new "Size" in the same save would otherwise put a delete
        // and an insert for the same (product, name) in one batch, and EF makes
        // no promise about which reaches the unique index first.
        await _db.SaveChangesAsync(cancellationToken);

        var result = new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
        var live = new List<(ProductOption Option, ParsedOption Desired)>();

        for (var index = 0; index < desired.Count; index++)
        {
            var wanted = desired[index];

            var option = existingOptions.FirstOrDefault(o =>
                string.Equals(o.Name, wanted.Name, StringComparison.OrdinalIgnoreCase));

            if (option is null)
            {
                option = new ProductOption { ProductId = product.Id, Name = wanted.Name };
                _db.ProductOptions.Add(option);
            }
            else
            {
                option.Name = wanted.Name;
            }

            option.DisplayOrder = index;
            live.Add((option, wanted));
        }

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var (option, wanted) in live)
        {
            var existingValues = option.Values.ToList();

            foreach (var value in existingValues.Where(v =>
                         !wanted.Values.Any(w => string.Equals(w.Value, v.Value, StringComparison.OrdinalIgnoreCase))))
            {
                _db.ProductOptionValues.Remove(value);
            }

            // Same reasoning as above, one level down: (option, value) is unique.
            await _db.SaveChangesAsync(cancellationToken);

            for (var index = 0; index < wanted.Values.Count; index++)
            {
                var wantedValue = wanted.Values[index];

                var value = existingValues.FirstOrDefault(v =>
                    string.Equals(v.Value, wantedValue.Value, StringComparison.OrdinalIgnoreCase));

                if (value is null)
                {
                    value = new ProductOptionValue { ProductOption = option, Value = wantedValue.Value };
                    _db.ProductOptionValues.Add(value);
                }
                else
                {
                    value.Value = wantedValue.Value;
                }

                value.SwatchHex = wantedValue.SwatchHex;
                value.DisplayOrder = index;

                wantedValue.Entity = value;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var (option, wanted) in live)
        {
            foreach (var value in wanted.Values)
            {
                result[ValueKey(option.Name, value.Value)] = (option.Id, value.Entity!.Id);
            }
        }

        return result;
    }

    /// <summary>
    /// The cartesian product of the options, in option order then value order.
    /// An empty option set yields one empty combination - the single default
    /// variant of a product that does not vary.
    /// </summary>
    private static List<List<(string OptionName, ParsedValue Value)>> BuildCombinations(
        IReadOnlyList<ParsedOption> options)
    {
        var combinations = new List<List<(string, ParsedValue)>>
        {
            new(),
        };

        foreach (var option in options)
        {
            var next = new List<List<(string, ParsedValue)>>(combinations.Count * option.Values.Count);

            foreach (var combination in combinations)
            {
                foreach (var value in option.Values)
                {
                    var extended = new List<(string, ParsedValue)>(combination) { (option.Name, value) };
                    next.Add(extended);
                }
            }

            combinations = next;
        }

        return combinations;
    }

    private static string SignatureOf(IEnumerable<(string OptionName, ParsedValue Value)> combination) =>
        string.Join(
            "|",
            combination
                .Select(part => ValueKey(part.OptionName, part.Value.Value))
                .OrderBy(key => key, StringComparer.Ordinal));

    private static string NameOf(IReadOnlyList<(string OptionName, ParsedValue Value)> combination) =>
        combination.Count == 0
            ? DefaultVariantName
            : string.Join(" / ", combination.Select(part => part.Value.Value));

    private static string ValueKey(string optionName, string value) =>
        $"{optionName.Trim().ToLowerInvariant()}={value.Trim().ToLowerInvariant()}";

    /// <summary>
    /// Next free SKU in the PRODUCTCODE-01 series.
    ///
    /// Pure, and it reserves what it hands out: the caller holds the set across
    /// a whole rebuild, so variants created in the same pass cannot be given
    /// the same SKU as each other.
    /// </summary>
    private static string NextVariantSku(string productCode, HashSet<string> reserved)
    {
        for (var suffix = 1; suffix < 1000; suffix++)
        {
            var candidate = $"{productCode}-{suffix:D2}";

            if (reserved.Add(candidate))
            {
                return candidate;
            }
        }

        // Unreachable in practice: combinations are capped well below this.
        // Failing loudly beats returning a duplicate.
        throw new InvalidOperationException($"Could not generate a free SKU based on '{productCode}'.");
    }

    private async Task<OperationResult> ValidateSkuAsync(
        string sku,
        long? excludingVariantId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sku) || !SkuPattern().IsMatch(sku))
        {
            return OperationResult.Failure(
                $"'{sku}' is not a usable SKU. Use 2 to 40 letters, numbers, dashes, dots or underscores.");
        }

        // IgnoreQueryFilters on purpose: the SKU index is not filtered on
        // deletion, because an SKU that has ever held stock must never be
        // reissued to a different item. Checking only live rows would report
        // the SKU as free and then fail at the database.
        var clash = await _db.ProductVariants
            .IgnoreQueryFilters()
            .Where(v => v.Sku == sku && (excludingVariantId == null || v.Id != excludingVariantId))
            .Select(v => new { v.Product!.Name, v.Product.IsDeleted })
            .FirstOrDefaultAsync(cancellationToken);

        if (clash is null)
        {
            return OperationResult.Success();
        }

        return OperationResult.Failure(
            clash.IsDeleted
                ? $"SKU '{sku}' belonged to '{clash.Name}', which was deleted. "
                  + "Deleted SKUs stay reserved so stock history cannot be misread - choose another."
                : $"SKU '{sku}' is already used by '{clash.Name}'.");
    }

    private async Task<OperationResult> ValidateBarcodeAsync(
        string barcode,
        long? excludingVariantId,
        CancellationToken cancellationToken)
    {
        var clash = await _db.ProductVariants
            .IgnoreQueryFilters()
            .Where(v => v.Barcode == barcode && (excludingVariantId == null || v.Id != excludingVariantId))
            .Select(v => v.Product!.Name)
            .FirstOrDefaultAsync(cancellationToken);

        return clash is null
            ? OperationResult.Success()
            : OperationResult.Failure($"Barcode '{barcode}' is already used by '{clash}'.");
    }

    private static OperationResult<List<ParsedOption>> ParseOptions(SaveOptionsRequest request)
    {
        var options = new List<ParsedOption>();

        foreach (var input in request.Options)
        {
            var name = input.Name?.Trim();
            var rawValues = input.Values?.Trim();

            // A wholly blank row is how the form says "this axis is unused".
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(rawValues))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return OperationResult<List<ParsedOption>>.Failure(
                    "An option has values but no name. Name it, or clear its values.");
            }

            if (name.Length > 50)
            {
                return OperationResult<List<ParsedOption>>.Failure($"Option name '{name}' is too long.");
            }

            if (options.Any(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return OperationResult<List<ParsedOption>>.Failure(
                    $"'{name}' is listed twice. Each option needs its own name.");
            }

            if (string.IsNullOrWhiteSpace(rawValues))
            {
                return OperationResult<List<ParsedOption>>.Failure(
                    $"Give '{name}' at least one value, for example \"30ml, 50ml\".");
            }

            var values = new List<ParsedValue>();

            foreach (var token in rawValues.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var match = OptionValuePattern().Match(token);

                if (!match.Success)
                {
                    continue;
                }

                var text = match.Groups["value"].Value.Trim();
                var hex = match.Groups["hex"].Success ? match.Groups["hex"].Value.ToUpperInvariant() : null;

                if (text.Length == 0)
                {
                    continue;
                }

                if (text.Length > 100)
                {
                    return OperationResult<List<ParsedOption>>.Failure(
                        $"The value '{text[..30]}...' under '{name}' is too long.");
                }

                if (values.Any(v => string.Equals(v.Value, text, StringComparison.OrdinalIgnoreCase)))
                {
                    return OperationResult<List<ParsedOption>>.Failure(
                        $"'{text}' is listed twice under '{name}'.");
                }

                values.Add(new ParsedValue { Value = text, SwatchHex = hex });
            }

            if (values.Count == 0)
            {
                return OperationResult<List<ParsedOption>>.Failure(
                    $"Give '{name}' at least one value, for example \"30ml, 50ml\".");
            }

            options.Add(new ParsedOption { Name = name, Values = values });
        }

        if (options.Count > Product.MaxOptions)
        {
            return OperationResult<List<ParsedOption>>.Failure(
                $"A product may vary along at most {Product.MaxOptions} options.");
        }

        return OperationResult<List<ParsedOption>>.Success(options);
    }

    /// <summary>
    /// Beyond this the variant table stops being something a person can fill in
    /// and starts needing a bulk import, which does not exist yet. Refusing is
    /// kinder than rendering 400 rows.
    /// </summary>
    private const int MaxVariantsPerProduct = 100;

    /// <summary>
    /// Identity, not equality.
    ///
    /// Variants are compared by reference while a rebuild is in flight because
    /// a new one has an id of 0 until it is saved - so an id-based set would
    /// treat every unsaved variant as the same variant. Spelled out rather than
    /// relying on the default comparer so that giving BaseEntity value equality
    /// one day cannot quietly break the rebuild.
    /// </summary>
    private sealed class ByReference : IEqualityComparer<ProductVariant>
    {
        public static readonly ByReference Instance = new();

        public bool Equals(ProductVariant? x, ProductVariant? y) => ReferenceEquals(x, y);

        public int GetHashCode(ProductVariant obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private sealed class ParsedOption
    {
        public string Name { get; init; } = string.Empty;

        public List<ParsedValue> Values { get; init; } = [];
    }

    private sealed class ParsedValue
    {
        public string Value { get; init; } = string.Empty;

        public string? SwatchHex { get; init; }

        /// <summary>Set during reconciliation so links can be built afterwards.</summary>
        public ProductOptionValue? Entity { get; set; }
    }
}
