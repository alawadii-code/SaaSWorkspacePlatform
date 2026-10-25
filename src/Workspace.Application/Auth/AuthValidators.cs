using FluentValidation;

namespace Workspace.Application.Auth;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(254)
            .EmailAddress();

        RuleFor(x => x.DisplayName)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(80);

        // Password rules live in PasswordPolicy so the same checks apply
        // wherever a password is set, and can be unit-tested on their own.
        RuleFor(x => x.Password)
            .Custom((password, context) =>
            {
                var request = context.InstanceToValidate;
                var error = PasswordPolicy.Validate(password, request.Email, request.DisplayName);

                if (error is not null)
                {
                    context.AddFailure(nameof(RegisterRequest.Password), error);
                }
            });
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        // Deliberately loose: login must not reveal the password policy, and a
        // malformed address simply fails to match any account.
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(PasswordPolicy.MaxLength);
    }
}
