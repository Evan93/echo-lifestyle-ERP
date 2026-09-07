// Quick Purchase - fast line entry for receiving stock.
//
// The fast path is: type or scan an SKU, press Enter, correct the quantity.
// Everything here is convenience. The server revalidates every field and
// recomputes every total, so nothing below can change what is actually posted.

(function () {
    "use strict";

    var lineIndex = 0;
    var chargeIndex = 0;

    function money(value) {
        return "৳ " + Number(value || 0).toLocaleString("en-BD", {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        });
    }

    function initQuickPurchase(options) {
        var $search = jQuery("#lineSearch");
        var $results = jQuery("#lineSearchResults");
        var $lines = jQuery("#lineRows");
        var $empty = jQuery("#noLines");
        var $supplier = jQuery("#SupplierId");

        lineIndex = jQuery("#lineRows tr[data-line]").length;
        chargeIndex = jQuery("#chargeRows tr[data-charge]").length;

        // -------------------------------------------------------------------
        // Import fields follow the supplier
        // -------------------------------------------------------------------
        function syncSupplier() {
            var selected = options.suppliers[String($supplier.val())];
            var isImporter = !!(selected && selected.isImporter);

            jQuery("#importPanel").toggle(isImporter);
            jQuery("#currencyLabel").text(selected ? selected.currencyCode : "BDT");

            // Charges and a rate only mean something on an import, and the
            // server drops both on a local purchase. Hiding them stops the form
            // implying otherwise.
            jQuery(".js-cost-currency").text(isImporter && selected ? selected.currencyCode : "BDT");

            recalculate();
        }

        $supplier.on("change", syncSupplier);

        // -------------------------------------------------------------------
        // Searching for a product
        // -------------------------------------------------------------------
        var searchTimer = null;

        function closeResults() {
            $results.empty().hide();
        }

        function renderResults(matches) {
            $results.empty();

            if (matches.length === 0) {
                $results.append(
                    jQuery("<div>").addClass("list-group-item text-muted small")
                        .text("Nothing matches that."));
                $results.show();
                return;
            }

            matches.forEach(function (match, position) {
                var $item = jQuery("<button>")
                    .attr("type", "button")
                    .addClass("list-group-item list-group-item-action py-2")
                    .toggleClass("active", position === 0)
                    .data("match", match);

                $item.append(jQuery("<div>").addClass("fw-semibold").text(match.display));

                var meta = match.sku + " · " + match.brandName;

                if (match.lastUnitCost !== null && typeof match.lastUnitCost !== "undefined") {
                    meta += " · last cost " + money(match.lastUnitCost);
                }

                $item.append(jQuery("<div>").addClass("small text-muted").text(meta));
                $results.append($item);
            });

            $results.show();
        }

        $search.on("input", function () {
            var term = jQuery(this).val();

            window.clearTimeout(searchTimer);

            if (!term || term.length < 2) {
                closeResults();
                return;
            }

            searchTimer = window.setTimeout(function () {
                jQuery.getJSON(options.lookupUrl, { term: term })
                    .done(renderResults)
                    .fail(closeResults);
            }, 200);
        });

        // Enter takes the highlighted row, arrows move between them. A barcode
        // scanner types the code and sends Enter, so this is the scanner path
        // as much as the keyboard one.
        $search.on("keydown", function (event) {
            var $items = $results.find(".list-group-item-action");

            if ($items.length === 0) {
                return;
            }

            var current = $items.index($items.filter(".active"));

            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                event.preventDefault();

                var next = event.key === "ArrowDown" ? current + 1 : current - 1;
                next = Math.max(0, Math.min($items.length - 1, next));

                $items.removeClass("active").eq(next).addClass("active");
            } else if (event.key === "Enter") {
                event.preventDefault();
                addLine($items.filter(".active").data("match"));
            } else if (event.key === "Escape") {
                closeResults();
            }
        });

        $results.on("click", ".list-group-item-action", function () {
            addLine(jQuery(this).data("match"));
        });

        jQuery(document).on("click", function (event) {
            if (!jQuery(event.target).closest("#lineSearchWrapper").length) {
                closeResults();
            }
        });

        // -------------------------------------------------------------------
        // Lines
        // -------------------------------------------------------------------
        function addLine(match) {
            if (!match) {
                return;
            }

            var existing = $lines.find('tr[data-variant="' + match.id + '"]');

            if (existing.length > 0) {
                // Scanning the same item twice means two of it, not a second
                // line to reconcile later.
                var $quantity = existing.find(".js-quantity");
                $quantity.val(Number($quantity.val() || 0) + 1).trigger("change");

                $search.val("").focus();
                closeResults();
                return;
            }

            var i = lineIndex++;
            var name = function (field) { return "Lines[" + i + "]." + field; };

            var $row = jQuery("<tr>")
                .attr("data-line", i)
                .attr("data-variant", match.id);

            var hidden = function (field, value) {
                return jQuery("<input>").attr({ type: "hidden", name: name(field) }).val(value);
            };

            var $description = jQuery("<td>")
                .append(jQuery("<div>").addClass("fw-semibold").text(match.display))
                .append(jQuery("<div>").addClass("small text-muted").text(match.sku))
                .append(hidden("ProductVariantId", match.id))
                .append(hidden("Sku", match.sku))
                .append(hidden("Description", match.display))
                .append(hidden("IsBatchTracked", match.isBatchTracked))
                .append(hidden("IsExpiryTracked", match.isExpiryTracked));

            $row.append($description);

            $row.append(jQuery("<td>").append(
                jQuery("<input>").attr({
                    type: "number", name: name("Quantity"), step: "0.01", min: "0.01"
                }).addClass("form-control form-control-sm text-end js-quantity").val(1)));

            $row.append(jQuery("<td>").append(
                jQuery("<input>").attr({
                    type: "number", name: name("UnitCost"), step: "0.0001", min: "0"
                }).addClass("form-control form-control-sm text-end js-cost")
                    .val(match.lastUnitCost !== null && typeof match.lastUnitCost !== "undefined"
                        ? match.lastUnitCost
                        : "")));

            $row.append(jQuery("<td>").append(
                jQuery("<input>").attr({
                    type: "number", name: name("DiscountAmount"), step: "0.01", min: "0"
                }).addClass("form-control form-control-sm text-end js-discount").val(0)));

            // Batch and expiry appear only where the product asks for them.
            // Showing them everywhere is what makes people stop reading forms.
            var $batch = jQuery("<td>");

            if (match.isBatchTracked) {
                $batch.append(jQuery("<input>").attr({
                    type: "text", name: name("BatchNumber"), placeholder: "Batch no.", maxlength: 60
                }).addClass("form-control form-control-sm mb-1"));
            }

            if (match.isExpiryTracked) {
                $batch.append(jQuery("<input>").attr({
                    type: "date", name: name("ExpiryDate"), title: "Expiry date"
                }).addClass("form-control form-control-sm"));
            }

            if (!match.isBatchTracked && !match.isExpiryTracked) {
                $batch.append(jQuery("<span>").addClass("text-muted small").text("—"));
            }

            $row.append($batch);

            $row.append(jQuery("<td>").addClass("text-end align-middle js-line-total").text(money(0)));

            $row.append(jQuery("<td>").addClass("text-end").append(
                jQuery("<button>").attr({ type: "button", title: "Remove" })
                    .addClass("btn btn-sm btn-outline-danger js-remove").text("×")));

            $lines.append($row);
            $empty.hide();

            $search.val("").focus();
            closeResults();
            recalculate();
        }

        $lines.on("click", ".js-remove", function () {
            jQuery(this).closest("tr").remove();

            if ($lines.find("tr[data-line]").length === 0) {
                $empty.show();
            }

            recalculate();
        });

        $lines.on("input change", "input", recalculate);
        jQuery("#chargeRows").on("input change", "input, select", recalculate);

        // -------------------------------------------------------------------
        // Charges
        // -------------------------------------------------------------------
        jQuery("#addCharge").on("click", function () {
            var i = chargeIndex++;
            var name = function (field) { return "Charges[" + i + "]." + field; };

            var $row = jQuery("<tr>").attr("data-charge", i);

            var $type = jQuery("<select>")
                .attr("name", name("ChargeType"))
                .addClass("form-select form-select-sm");

            options.chargeTypes.forEach(function (type) {
                $type.append(jQuery("<option>").val(type.value).text(type.text));
            });

            $row.append(jQuery("<td>").append($type));

            $row.append(jQuery("<td>").append(
                jQuery("<input>").attr({
                    type: "text", name: name("Description"), placeholder: "Optional", maxlength: 200
                }).addClass("form-control form-control-sm")));

            $row.append(jQuery("<td>").append(
                jQuery("<input>").attr({
                    type: "number", name: name("Amount"), step: "0.01", min: "0"
                }).addClass("form-control form-control-sm text-end js-charge-amount").val(0)));

            var $method = jQuery("<select>")
                .attr("name", name("ApportionMethod"))
                .addClass("form-select form-select-sm");

            options.apportionMethods.forEach(function (method) {
                $method.append(jQuery("<option>").val(method.value).text(method.text));
            });

            $row.append(jQuery("<td>").append($method));

            $row.append(jQuery("<td>").addClass("text-end").append(
                jQuery("<button>").attr({ type: "button", title: "Remove" })
                    .addClass("btn btn-sm btn-outline-danger js-remove-charge").text("×")));

            jQuery("#chargeRows").append($row);
            recalculate();
        });

        jQuery("#chargeRows").on("click", ".js-remove-charge", function () {
            jQuery(this).closest("tr").remove();
            recalculate();
        });

        // -------------------------------------------------------------------
        // Totals - a preview only. The server recalculates every one of these.
        // -------------------------------------------------------------------
        function recalculate() {
            var selected = options.suppliers[String($supplier.val())];
            var isImporter = !!(selected && selected.isImporter);
            var rate = isImporter ? Number(jQuery("#ExchangeRate").val() || 1) : 1;

            if (!(rate > 0)) {
                rate = 1;
            }

            var subTotal = 0;

            $lines.find("tr[data-line]").each(function () {
                var $row = jQuery(this);
                var quantity = Number($row.find(".js-quantity").val() || 0);
                var cost = Number($row.find(".js-cost").val() || 0);
                var discount = Number($row.find(".js-discount").val() || 0);

                var lineTotal = Math.max(0, (quantity * cost) - discount) * rate;

                $row.find(".js-line-total").text(money(lineTotal));
                subTotal += lineTotal;
            });

            var chargeTotal = 0;

            if (isImporter) {
                jQuery("#chargeRows .js-charge-amount").each(function () {
                    chargeTotal += Number(jQuery(this).val() || 0);
                });
            }

            jQuery("#subTotal").text(money(subTotal));
            jQuery("#chargeTotal").text(money(chargeTotal));
            jQuery("#grandTotal").text(money(subTotal + chargeTotal));

            jQuery("#lineCount").text($lines.find("tr[data-line]").length);
        }

        jQuery("#ExchangeRate").on("input change", recalculate);

        syncSupplier();
        recalculate();
        $search.focus();
    }

    window.echo = window.echo || {};
    window.echo.quickPurchase = initQuickPurchase;
})();
