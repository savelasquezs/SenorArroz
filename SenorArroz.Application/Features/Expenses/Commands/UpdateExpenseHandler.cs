// SenorArroz.Application/Features/Expenses/Commands/UpdateExpenseHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Expenses.DTOs;
using SenorArroz.Application.Features.Expenses.Helpers;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Expenses.Commands;

public class UpdateExpenseHandler : IRequestHandler<UpdateExpenseCommand, ExpenseDto>
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly IExpenseCategoryRepository _categoryRepository;
    private readonly IProductRepository _productRepository;
    private readonly IProductCategoryRepository _productCategoryRepository;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IMapper _mapper;

    public UpdateExpenseHandler(
        IExpenseRepository expenseRepository,
        IExpenseCategoryRepository categoryRepository,
        IProductRepository productRepository,
        IProductCategoryRepository productCategoryRepository,
        IApplicationDbContext context,
        ICurrentUser currentUser,
        IMapper mapper)
    {
        _expenseRepository = expenseRepository;
        _categoryRepository = categoryRepository;
        _productRepository = productRepository;
        _productCategoryRepository = productCategoryRepository;
        _context = context;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<ExpenseDto> Handle(UpdateExpenseCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.Role != "admin" && _currentUser.Role != "superadmin")
            throw new BusinessException("No tienes permisos para modificar gastos");

        var expense = await _expenseRepository.GetByIdAsync(request.Id);
        if (expense == null)
            throw new NotFoundException($"Gasto con ID {request.Id} no encontrado");

        if (!await _categoryRepository.ExistsAsync(request.CategoryId))
            throw new NotFoundException($"Categoría con ID {request.CategoryId} no encontrada");

        if (await _expenseRepository.NameExistsInCategoryAsync(request.Name, request.CategoryId, request.Id))
            throw new BusinessException($"Ya existe otro gasto con el nombre '{request.Name}' en esta categoría");

        var menuTargets = request.MenuTargets ?? [];
        ExpenseMenuTargetCommandsHelper.ValidateMenuTargetInputs(menuTargets);
        await ExpenseMenuTargetCommandsHelper.EnsureTargetsExistAsync(
            menuTargets,
            _productRepository,
            _productCategoryRepository,
            cancellationToken);

        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            expense.Name = request.Name.Trim();
            expense.CategoryId = request.CategoryId;
            expense.Unit = request.Unit;

            await _expenseRepository.UpdateAsync(expense);

            await ExpenseMenuTargetCommandsHelper.ReplaceMenuTargetsAsync(
                request.Id,
                menuTargets,
                _context,
                cancellationToken);

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        var updated = await _expenseRepository.GetByIdAsync(request.Id);
        if (updated == null)
            throw new BusinessException("Error al actualizar el gasto");

        var dto = _mapper.Map<ExpenseDto>(updated);
        await ExpenseMenuTargetCommandsHelper.EnrichMenuTargetsAsync(dto, _context, cancellationToken);
        return dto;
    }
}
