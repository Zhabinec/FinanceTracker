namespace Finance.Application.Auth.Register;

public enum RegisterUserStatus
{
    Success,
    InvalidInput,
    EmailAlreadyExists
}

public sealed record RegisterUserResult(
    RegisterUserStatus Status,
    RegisteredUserDto? User,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static RegisterUserResult Success(RegisteredUserDto user) =>
        new(RegisterUserStatus.Success, user, EmptyErrors);

    public static RegisterUserResult Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(RegisterUserStatus.InvalidInput, null, errors);

    public static RegisterUserResult EmailTaken() =>
        new(
            RegisterUserStatus.EmailAlreadyExists,
            null,
            new Dictionary<string, string[]>
            {
                ["email"] = ["An account with this email already exists."]
            });

    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>();
}
