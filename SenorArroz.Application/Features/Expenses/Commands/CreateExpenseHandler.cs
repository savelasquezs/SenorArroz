// SenorArroz.Application/Features/Expenses/Commands/CreateExpenseHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Expenses.DTOs;
using SenorArroz.Application.Features.Expenses.Helpers;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Expenses.Commands;

public class CreateExpenseHandler : IRequestHandler<CreateExpenseCommand, ExpenseDto>
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly IExpenseCategoryRepository _categoryRepository;
    private readonly IProductRepository _productRepository;
    private readonly IProductCategoryRepository _productCategoryRepository;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IMapper _mapper;

    public CreateExpenseHandler(
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

    public async Task<ExpenseDto> Handle(CreateExpenseCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.Role != "admin" && _currentUser.Role != "superadmin")
            throw new BusinessException("No tienes permisos para crear gastos");

        if (!await _categoryRepository.ExistsAsync(request.CategoryId))
            throw new NotFoundException($"Categoría con ID {request.CategoryId} no encontrada");

        if (await _expenseRepository.NameExistsInCategoryAsync(request.Name, request.CategoryId))
            throw new BusinessException($"Ya existe un gasto con el nombre '{request.Name}' en esta categoría");

        var menuTargets = request.MenuTargets ?? [];
        ExpenseMenuTargetCommandsHelper.ValidateMenuTargetInputs(menuTargets);
        await ExpenseMenuTargetCommandsHelper.EnsureTargetsExistAsync(
            menuTargets,
            _productRepository,
            _productCategoryRepository,
            cancellationToken);

        int newExpenseId;
        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var expense = new Expense
            {
                Name = request.Name.Trim(),
                CategoryId = request.CategoryId,
                Unit = request.Unit,
            };

            _context.Expenses.Add(expense);
            await _context.SaveChangesAsync(cancellationToken);
            newExpenseId = expense.Id;

            await ExpenseMenuTargetCommandsHelper.ReplaceMenuTargetsAsync(
                newExpenseId,
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

        var created = await _expenseRepository.GetByIdAsync(newExpenseId, cancellationToken);
        if (created == null)
            throw new BusinessException("Error al crear el gasto");

        var dto = _mapper.Map<ExpenseDto>(created);
        await ExpenseMenuTargetCommandsHelper.EnrichMenuTargetsAsync(dto, _context, cancellationToken);
        return dto;
    }
}
