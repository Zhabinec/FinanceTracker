using Finance.Domain.Transactions;

namespace Finance.Domain.Categories;

public static class StarterCategories
{
    public static IReadOnlyList<Category> Create(Guid userId) =>
    [
        Category.Create(userId, "Продукты", TransactionType.Expense),
        Category.Create(userId, "Транспорт", TransactionType.Expense),
        Category.Create(userId, "Дом", TransactionType.Expense),
        Category.Create(userId, "Здоровье", TransactionType.Expense),
        Category.Create(userId, "Развлечения", TransactionType.Expense),
        Category.Create(userId, "Зарплата", TransactionType.Income),
        Category.Create(userId, "Подработка", TransactionType.Income),
    ];
}
