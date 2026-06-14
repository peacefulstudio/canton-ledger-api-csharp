// Copyright 2026 Peaceful Studio OÜ

using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Canton.Ledger.Auth.TokenGeneration;

/// <summary>
/// Validates <see cref="ClientCredentialsOptions"/> through its
/// <see cref="IValidatableObject"/> implementation. Used instead of
/// <c>ValidateDataAnnotations</c> because <c>Validator.TryValidateObject</c> reads every
/// public property getter, so the throwing
/// <see cref="ClientCredentialsOptions.TokenGenerationEndpoint"/> getter would surface
/// misconfiguration as <see cref="System.Reflection.TargetInvocationException"/> instead
/// of <see cref="OptionsValidationException"/>.
/// </summary>
internal sealed class ClientCredentialsOptionsValidator : IValidateOptions<ClientCredentialsOptions>
{
    public ValidateOptionsResult Validate(string? name, ClientCredentialsOptions options)
    {
        if (name != Options.DefaultName)
            return ValidateOptionsResult.Skip;

        var failures = options.Validate(new ValidationContext(options))
            .Select(result => result.ErrorMessage ?? "ClientCredentialsOptions validation failed.")
            .ToList();

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
