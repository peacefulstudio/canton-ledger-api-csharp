// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Canton.Ledger.Kernel.Authentication.TokenGeneration;

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
