using Finance.Api.Contracts.Categories;
using Finance.Application.Categories;
using Finance.Application.Categories.Create;
using Finance.Application.Categories.Deactivate;
using Finance.Application.Categories.GetCategories;
using Finance.Application.Categories.Rename;
using Finance.Application.Categories.Restore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finance.Api.Controllers;

[ApiController]
[Route("api/categories")]
[Authorize]
public sealed class CategoriesController(
    CreateCategoryHandler createCategoryHandler,
    GetCategoriesHandler getCategoriesHandler,
    RenameCategoryHandler renameCategoryHandler,
    DeactivateCategoryHandler deactivateCategoryHandler,
    RestoreCategoryHandler restoreCategoryHandler) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var categories = await getCategoriesHandler.HandleAsync(
            includeInactive,
            cancellationToken);
        return categories is null ? Unauthorized() : Ok(categories);
    }

    [HttpPost]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await createCategoryHandler.HandleAsync(
            new CreateCategoryCommand(request.Name, request.Type),
            cancellationToken);
        return ToActionResult(result, StatusCodes.Status201Created);
    }

    [HttpPatch("{categoryId:guid}")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Rename(
        Guid categoryId,
        RenameCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await renameCategoryHandler.HandleAsync(
            new RenameCategoryCommand(categoryId, request.Name),
            cancellationToken);
        return ToActionResult(result, StatusCodes.Status200OK);
    }

    [HttpPost("{categoryId:guid}/deactivate")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        var result = await deactivateCategoryHandler.HandleAsync(
            categoryId,
            cancellationToken);
        return ToActionResult(result, StatusCodes.Status200OK);
    }

    [HttpPost("{categoryId:guid}/restore")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Restore(
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        var result = await restoreCategoryHandler.HandleAsync(
            categoryId,
            cancellationToken);
        return ToActionResult(result, StatusCodes.Status200OK);
    }

    private IActionResult ToActionResult(
        CategoryMutationResult result,
        int successStatusCode)
    {
        return result.Status switch
        {
            CategoryMutationStatus.Success => StatusCode(successStatusCode, result.Category),
            CategoryMutationStatus.InvalidInput => BadRequest(new ValidationProblemDetails(
                result.Errors.ToDictionary(error => error.Key, error => error.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred."
            }),
            CategoryMutationStatus.NotFound => NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Category was not found."
            }),
            CategoryMutationStatus.DuplicateName => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = result.Errors["name"][0]
            }),
            CategoryMutationStatus.Unauthorized => Unauthorized(),
            _ => throw new InvalidOperationException("Unknown category mutation result.")
        };
    }
}
