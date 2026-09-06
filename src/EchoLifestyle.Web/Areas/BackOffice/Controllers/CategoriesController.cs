using EchoLifestyle.Application.Catalog.Categories;
using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Web.Areas.BackOffice.Models.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

/// <summary>
/// The category tree.
///
/// Deliberately not a DataTables grid: the tree is a few dozen rows, paging it
/// would hide the structure that makes it a tree, and a server-side grid cannot
/// express "children immediately after their parent". It renders as one ordered
/// table with indentation.
/// </summary>
[Authorize(Policy = Permissions.Catalog.CategoryView)]
public class CategoriesController : BackOfficeControllerBase
{
    private readonly CategoryAdminService _categories;

    public CategoriesController(CategoryAdminService categories)
    {
        _categories = categories;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        StatusFilter? status,
        string? search,
        CancellationToken cancellationToken)
    {
        // Nullable so the default can be All rather than Active. Hiding
        // inactive nodes by default makes a category vanish from the tree the
        // moment someone deactivates it, which reads as data loss rather than
        // as a filter doing its job.
        var effective = status ?? StatusFilter.All;

        ViewData["Status"] = effective;
        ViewData["Search"] = search;

        var tree = await _categories.GetTreeAsync(effective, search, cancellationToken);

        return View(tree);
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Catalog.CategoryEdit)]
    public async Task<IActionResult> Create(long? parentId, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New category";

        var model = new CategoryFormModel
        {
            ParentId = parentId,
            ParentOptions = await _categories.GetParentOptionsAsync(null, cancellationToken),
        };

        return View("Form", model);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Catalog.CategoryEdit)]
    public async Task<IActionResult> Create(CategoryFormModel model, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New category";

        if (!ModelState.IsValid)
        {
            model.ParentOptions = await _categories.GetParentOptionsAsync(null, cancellationToken);
            return View("Form", model);
        }

        var result = await _categories.CreateAsync(model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            model.ParentOptions = await _categories.GetParentOptionsAsync(null, cancellationToken);
            return View("Form", model);
        }

        Notify($"Category '{model.Name}' created.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Catalog.CategoryEdit)]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        var detail = await _categories.GetAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        ViewData["Title"] = detail.Name;

        var model = CategoryFormModel.FromDetail(detail);
        model.ParentOptions = await _categories.GetParentOptionsAsync(id, cancellationToken);

        return View("Form", model);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Catalog.CategoryEdit)]
    public async Task<IActionResult> Edit(long id, CategoryFormModel model, CancellationToken cancellationToken)
    {
        model.Id = id;
        ViewData["Title"] = model.Name;

        if (!ModelState.IsValid)
        {
            await RestoreReadOnlyAsync(model, cancellationToken);
            return View("Form", model);
        }

        var result = await _categories.UpdateAsync(id, model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            await RestoreReadOnlyAsync(model, cancellationToken);
            return View("Form", model);
        }

        Notify($"Category '{model.Name}' saved.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Catalog.CategoryEdit)]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var result = await _categories.DeleteAsync(id, cancellationToken);

        if (!result.Succeeded)
        {
            Notify(result.Error!, "danger");
            return RedirectToAction(nameof(Edit), new { id });
        }

        Notify("Category deleted.");
        return RedirectToAction(nameof(Index));
    }

    private async Task RestoreReadOnlyAsync(CategoryFormModel model, CancellationToken cancellationToken)
    {
        model.ParentOptions = await _categories.GetParentOptionsAsync(
            model.IsNew ? null : model.Id, cancellationToken);

        if (model.IsNew)
        {
            return;
        }

        var detail = await _categories.GetAsync(model.Id, cancellationToken);

        if (detail is not null)
        {
            model.Breadcrumb = detail.Breadcrumb;
            model.DirectProductCount = detail.DirectProductCount;
            model.ChildCount = detail.ChildCount;
        }
    }
}
