// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Canton.Ledger.Kernel.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public class OptionsServiceCollectionExtensionsTests
{
    private sealed class SampleOptions
    {
        [Required]
        public string? Name { get; set; }
    }

    private static IConfiguration ConfigWith(string? name) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Name"] = name })
            .Build();

    [Fact]
    public void AddValidatedOptions_configuration_binds_options_from_configuration()
    {
        var services = new ServiceCollection();

        services.AddValidatedOptions<SampleOptions>(ConfigWith("bound"));

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<SampleOptions>>().Value.Name.Should().Be("bound");
    }

    [Fact]
    public void AddValidatedOptions_action_configures_options_from_delegate()
    {
        var services = new ServiceCollection();

        services.AddValidatedOptions<SampleOptions>(o => o.Name = "configured");

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<SampleOptions>>().Value.Name.Should().Be("configured");
    }

    [Fact]
    public void AddValidatedOptions_configuration_returns_a_builder_for_the_default_named_options()
    {
        var services = new ServiceCollection();

        var builder = services.AddValidatedOptions<SampleOptions>(ConfigWith("x"));

        builder.Name.Should().Be(Options.DefaultName);
    }

    [Fact]
    public void AddValidatedOptions_action_returns_a_builder_for_the_default_named_options()
    {
        var services = new ServiceCollection();

        var builder = services.AddValidatedOptions<SampleOptions>(_ => { });

        builder.Name.Should().Be(Options.DefaultName);
    }

    [Fact]
    public void AddValidatedOptions_configuration_wires_ValidateOnStart_so_invalid_options_fail_at_start()
    {
        var services = new ServiceCollection();
        services.AddValidatedOptions<SampleOptions>(ConfigWith(null)).ValidateDataAnnotations();

        var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().Contain(f => f.Contains(nameof(SampleOptions.Name), StringComparison.Ordinal));
    }

    [Fact]
    public void AddValidatedOptions_action_wires_ValidateOnStart_so_invalid_options_fail_at_start()
    {
        var services = new ServiceCollection();
        services.AddValidatedOptions<SampleOptions>(_ => { }).ValidateDataAnnotations();

        var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void AddValidatedOptions_configuration_lets_valid_options_resolve()
    {
        var services = new ServiceCollection();
        services.AddValidatedOptions<SampleOptions>(ConfigWith("ok")).ValidateDataAnnotations();

        var provider = services.BuildServiceProvider();
        provider.Invoking(p => p.GetRequiredService<IStartupValidator>().Validate())
            .Should().NotThrow();
        provider.GetRequiredService<IOptions<SampleOptions>>().Value.Name.Should().Be("ok");
    }

    [Fact]
    public void AddValidatedOptions_configuration_throws_for_null_services()
    {
        IServiceCollection services = null!;

        var act = () => services.AddValidatedOptions<SampleOptions>(new ConfigurationBuilder().Build());

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddValidatedOptions_configuration_throws_for_null_configuration()
    {
        var services = new ServiceCollection();

        var act = () => services.AddValidatedOptions<SampleOptions>((IConfiguration)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddValidatedOptions_action_throws_for_null_services()
    {
        IServiceCollection services = null!;

        var act = () => services.AddValidatedOptions<SampleOptions>(_ => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddValidatedOptions_action_throws_for_null_configure()
    {
        var services = new ServiceCollection();

        var act = () => services.AddValidatedOptions<SampleOptions>((Action<SampleOptions>)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configure");
    }
}
