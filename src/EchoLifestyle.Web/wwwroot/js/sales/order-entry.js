// Order entry - taking an order the way one actually arrives.
//
// Two pickers: a customer, then products. The product search shows what can be
// *promised*, not what is on hand - a jar already in somebody else's box is not
// available, and offering it is how a customer gets told twice.
//
// Everything here is convenience. The server revalidates every field, reprices
// every line and recomputes every total, so nothing below can change what is
// actually saved.

(function () {
    "use strict";

    var lineIndex = 0;

    function money(value) {
        return "৳ " + Number(value || 0).toLocaleString("en-BD", {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        });
    }

    function initOrderEntry(options) {
        var $customerSearch = jQuery("#customerSearch");
        var $customerResults = jQuery("#customerSearchResults");
        var $customerId = jQuery("#CustomerId");
        var $addresses = jQuery("#CustomerAddressId");

        var $search = jQuery("#lineSearch");
        var $results = jQuery("#lineSearchResults");
        var $lines = jQuery("#lineRows");
        var $empty = jQuery("#noLines");

        lineIndex = jQuery("#lineRows tr[data-line]").length;

        // -------------------------------------------------------------------
        // Customer
        // -------------------------------------------------------------------
        var customerTimer = null;

        function closeCustomers() {
            $customerResults.empty().hide();
        }

        function loadAddresses(customerId, selectedId) {
            $addresses.empty();

            if (!customerId) {
                $addresses.append(jQuery("<option>").val("").text("Choose a customer first"));
                return;
            }

            jQuery.getJSON(options.addressesUrl, { customerId: customerId }).done(function (list) {
                $addresses.empty();

                if (!list || list.length === 0) {
                    // Nothing can ship without a district for the courier to
                    // price, so this is a hard stop rather than a warning.
                    $addresses.append(jQuery("<option>").val("")
                        .text("No address on file - add one on their record"));
                    $addresses.addClass("is-invalid");
                    return;
                }

                $addresses.removeClass("is-invalid");

                list.forEach(function (address) {
                    var label = address.label + " — " + address.oneLine +
                        (address.isDefault ? " (default)" : "");

                    $addresses.append(jQuery("<option>")
                        .val(address.id)
                        .text(label)
                        .prop("selected", selectedId
                            ? String(address.id) === String(selectedId)
                            : address.isDefault));
                });
            });
        }

        function chooseCustomer(match) {
            if (!match) {
                return;
            }

            $customerId.val(match.id);
            $customerSearch.val(match.fullName + " · " + match.phone);
            jQuery("#CustomerName").val(match.fullName);

            jQuery("#blockedWarning").toggle(!!match.isBlocked)
                .find(".js-reason").text(match.blockReason || "");

            closeCustomers();
            loadAddresses(match.id, null);

            // Prices depend on who is buying, so anything already on the order
            // is now priced for the wrong person.
            if ($lines.find("tr[data-line]").length > 0) {
                jQuery("#repriceNotice").show();
            }

            $search.focus();
        }

        $customerSearch.on("input", function () {
            var term = jQuery(this).val();

            window.clearTimeout(customerTimer);

            // Typing over a chosen customer clears the choice - otherwise the
            // box says one name and the hidden field holds another.
            $customerId.val("");

            if (!term || term.length < 2) {
                closeCustomers();
                return;
            }

            customerTimer = window.setTimeout(function () {
                jQuery.getJSON(options.customerLookupUrl, { term: term })
                    .done(function (matches) {
                        $customerResults.empty();

                        if (matches.length === 0) {
                            $customerResults.append(
                                jQuery("<div>").addClass("list-group-item text-muted small")
                                    .text("No customer matches that. Add them first."));
                            $customerResults.show();
                            return;
                        }

                        matches.forEach(function (match, position) {
                            var $item = jQuery("<button>")
                                .attr("type", "button")
                                .addClass("list-group-item list-group-item-action py-2")
                                .toggleClass("active", position === 0)
                                .data("match", match);

                            var $name = jQuery("<div>").addClass("fw-semibold").text(match.fullName);

                            if (match.isBlocked) {
                                $name.append(jQuery("<span>")
                                    .addClass("badge text-bg-danger ms-2").text("Blocked"));
                            }

                            $item.append($name);
                            $item.append(jQuery("<div>").addClass("small text-muted")
                                .text(match.phone + (match.defaultAddress
                                    ? " · " + match.defaultAddress
                                    : "")));

                            $customerResults.append($item);
                        });

                        $customerResults.show();
                    })
                    .fail(closeCustomers);
            }, 200);
        });

        $customerSearch.on("keydown", function (event) {
            var $items = $customerResults.find(".list-group-item-action");

            if ($items.length === 0) {
                return;
            }

            var current = $items.index($items.filter(".active"));

            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                event.preventDefault();

                var next = event.key === "ArrowDown" ? current + 1 : current - 1;
                $items.removeClass("active")
                    .eq(Math.max(0, Math.min($items.length - 1, next))).addClass("active");
            } else if (event.key === "Enter") {
                event.preventDefault();
                chooseCustomer($items.filter(".active").data("match"));
            } else if (event.key === "Escape") {
                closeCustomers();
            }
        });

        $customerResults.on("click", ".list-group-item-action", function () {
            chooseCustomer(jQuery(this).data("match"));
        });

        // -------------------------------------------------------------------
        // Products
        // -------------------------------------------------------------------
        var searchTimer = null;

        function closeResults() {
            $results.empty().hide();
        }

        $search.on("input", function () {
            var term = jQuery(this).val();

            window.clearTimeout(searchTimer);

            if (!term || term.length < 2) {
                closeResults();
                return;
            }

            searchTimer = window.setTimeout(function () {
                jQuery.getJSON(options.lookupUrl, {
                    term: term,
                    customerId: $customerId.val() || null,
                    warehouseId: jQuery("#WarehouseId").val()
                })
                    .done(function (matches) {
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

                            $item.append(jQuery("<div>").addClass("fw-semibold")
                                .text(match.display));

                            var $meta = jQuery("<div>").addClass("small text-muted")
                                .text(match.sku + " · " + match.brandName + " · " +
                                    (match.unitPrice !== null ? money(match.unitPrice) : "no price"));

                            // Availability is on hand minus reserved. Zero here
                            // does not mean the shelf is empty - it means every
                            // unit is already promised to somebody.
                            var $stock = jQuery("<span>")
                                .addClass(match.available > 0 ? "text-success" : "text-danger")
                                .text(" · " + Number(match.available).toLocaleString("en-BD") +
                                    " available");

                            $meta.append($stock);
                            $item.append($meta);

                            $results.append($item);
                        });

                        $results.show();
                    })
                    .fail(closeResults);
            }, 200);
        });

        $search.on("keydown", function (event) {
            var $items = $results.find(".list-group-item-action");

            if ($items.length === 0) {
                return;
            }

            var current = $items.index($items.filter(".active"));

            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                event.preventDefault();

                var next = event.key === "ArrowDown" ? current + 1 : current - 1;
                $items.removeClass("active")
                    .eq(Math.max(0, Math.min($items.length - 1, next))).addClass("active");
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

            if (!jQuery(event.target).closest("#customerSearchWrapper").length) {
                closeCustomers();
            }
        });

        function addLine(match) {
            if (!match) {
                return;
            }

            var existing = $lines.find('tr[data-variant="' + match.productVariantId + '"]');

            if (existing.length > 0) {
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
                .attr("data-variant", match.productVariantId)
                .attr("data-available", match.available);

            var hidden = function (field, value) {
                return jQuery("<input>").attr({ type: "hidden", name: name(field) }).val(value);
            };

            $row.append(jQuery("<td>")
                .append(jQuery("<div>").addClass("fw-semibold").text(match.display))
                .append(jQuery("<div>").addClass("small text-muted js-stock")
                    .text(match.sku + " · " +
                        Number(match.available).toLocaleString("en-BD") + " available"))
                .append(hidden("ProductVariantId", match.productVariantId))
                .append(hidden("Sku", match.sku))
                .append(hidden("Description", match.display)));

            $row.append(jQuery("<td>").append(
                jQuery("<input>").attr({
                    type: "number", name: name("Quantity"), step: "0.01", min: "0.01"
                }).addClass("form-control form-control-sm text-end js-quantity").val(1)));

            $row.append(jQuery("<td>").append(
                jQuery("<input>").attr({
                    type: "number", name: name("UnitPrice"), step: "0.01", min: "0"
                }).addClass("form-control form-control-sm text-end js-price")
                    .val(match.unitPrice !== null ? match.unitPrice : "")));

            $row.append(jQuery("<td>").append(
                jQuery("<input>").attr({
                    type: "number", name: name("DiscountAmount"), step: "0.01", min: "0"
                }).addClass("form-control form-control-sm text-end js-discount").val(0)));

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
        jQuery("#DiscountAmount, #DeliveryCharge").on("input change", recalculate);

        // -------------------------------------------------------------------
        // Totals - a preview. The server recomputes every one of these.
        // -------------------------------------------------------------------
        function recalculate() {
            var subTotal = 0;
            var overcommitted = false;

            $lines.find("tr[data-line]").each(function () {
                var $row = jQuery(this);
                var quantity = Number($row.find(".js-quantity").val() || 0);
                var price = Number($row.find(".js-price").val() || 0);
                var discount = Number($row.find(".js-discount").val() || 0);
                var available = Number($row.attr("data-available") || 0);

                var lineTotal = Math.max(0, (quantity * price) - discount);

                $row.find(".js-line-total").text(money(lineTotal));
                subTotal += lineTotal;

                // Flagged, not blocked. A delivery arriving tomorrow can fill
                // it, and the draft is worth writing down either way - the
                // server refuses only at confirmation, when the promise is made.
                var short = quantity > available;
                $row.toggleClass("table-warning", short);
                overcommitted = overcommitted || short;
            });

            var discountAmount = Number(jQuery("#DiscountAmount").val() || 0);
            var delivery = Number(jQuery("#DeliveryCharge").val() || 0);

            jQuery("#subTotal").text(money(subTotal));
            jQuery("#discountTotal").text(money(discountAmount));
            jQuery("#deliveryTotal").text(money(delivery));
            jQuery("#grandTotal").text(money(Math.max(0, subTotal - discountAmount) + delivery));

            jQuery("#lineCount").text($lines.find("tr[data-line]").length);
            jQuery("#stockWarning").toggle(overcommitted);
        }

        // Changing warehouse changes what is available, so the numbers on
        // screen are about the wrong place until the lines are re-checked.
        jQuery("#WarehouseId").on("change", function () {
            jQuery("#repriceNotice").show();
        });

        loadAddresses($customerId.val(), options.selectedAddressId);
        recalculate();

        if (!$customerId.val()) {
            $customerSearch.focus();
        } else {
            $search.focus();
        }
    }

    window.echo = window.echo || {};
    window.echo.orderEntry = initOrderEntry;
})();
